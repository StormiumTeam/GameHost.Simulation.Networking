using Unity.Mathematics;

namespace Revolution.Utils
{
	public struct QuantizedFloat3
	{
		public int3 Result;

		public int this[int i]
		{
			get => Result[i];
			set => Result[i] = value;
		}

		public void Set(int quantization, float3 f)
		{
			for (var v = 0; v != 3; v++)
				f[v] = math.isnan(f[v]) ? 0.0f : f[v];
			
			Result = (int3) (f * quantization);
		}

		public float3 Get(float deQuantization)
		{
			return (float3) Result * deQuantization;
		}

		public override string ToString()
		{
			return $"qf3({Result.x}, {Result.y}, {Result.z})";
		}
	}
	
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
			for (var v = 0; v != 4; v++)
				f[v] = math.isnan(f[v]) ? 0.0f : f[v];
			
			Result = (int4) (f * quantization);
		}

		public float4 Get(float deQuantization)
		{
			return (float4) Result * deQuantization;
		}

		public override string ToString()
		{
			return $"qf4({Result.x}, {Result.y}, {Result.z}, {Result.w})";
		}
	}
}