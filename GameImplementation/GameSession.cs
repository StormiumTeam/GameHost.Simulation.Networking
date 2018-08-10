using System;
using System.Net;
using LiteNetLib.Utils;
using package.stormiumteam.networking;
using Unity.Entities;
using UnityEngine;

namespace GameImplementation
{
    public class GameSession
    {
        public bool IsConnected;
        public NetworkInstance LocalInstance;
        public NetworkInstance ConnectedInstance;

        public string ConnectAddress;
        public int ConnectPort;
        public int LocalPort;

        public NetDataWriter ConnectData;

        public void ConnectTo()
        {
            var world = World.Active;
            var connectionCreator = new NetworkSelfConnectionCreator()
            {
                ManagerAddress = "127.0.0.1",
                ManagerPort    = (short) LocalPort,
            };

            NetworkConnectionCreator.ConnectToNetwork(world.GetExistingManager<NetworkManager>(),
                ConnectData,
                connectionCreator,
                new IPEndPoint(IPAddress.Parse(ConnectAddress), ConnectPort),
                out LocalInstance,
                out ConnectedInstance);
        }

        public void Create()
        {
            var world = World.Active;
            var connectionCreator = new NetworkSelfConnectionCreator()
            {
                ManagerAddress = "127.0.0.1",
                ManagerPort    = (short) LocalPort,
            };

            NetworkConnectionCreator.CreateNetwork(world.GetExistingManager<NetworkManager>(),
                connectionCreator,
                out LocalInstance);
        }

        public void Disconnect()
        {
            try
            {
                LocalInstance?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                LocalInstance = null;
            }

            try
            {
                ConnectedInstance?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                ConnectedInstance = null;
            }
        }
    }

    public struct ReadOnlyGameSession
    {
        private GameSession m_Instance;

        public ReadOnlyGameSession(GameSession session)
        {
            m_Instance = session;
        }
    }
}