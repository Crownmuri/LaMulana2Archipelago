// Inlined from LM2RandomiserMod — removes patched Assembly-CSharp.dll dependency.
namespace LM2RandomiserMod
{
    public class ShopItem
    {
        public LaMulana2RandomizerShared.ItemID ID;
        public int Multiplier;
        public ShopItem(LaMulana2RandomizerShared.ItemID id, int multiplier)
        {
            ID = id;
            Multiplier = multiplier;
        }
    }
}
