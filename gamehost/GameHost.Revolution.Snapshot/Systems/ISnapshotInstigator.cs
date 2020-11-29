using Collections.Pooled;
using DefaultEcs;
using GameHost.Revolution.Snapshot.Serializers;

namespace GameHost.Revolution.Snapshot.Systems
{
	/// <summary>
	///     Represent a snapshot manager. It contains a state, can be bi-directional...
	/// </summary>
	public interface ISnapshotInstigator
	{
		/// <summary>
		///     The ID of this instigator.
		/// </summary>
		/// <remarks>
		///     Generally, a server have 0 as an ID.
		///     When doing networked games, you must want to make sure that all clients have a unique ID.
		/// </remarks>
		public int InstigatorId { get; }

		/// <summary>
		///     The EC storage of this instigator
		/// </summary>
		public Entity Storage { get; }

		/// <summary>
		///     State of the instigator, contains entity, archetype, system data about the previous serialization.
		/// </summary>
		public ISnapshotState State { get; }

		/// <summary>
		///     Mapped serializers to ids.
		/// </summary>
		public PooledDictionary<uint, ISerializer> Serializers { get; }
	}
}