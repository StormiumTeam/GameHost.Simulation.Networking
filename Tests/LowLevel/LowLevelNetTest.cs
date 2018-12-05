using ENet;
using UnityEngine;

namespace package.stormiumteam.networking.Tests
{
    public class LowLevelNetTest : MonoBehaviour
    {
        public CreateServerCode ServerCode;
        public CreateClientCode ClientCode;

        public int ServerPort = 9000;

        private void Start()
        {
            if (!Library.Initialize()) Debug.LogWarning("Couldn't initialize ENet");
            
            ServerCode = new CreateServerCode();
            ClientCode = new CreateClientCode();
            
            ServerCode.Port = ServerPort;
            ClientCode.ServerPort = ServerPort;
            
            Debug.Log("Server Start()");
            ServerCode.Start();
            Debug.Log("Client Start()");
            ClientCode.Start();
        }

        private void Update()
        {
            ServerCode.Update();
            ClientCode.Update();
        }

        private void OnDestroy()
        {
            if (!isActiveAndEnabled) return;
            
            ServerCode.Destroy();
            ClientCode.Destroy();
            
            Library.Deinitialize();
        }
    }
}