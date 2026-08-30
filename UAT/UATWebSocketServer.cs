using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LaMulana2Archipelago.UAT
{
    /// <summary>
    /// Minimal RFC 6455 WebSocket server, loopback only.
    ///
    /// Hand-rolled rather than taken from a library: the plugin targets net35,
    /// where HttpListener's AcceptWebSocket support does not exist on Mono, and
    /// websocket-sharp ships here as a transitive Archipelago dependency that
    /// the Debug build deliberately drops (see ExcludeWebSocketSharp in the
    /// csproj) -- so it cannot be relied on. UAT needs very little of the
    /// protocol: small text frames, one or two local clients, no TLS, no
    /// extensions, no compression.
    ///
    /// Threading: one accept thread, one thread per client. Sends are
    /// serialised per client by a lock on its stream, so any thread may
    /// Broadcast.
    /// </summary>
    internal sealed class UATWebSocketServer
    {
        // Fixed by the UAT spec: clients probe 65399 first, then 44444.
        // Neither is configurable client-side, so there is no point exposing a
        // port setting -- a custom port would simply never be found.
        public const int PrimaryPort = 65399;
        public const int FallbackPort = 44444;

        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int MaxMessageBytes = 4 * 1024 * 1024;

        private sealed class Client
        {
            public TcpClient Tcp;
            public NetworkStream Stream;
            public readonly object SendLock = new object();
            public volatile bool Closed;
        }

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private readonly List<Client> _clients = new List<Client>();
        private readonly object _clientsLock = new object();

        /// <summary>Raised on a client thread with the text payload received.</summary>
        public Action<object, string> OnMessage;

        /// <summary>Raised on a client thread once the handshake completes.</summary>
        public Action<object> OnConnect;

        public int Port { get; private set; }
        public bool Running { get { return _running; } }

        public bool Start()
        {
            if (_running) return true;

            foreach (int port in new[] { PrimaryPort, FallbackPort })
            {
                try
                {
                    // Loopback only. The tracker runs on the same machine, and
                    // binding the wildcard address would expose the player's
                    // seed to the local network.
                    _listener = new TcpListener(IPAddress.Loopback, port);
                    _listener.Start();
                    Port = port;
                    break;
                }
                catch (SocketException ex)
                {
                    Plugin.Log.LogWarning($"[UAT] Port {port} unavailable: {ex.Message}");
                    _listener = null;
                }
            }

            if (_listener == null)
            {
                Plugin.Log.LogError(
                    $"[UAT] Could not bind {PrimaryPort} or {FallbackPort}; autotracking is off. " +
                    "Another tracker or UAT server is probably already running.");
                return false;
            }

            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "UAT accept" };
            _acceptThread.Start();
            Plugin.Log.LogInfo($"[UAT] Listening on ws://127.0.0.1:{Port}");
            return true;
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;

            lock (_clientsLock)
            {
                foreach (var c in _clients) CloseClient(c);
                _clients.Clear();
            }
            Plugin.Log.LogInfo("[UAT] Server stopped");
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient tcp;
                try
                {
                    tcp = _listener.AcceptTcpClient();
                }
                catch
                {
                    // Stop() closes the listener out from under us; that is the
                    // normal way out of this loop.
                    if (_running) Plugin.Log.LogWarning("[UAT] Accept failed");
                    return;
                }

                var client = new Client { Tcp = tcp, Stream = tcp.GetStream() };
                lock (_clientsLock) _clients.Add(client);

                var t = new Thread(() => ClientLoop(client))
                {
                    IsBackground = true,
                    Name = "UAT client",
                };
                t.Start();
            }
        }

        private void ClientLoop(Client client)
        {
            try
            {
                if (!Handshake(client))
                {
                    CloseClient(client);
                    return;
                }

                Plugin.Log.LogInfo("[UAT] Client connected");
                var onConnect = OnConnect;
                if (onConnect != null) onConnect(client);

                var message = new MemoryStream();
                while (_running && !client.Closed)
                {
                    int opcode;
                    byte[] payload;
                    if (!ReadFrame(client.Stream, out opcode, out payload)) break;

                    switch (opcode)
                    {
                        case 0x0: // continuation
                        case 0x1: // text
                        case 0x2: // binary -- UAT is text, but buffer it the same
                            message.Write(payload, 0, payload.Length);
                            if (message.Length > MaxMessageBytes)
                            {
                                Plugin.Log.LogWarning("[UAT] Oversized message, dropping client");
                                CloseClient(client);
                                return;
                            }
                            // ReadFrame only returns whole frames and reports FIN
                            // through _lastFrameWasFinal.
                            if (_lastFrameWasFinal)
                            {
                                string text = Encoding.UTF8.GetString(message.ToArray());
                                message.SetLength(0);
                                var onMessage = OnMessage;
                                if (onMessage != null) onMessage(client, text);
                            }
                            break;

                        case 0x8: // close
                            CloseClient(client);
                            return;

                        case 0x9: // ping -> pong, echoing the payload
                            SendFrame(client, 0xA, payload);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug("[UAT] Client loop ended: " + ex.Message);
            }
            finally
            {
                CloseClient(client);
                lock (_clientsLock) _clients.Remove(client);
                Plugin.Log.LogInfo("[UAT] Client disconnected");
            }
        }

        // Set by ReadFrame; only read on the same client thread that called it.
        [ThreadStatic] private static bool _lastFrameWasFinal;

        private static bool Handshake(Client client)
        {
            string request = ReadHttpRequest(client.Stream);
            if (request == null) return false;

            string key = null;
            foreach (string line in request.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    key = trimmed.Substring("Sec-WebSocket-Key:".Length).Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(key))
            {
                Plugin.Log.LogWarning("[UAT] Handshake without Sec-WebSocket-Key");
                return false;
            }

            string accept;
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid));
                accept = Convert.ToBase64String(hash);
            }

            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
            client.Stream.Write(response, 0, response.Length);
            client.Stream.Flush();
            return true;
        }

        private static string ReadHttpRequest(NetworkStream stream)
        {
            var buffer = new MemoryStream();
            var one = new byte[1];
            // Read to the blank line that ends the headers. Bounded so a
            // non-WebSocket client poking the port cannot hold the thread.
            while (buffer.Length < 8192)
            {
                int read = stream.Read(one, 0, 1);
                if (read <= 0) return null;
                buffer.WriteByte(one[0]);

                byte[] b = buffer.GetBuffer();
                long n = buffer.Length;
                if (n >= 4 && b[n - 4] == '\r' && b[n - 3] == '\n' && b[n - 2] == '\r' && b[n - 1] == '\n')
                    return Encoding.ASCII.GetString(b, 0, (int)n);
            }
            return null;
        }

        private static bool ReadFrame(NetworkStream stream, out int opcode, out byte[] payload)
        {
            opcode = 0;
            payload = null;

            var header = new byte[2];
            if (!ReadExact(stream, header, 2)) return false;

            _lastFrameWasFinal = (header[0] & 0x80) != 0;
            opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long length = header[1] & 0x7F;

            if (length == 126)
            {
                var ext = new byte[2];
                if (!ReadExact(stream, ext, 2)) return false;
                length = (ext[0] << 8) | ext[1];
            }
            else if (length == 127)
            {
                var ext = new byte[8];
                if (!ReadExact(stream, ext, 8)) return false;
                length = 0;
                for (int i = 0; i < 8; i++) length = (length << 8) | ext[i];
            }

            if (length < 0 || length > MaxMessageBytes) return false;

            byte[] mask = null;
            if (masked)
            {
                mask = new byte[4];
                if (!ReadExact(stream, mask, 4)) return false;
            }

            payload = new byte[length];
            if (length > 0 && !ReadExact(stream, payload, (int)length)) return false;

            // Every client-to-server frame is masked (RFC 6455 §5.3).
            if (masked)
                for (int i = 0; i < payload.Length; i++)
                    payload[i] = (byte)(payload[i] ^ mask[i % 4]);

            return true;
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }

        public void Send(object clientHandle, string text)
        {
            var client = clientHandle as Client;
            if (client == null) return;
            SendFrame(client, 0x1, Encoding.UTF8.GetBytes(text));
        }

        public void Broadcast(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            Client[] snapshot;
            lock (_clientsLock) snapshot = _clients.ToArray();
            foreach (var c in snapshot) SendFrame(c, 0x1, data);
        }

        public int ClientCount
        {
            get { lock (_clientsLock) return _clients.Count; }
        }

        private static void SendFrame(Client client, int opcode, byte[] payload)
        {
            if (client.Closed) return;
            if (payload == null) payload = new byte[0];

            var header = new List<byte>(10) { (byte)(0x80 | opcode) };
            // Server-to-client frames are never masked.
            if (payload.Length < 126)
            {
                header.Add((byte)payload.Length);
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                header.Add(126);
                header.Add((byte)(payload.Length >> 8));
                header.Add((byte)(payload.Length & 0xFF));
            }
            else
            {
                header.Add(127);
                for (int i = 7; i >= 0; i--)
                    header.Add((byte)((long)payload.Length >> (8 * i) & 0xFF));
            }

            try
            {
                lock (client.SendLock)
                {
                    if (client.Closed) return;
                    byte[] head = header.ToArray();
                    client.Stream.Write(head, 0, head.Length);
                    if (payload.Length > 0) client.Stream.Write(payload, 0, payload.Length);
                    client.Stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug("[UAT] Send failed, closing client: " + ex.Message);
                CloseClient(client);
            }
        }

        private static void CloseClient(Client client)
        {
            if (client.Closed) return;
            client.Closed = true;
            try { if (client.Stream != null) client.Stream.Close(); } catch { }
            try { if (client.Tcp != null) client.Tcp.Close(); } catch { }
        }
    }
}
