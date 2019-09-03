using System;
using System.Runtime.InteropServices;
using Unity.Burst;

namespace Revolution
{
	public class BurstDelegate<TDelegate>
		where TDelegate : Delegate
	{
		private bool                       m_IsCompiled;
		private FunctionPointer<TDelegate> m_BurstResult;

		public TDelegate Origin;
		public bool      WasBurstCompiled;

		public BurstDelegate(TDelegate origin, bool lazy = false)
		{
			WasBurstCompiled = false;
			Origin           = origin;

			m_IsCompiled  = false;
			m_BurstResult = default;

			if (!lazy)
				Get();
		}

		public FunctionPointer<TDelegate> Get()
		{
			if (!m_IsCompiled)
			{
				m_IsCompiled = true;
				
				if (!BurstCompiler.Options.IsEnabled || !BurstCompiler.Options.EnableBurstCompilation)
				{
					return new FunctionPointer<TDelegate>(Marshal.GetFunctionPointerForDelegate(Origin));
				}

				try
				{
					m_BurstResult    = BurstCompiler.CompileFunctionPointer(Origin);
					WasBurstCompiled = true;
				}
				catch
				{
					m_BurstResult    = default;
					WasBurstCompiled = false;
				}
			}

			if (WasBurstCompiled)
				return m_BurstResult;
			return new FunctionPointer<TDelegate>(Marshal.GetFunctionPointerForDelegate(Origin));
		}
	}
}