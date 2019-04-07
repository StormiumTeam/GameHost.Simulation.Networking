using package.stormiumteam.shared;

namespace StormiumShared.Core.Networking
{
	public struct CustomBitMask
	{
		public byte Mask;
		public byte Position;

		public bool this[int index]
		{
			get => MainBit.GetBitAt(Mask, Position) == 1;
			set => MainBit.SetBitAt(ref Mask, Position, value ? (byte) 1 : (byte) 0);
		}
	}
}