using System.Collections.Generic;

namespace LaMulana2Archipelago.Managers
{
    public struct ShopCell
    {
        public int ShopId, PageId, CellId, TokenIndex;
        public ShopCell(int s, int p, int c, int t) { ShopId = s; PageId = p; CellId = c; TokenIndex = t; }
    }

    public static class ShopCellMap
    {
        // Key: (shopId, pageId, cellId)  →  Value: AP location name (must match your world's location names exactly)
        public static readonly Dictionary<ShopCell, string> CellToLocation = new()
        {
            // Nebur
            { new ShopCell(2, 5, 3, 7), "[VOD C-4] Nebur Shop 1" },
            { new ShopCell(2, 6, 3, 7), "[VOD C-4] Nebur Shop 2" },
            { new ShopCell(2, 7, 3, 7), "[VOD C-4] Nebur Shop 3" },

            // Modro
            { new ShopCell(1, 6, 3, 7), "[VOD E-4] Modro Shop 1" },
            { new ShopCell(1, 7, 3, 7), "[VOD E-4] Modro Shop 2" },
            { new ShopCell(1, 8, 3, 7), "[VOD E-4] Modro Shop 3" },

            // Sidro
            { new ShopCell(0, 6, 3, 7), "[VOD E-4] Sidro Shop 1" },
            { new ShopCell(0, 7, 3, 7), "[VOD E-4] Sidro Shop 2" },
            { new ShopCell(0, 8, 3, 7), "[VOD E-4] Sidro Shop 3" },

            // Hiner
            { new ShopCell(3, 6, 3, 7), "[GOG B-4] Hiner Shop 1" },
            { new ShopCell(3, 7, 3, 7), "[GOG B-4] Hiner Shop 2" },
            { new ShopCell(3, 8, 3, 7), "[GOG B-4] Hiner Shop 3" },
            { new ShopCell(4, 5, 3, 7), "[GOG B-4] Hiner Shop 1" },
            { new ShopCell(4, 6, 3, 7), "[GOG B-4] Hiner Shop 2" },
            { new ShopCell(4, 7, 3, 7), "[GOG B-4] Hiner Shop 4 (3 Guardians)" },

            // Korobock
            { new ShopCell(5, 5, 3, 1), "[ROY C-5] Korobock Shop 1" },
            { new ShopCell(5, 6, 3, 1), "[ROY C-5] Korobock Shop 2" },
            { new ShopCell(5, 7, 3, 7), "[ROY C-5] Korobock Shop 3" },

            // Pym
            { new ShopCell(6, 5, 3, 7), "[ANN E-1] Pym Shop 1" },
            { new ShopCell(6, 6, 3, 7), "[ANN E-1] Pym Shop 2" },
            { new ShopCell(6, 7, 3, 7), "[ANN E-1] Pym Shop 3" },

            // Peibalusa
            { new ShopCell(7, 5, 3, 7), "[IB E-1] Peibalusa Shop 1" },
            { new ShopCell(7, 6, 3, 7), "[IB E-1] Peibalusa Shop 2" },
            { new ShopCell(7, 7, 3, 7), "[IB E-1] Peibalusa Shop 3" },

            // Hiro Roderick
            { new ShopCell(8, 5, 3, 7), "[IB G-6] Hiro Roderick Shop 1" },
            { new ShopCell(8, 6, 3, 7), "[IB G-6] Hiro Roderick Shop 2" },
            { new ShopCell(8, 7, 3, 7), "[IB G-6] Hiro Roderick Shop 3" },

            // Mino
            { new ShopCell(11, 5, 3, 7), "[IT C-4] Mino Shop 1" },
            { new ShopCell(11, 6, 3, 7), "[IT C-4] Mino Shop 2" },
            { new ShopCell(11, 7, 3, 7), "[IT C-4] Mino Shop 3" },

            // BTK
            { new ShopCell(9, 5, 3, 7), "[IT A-1] BTK Shop 1" },
            { new ShopCell(9, 6, 3, 7), "[IT A-1] BTK Shop 2" },
            { new ShopCell(9, 7, 3, 7), "[IT A-1] BTK Shop 3" },

            // Shuhoka
            { new ShopCell(12, 5, 3, 7), "[DF B-5] Shuhoka Shop 1 (S2)" },
            { new ShopCell(12, 6, 3, 7), "[DF B-5] Shuhoka Shop 2 (S2)" },
            { new ShopCell(12, 7, 3, 7), "[DF B-5] Shuhoka Shop 3 (S2)" },

            // Hydlit
            { new ShopCell(13, 5, 3, 7), "[SFG A-5] Hydlit Shop 1" },
            { new ShopCell(13, 6, 3, 7), "[SFG A-5] Hydlit Shop 2" },
            { new ShopCell(13, 7, 3, 7), "[SFG A-5] Hydlit Shop 3" },

            // Aytum
            { new ShopCell(14, 5, 3, 1), "[GOTD B-3] Aytum Shop 1" },
            { new ShopCell(14, 6, 3, 1), "[GOTD B-3] Aytum Shop 2" },
            { new ShopCell(14, 7, 3, 1), "[GOTD B-3] Aytum Shop 3" },

            // Ash Geen
            { new ShopCell(15, 5, 3, 7), "[TS C-4] Ash Geen Shop 1" },
            { new ShopCell(15, 6, 3, 7), "[TS C-4] Ash Geen Shop 2" },
            { new ShopCell(15, 7, 3, 7), "[TS C-4] Ash Geen Shop 3" },

            // Megarock
            { new ShopCell(16, 5, 3, 7), "[HL A-3] Megarock Shop 1" },
            { new ShopCell(16, 6, 3, 7), "[HL A-3] Megarock Shop 2" },
            { new ShopCell(16, 7, 3, 7), "[HL A-3] Megarock Shop 3" },

            // Bargain Duck
            { new ShopCell(17, 5, 3, 7), "[VAL D-4] Bargain Duck Shop 1" },
            { new ShopCell(17, 6, 3, 7), "[VAL D-4] Bargain Duck Shop 2" },
            { new ShopCell(17, 7, 3, 7), "[VAL D-4] Bargain Duck Shop 3" },

            // Kero
            { new ShopCell(18, 5, 3, 7), "[DSLM C-2] Kero Shop 1" },
            { new ShopCell(18, 6, 3, 7), "[DSLM C-2] Kero Shop 2" },
            { new ShopCell(18, 7, 3, 7), "[DSLM C-2] Kero Shop 3" },

            // Venum
            { new ShopCell(19, 5, 3, 13), "[AC C-6] Venum Shop 1" },
            { new ShopCell(19, 6, 3, 13), "[AC C-6] Venum Shop 2" },
            { new ShopCell(19, 7, 3, 13), "[AC C-6] Venum Shop 3" },

            // Fairylan
            { new ShopCell(20, 5, 3, 7), "[HOM G-4] Fairylan Shop 1" },
            { new ShopCell(20, 6, 3, 7), "[HOM G-4] Fairylan Shop 2" },
            { new ShopCell(20, 7, 3, 7), "[HOM G-4] Fairylan Shop 3" },

            //BTK--RANDO
            { new ShopCell(10, 5, 3, 7), "[RANDO] Starting Shop 1" },
            { new ShopCell(10, 6, 3, 7), "[RANDO] Starting Shop 2" },
            { new ShopCell(10, 7, 3, 7), "[RANDO] Starting Shop 3" },
        };
    }
}