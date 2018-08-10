namespace package.stormiumteam.networking
{
    public enum ConnectionType
    {
        Unknow = 0,
        
        /// <summary>
        /// All the connections that was connected to our network (eg :clients instance to our main instance)
        /// </summary>
        In = 1,

        /// <summary>
        /// All the created connections that are connected to different network (eg: main instance to servers instance)
        /// </summary>
        Out = 2,
        
        /// <summary>
        /// A selfish connection that can receive clients and connect to server, it's the base of the base.
        /// </summary>
        Self = 3,
    }
}