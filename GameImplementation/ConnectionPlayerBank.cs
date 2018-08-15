using System.Collections.Generic;
using package.stormiumteam.networking;
using package.stormiumteam.networking.game;
using package.stormiumteam.shared;
using package.stormiumteam.shared.online;
using Unity.Entities;

namespace GameImplementation
{
    public class ConnectionPlayerBank : NetworkConnectionSystem
    {
        private Dictionary<long, GamePlayer> m_AllPlayers = new Dictionary<long, GamePlayer>();
        
        protected override void OnUpdate()
        {
            
        }

        protected override void OnDestroyManager()
        {
            m_AllPlayers.Clear();
            m_AllPlayers = null;
        }

        /// <summary>
        /// Register a new player to the connection
        /// </summary>
        /// <param name="index">The server target</param>
        /// <param name="player">The player</param>
        public void RegisterPlayer(long index, GamePlayer player)
        {
            m_AllPlayers[index] = player;

            player.WorldPointer.SetOrAddComponentData(new ClientPlayerServerPlayerLink(index));
        }

        /// <summary>
        /// Unregister a player
        /// </summary>
        /// <param name="index">The server target</param>
        public void UnregisterPlayer(long index)
        {
            if (m_AllPlayers.ContainsKey(index))
            {
                var player = m_AllPlayers[index];
                player.WorldPointer.RemoveComponentIfExist<ClientPlayerServerPlayerLink>();
                
                m_AllPlayers[index] = new GamePlayer();
            }
        }

        /// <summary>
        /// Get a player from the server
        /// </summary>
        /// <param name="index">The server target</param>
        /// <returns>Return a player</returns>
        public GamePlayer Get(long index)
        {
            return m_AllPlayers.ContainsKey(index) ? m_AllPlayers[index] : new GamePlayer();
        }
        
        public GamePlayer Get(Entity netEntity)
        {
            var index = StMath.DoubleIntToLong(netEntity.Index, netEntity.Version);
            return m_AllPlayers.ContainsKey(index) ? m_AllPlayers[index] : new GamePlayer();
        }
    }
}