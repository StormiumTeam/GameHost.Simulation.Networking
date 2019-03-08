using StormiumShared.Core.Networking;
using Unity.Entities;

namespace package.stormiumteam.networking
{
	public struct ComponentDataChangedFromEntity<T>
		where T : struct, IComponentData
	{
		private ComponentDataFromEntity<DataChanged<T>> m_ChangeFromEntity;
		private ComponentDataFromEntity<T> m_DataFromEntity;

		public bool Exists(Entity entity)
		{
			return m_DataFromEntity.Exists(entity);
		}

		public bool HasChange(Entity entity)
		{
			return m_ChangeFromEntity[entity].IsDirty == 1;
		}

		public T this[Entity entity]
		{
			get => m_DataFromEntity[entity];
			set => m_DataFromEntity[entity] = value;
		}

		public ComponentDataChangedFromEntity(ComponentDataFromEntity<DataChanged<T>> change, ComponentDataFromEntity<T> data)
		{
			m_ChangeFromEntity = change;
			m_DataFromEntity = data;
		}

		public ComponentDataChangedFromEntity(ComponentSystemBase componentSystem)
		{
			m_ChangeFromEntity = componentSystem.GetComponentDataFromEntity<DataChanged<T>>();
			m_DataFromEntity = componentSystem.GetComponentDataFromEntity<T>();
		}
	}
}