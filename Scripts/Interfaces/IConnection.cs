using LiteNetLib;

namespace package.stormiumteam.networking
{
    public interface IConnection
    {
        string ManagerAddress { get; set; }
        short  ManagerPort    { get; set; }

        NetManager Manager { get; set; }
    }
}