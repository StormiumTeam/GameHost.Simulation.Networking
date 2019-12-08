using Unity.Collections;
using Unity.Entities;

namespace Revolution
{
	/// <summary>
	///     Get the entity components a system represent
	/// </summary>
	public interface IEntityComponents
	{
		NativeArray<ComponentType> EntityComponents { get; }
	}
}