using package.stormiumteam.shared;

namespace StormiumShared.Core.Networking
{
	public struct CustomBitMask
	{
		public byte Mask;
		public byte Position;

		public bool this[int index]
		{
			get => MainBit.GetBitAt(Mask, (byte) index) == 1;
			set => MainBit.SetBitAt(ref Mask, (byte) index, value ? (byte) 1 : (byte) 0);
		}
	}
}