namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	public enum NetCodeMessageType : byte
	{
		/// <summary>
		/// Server confirm the connection of a client by sending client instigator id.
		/// </summary>
		ClientConnection = 1,

		/// <summary>
		/// Client and server send snapshot data to their counterpart.
		/// </summary>
		Snapshot = 10,

		/// <summary>
		/// Server send available snapshots systems to clients
		/// </summary>
		/// <remarks>
		/// The client isn't allowed to call this message.
		/// (Since it could represent some security risks...)
		/// </remarks>
		SendSnapshotSystems = 11,
	}
}