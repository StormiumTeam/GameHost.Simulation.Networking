using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Revolution
{
	/// <summary>
	///     Represent a system that use Burst delegate to manage entity snapshots
	/// </summary>
	public interface ISystemDelegateForSnapshot
	{
		/// <summary>
		///     The serialize function
		/// </summary>
		FunctionPointer<OnSerializeSnapshot> SerializeDelegate { get; }

		/// <summary>
		///     The deserialize function
		/// </summary>
		FunctionPointer<OnDeserializeSnapshot> DeserializeDelegate { get; }

		NativeString512 NativeName { get; }

		/// <summary>
		///     Called when a snapshot system is beginning to serialize
		/// </summary>
		/// <param name="client">The client entity, the one who will receive the data</param>
		void OnBeginSerialize(Entity client);

		/// <summary>
		///     Called when a snapshot system is beginning to deserialize
		/// </summary>
		/// <param name="client">The client entity, the one who sent the data to us</param>
		void OnBeginDeserialize(Entity client);
	}
}