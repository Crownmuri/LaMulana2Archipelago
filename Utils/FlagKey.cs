using System;

namespace LaMulana2Archipelago.Util
{
    public struct FlagKey
    {
        public int Sheet;
        public int Flag;

        public FlagKey(int sheet, int flag)
        {
            Sheet = sheet;
            Flag = flag;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Sheet * 397) ^ Flag;
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is FlagKey))
                return false;

            var other = (FlagKey)obj;
            return Sheet == other.Sheet && Flag == other.Flag;
        }
    }
}