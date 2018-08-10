using LiteNetLib;

namespace package.stormiumteam.networking
{
    public interface IConnectionHost : IConnection
    {
        EventBasedNetListener Listener { get; set; }
    }
}