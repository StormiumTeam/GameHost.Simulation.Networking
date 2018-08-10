using System.Net;

namespace package.stormiumteam.networking
{
    public interface IConnectionCreator
    {
        IPEndPoint GetAddress();

        void Init();
    }
}