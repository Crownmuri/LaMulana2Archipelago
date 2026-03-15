using System;

namespace LaMulana2Archipelago.Managers
{
    public static class ItemGrantRecursiveGuard
    {
        private static int _depth = 0;
        public static bool IsGranting => _depth > 0;

        public static IDisposable Begin()
        {
            _depth++;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _depth--;
            }
        }
    }
}