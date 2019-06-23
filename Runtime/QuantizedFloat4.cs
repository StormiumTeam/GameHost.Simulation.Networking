using Unity.Mathematics;

namespace StormiumTeam.Networking.Utilities
{
	public struct QuantizedFloat4
	{
		public int4 Result;
		
		public int this[int i]
		{
			get => Result[i];
			set => Result[i] = value;
		}

		public void Set(int quantization, float4 f)
		{
			Result = (int4) (f * quantization);
		}

		public float4 Get(float deQuantization)
		{
			return (float4) Result * deQuantization;
		}
	}
}