using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Unity.Entities;
using Unity.NetCode;

namespace DefaultNamespace
{
	[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
	public class RegisterSerializerGroup : ComponentSystemGroup
	{}
		
	[UpdateInGroup(typeof(RegisterSerializerGroup))]
	public abstract class AddSerializer<TSerializer, TSnapshot> : ComponentSystem
		where TSerializer : struct, IGhostSerializer<TSnapshot>
		where TSnapshot : unmanaged, ISnapshotData<TSnapshot>
	{
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

		[UsedImplicitly]
		public class SystemGhostSerializer : BaseGhostManageSerializer<TSnapshot, TSerializer>
		{
			public override ComponentTypes GetComponents()
			{
				return World.GetExistingSystem<AddSerializer<TSerializer, TSnapshot>>().GetComponents();
			}
		}
	}
}