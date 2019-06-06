using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Unity.NetCode;
using Unity.Entities;

namespace DefaultNamespace
{
	[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
	public class RegisterSerializerGroup : ComponentSystemGroup
	{
	}


	[UpdateInGroup(typeof(RegisterSerializerGroup))]
	public abstract class AddSerializer<TSerializer, TSnapshot> : ComponentSystem
		where TSerializer : struct, IGhostSerializer<TSnapshot>
		where TSnapshot : unmanaged, ISnapshotData<TSnapshot>
	{
#if UNITY_NETCODE_MODIFIED
		private TSerializer m_Serializer;
		[PublicAPI]
		public int SerializerId { get; private set; }
		
		public TSerializer GetSerializer()
		{
			return m_Serializer;
		}

		public virtual bool WantsPredictionDelta => false;
		public virtual bool WantsSingleHistory   => false;
		public virtual int  Importance           => 1;

		[PublicAPI]
		public abstract ComponentTypes GetComponents();

		protected override void OnCreate()
		{
			base.OnCreate();

			World.GetOrCreateSystem<SystemGhostSerializer>();
			World.GetOrCreateSystem<GhostSerializerCollectionSystem>().TryAdd<TSerializer, TSnapshot>(out m_Serializer);

			SerializerId = m_Serializer.Header.Id;
		}

		protected override void OnUpdate()
		{
		}
#endif

		[UsedImplicitly]
		public class SystemGhostSerializer
#if UNITY_NETCODE_MODIFIED
			: BaseGhostManageSerializer<TSnapshot, TSerializer>
#endif
		{
#if UNITY_NETCODE_MODIFIED
			public override ComponentTypes GetComponents()
			{
				return World.GetExistingSystem<AddSerializer<TSerializer, TSnapshot>>().GetComponents();
			}
#endif
		}
	}
}