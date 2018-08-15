using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using DefaultNamespace;
using JetBrains.Annotations;
using LiteNetLib;
using package.stormiumteam.shared.modding;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Experimental.LowLevel;
using package.stormiumteam.shared;
using UnityEngine.Assertions;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking
{
	[DisableAutoCreation]
	public abstract class NetworkConnectionSystem : ComponentSystem
	{
		private World m_MainWorld;
		
		public NetworkInstance NetInstance => NetWorld?.Instance;
		public NetworkWorld    NetWorld    => World as NetworkWorld;

		public World MainWorld
		{
			get { return m_MainWorld = m_MainWorld ?? World.Active; }
		}

		public virtual void OnInstanceGettingReady()
		{

		}

		public virtual void OnInstanceConnected()
		{

		}

		public virtual void OnInstanceBroadcastingData(NetPeerInstance peerInstance)
		{

		}
	}

	public class NetworkInstance : IDisposable
	{
		public static event Action<NetworkInstance, ConnectionType>                  OnNewInstance;
		public static event Action<NetworkInstance, ConnectionType>                  OnInstanceReady;
		public static event Action<NetworkInstance, NetPeerInstance, ConnectionType> OnInstanceBroadcastingData;
		public static event Action<NetworkInstance, ConnectionType>                  OnBeforeDisposingInstance;
		public static event Action<NetworkInstance, ConnectionType>                  OnDisposedInstance;

		private static int s_IncrementId = 0;
		private static List<NetworkInstance> s_AllCreationValidInstances = new List<NetworkInstance>();

		public static NetworkInstance FromId(int id)
		{
			if (s_AllCreationValidInstances.Count <= id)
				return null;
			
			return s_AllCreationValidInstances[id];
		}
		
		public int Id { get; private set; }
		public NetworkConnectionInfo               ConnectionInfo   { get; }
		public NetworkWorld                        World            { get; private set; }
		public NetworkInstance                     Main             { get; private set; }
		public ReadOnlyCollection<NetworkInstance> Interconnections { get; }
		public ReadOnlyCollection<NetUser> AllInUsers { get; internal set; }
		public NetPeerInstance PeerInstance { get; private set; }
		public bool IsDisposed { get; private set; }

		public bool    SelfHost     => IsMainItself && ConnectionInfo.ConnectionType == ConnectionType.Self;
		public bool    IsMainItself => this == Main;
		public bool    IsReady      => m_IsReady;
		public bool    IsConnected  => m_IsConnected;
		public NetUser NetUser      => m_NetUser;

		private  bool                  m_IsReady;
		private  bool                  m_Finalized;
		private  bool                  m_IsConnected;
		private  NetUser               m_NetUser;
		internal List<NetworkInstance> m_Interconnections;

		public NetworkInstance(NetworkConnectionInfo connectionInfo, bool createPeerInstance, bool createWorld)
		{
			Id = s_IncrementId;
			
			m_IsReady        = false;
			m_Finalized      = false;
			m_IsConnected    = false;
			ConnectionInfo   = connectionInfo;
			Interconnections = new ReadOnlyCollection<NetworkInstance>(m_Interconnections = new List<NetworkInstance>());

			if (createPeerInstance)
			{
				var connection     = ConnectionInfo.Creator as IConnection;
				var hostConnection = ConnectionInfo.Creator as IConnectionHost;
				
				Assert.IsTrue(connection != null && hostConnection != null, "connection != null && hostConnection != null");

				var data = new NetworkChannelData
				(
					creator: this,
					id: NetConstants.Channel_Main,
					port: -1,
					configuration: null,
					defaultDelivery: DeliveryMethod.ReliableOrdered
				);
				var channel = new NetworkChannel
				(
					data: data,
					manager: connection.Manager,
					listener: hostConnection.Listener
				);
				
				SetPeerInstance(new NetPeerInstance(this, channel, connection.Manager.GetFirstPeer()));
			}
			if (createWorld) CreateWorld();

			s_IncrementId++; // Only increment if we had no problem
			s_AllCreationValidInstances.Add(this);
		}

		public void SetUser(NetUser user)
		{
			if (user.Index == ulong.MaxValue)
			{
				m_NetUser = this.GetUserManager().Allocate(null);
			}

			m_NetUser = user;
			
			Unity.Entities.World.Active.GetExistingManager<NetworkUserSystem>()
			         .TriggerOnUserEvent(PeerInstance, user, StatusChange.MainUser);
		}

		public void SetPeerInstance(NetPeerInstance peerInstance)
		{
			if (PeerInstance != null) return;

			PeerInstance = peerInstance;
		}
		

		public NetworkWorld CreateWorld()
		{
			if (World != null)
				return World;

			World          = new NetworkWorld(ConnectionInfo);
			World.Instance = this;

			NetworkWorld.AllWorlds.Add(World);

			ECSWorldLoop.FlagAsLoopable(World);
			ECSWorldLoop.Retake();

			return World;
		}

		public void SetMain(NetworkInstance networkInstance, bool forceMain = false)
		{
			if (Main != null && !forceMain)
			{
				throw new Exception("There is already a main instance assigned");
			}

			Main = networkInstance;
		}

		public void FinalizeInstance()
		{
			if (m_Finalized)
				return;
			m_Finalized = true;

			OnNewInstance?.Invoke(this, ConnectionInfo.ConnectionType);
		}

		private void UpdateSystem(PlayerLoopSystem loopSystem, List<Type> knSystem, string methodName, object[] @params)
		{
			if (loopSystem.type != null
			    && !knSystem.Contains(loopSystem.type)
			    && loopSystem.type.IsSubclassOf(typeof(NetworkConnectionSystem)))
			{
				var method = loopSystem.type.GetMethod(methodName);

				Assert.IsFalse(method == null, $"{@params}() == null (from {loopSystem.type.Name})");

				var system = World.GetExistingManager(loopSystem.type);

				if (system != null)
				{
					method?.Invoke(system, @params);

					knSystem.Add(loopSystem.type);
				}
			}

			if (loopSystem.subSystemList != null)
			{
				foreach (var innerLoopSystem in loopSystem.subSystemList)
					UpdateSystem(innerLoopSystem, knSystem, methodName, @params);
			}
		}

		public void SetReady()
		{
			if (m_IsReady)
				return;

			var calledSystems  = new List<Type>();
			var currPlayerLoop = ScriptBehaviourUpdateOrder.CurrentPlayerLoop;
			UpdateSystem(currPlayerLoop, calledSystems, "OnInstanceGettingReady", new object[0]);

			m_IsReady = true;
			OnInstanceReady?.Invoke(this, ConnectionInfo.ConnectionType);
		}

		public void BroadcastData(NetPeerInstance peerInstance)
		{
			if (!m_IsReady)
				throw new Exception($"The instance {World.Name} wasn't ready");

			var calledSystems  = new List<Type>();
			var currPlayerLoop = ScriptBehaviourUpdateOrder.CurrentPlayerLoop;
			UpdateSystem(currPlayerLoop, calledSystems, "OnInstanceBroadcastingData", new object[] {peerInstance});

			OnInstanceBroadcastingData?.Invoke(this, peerInstance, ConnectionInfo.ConnectionType);

			peerInstance.AllBroadcastedDataSent();
			peerInstance.SetInitialized();
		}

		public void SetAsConnected()
		{
			if (m_IsConnected)
				return;

			var calledSystems  = new List<Type>();
			var currPlayerLoop = ScriptBehaviourUpdateOrder.CurrentPlayerLoop;
			UpdateSystem(currPlayerLoop, calledSystems, "OnInstanceConnected", new object[0]);

			m_IsConnected = true;
		}

		public void Dispose()
		{
			try
			{
				Profiler.BeginSample("Invoke OnBeforeDisposingInstance");
				OnBeforeDisposingInstance?.Invoke(this, ConnectionInfo.ConnectionType);
				Profiler.EndSample();
				if (World != null)
				{
					Profiler.BeginSample("Dispose World Instance");
					World.Dispose();
					NetworkWorld.AllWorlds.Remove(World);
					Profiler.EndSample();

					Profiler.BeginSample("Flag world as unloopable");
					ECSWorldLoop.FlagAsUnloopable(World);
					Profiler.EndSample();
					Profiler.BeginSample("Retake Loop");
					ECSWorldLoop.Retake();
					Profiler.EndSample();
				}

				Profiler.BeginSample("invoke OnDisposedInstance");
				OnDisposedInstance?.Invoke(this, ConnectionInfo.ConnectionType);
				Profiler.EndSample();

				World      = null;
				Main       = null;
				IsDisposed = true;

				try
				{
					var networkManager = Unity.Entities.World.Active.GetOrCreateManager<NetworkManager>();
					networkManager.RemoveInstance(this);
				}
				catch
				{
					// ignored
				}
			}
			finally
			{
				// We don't remove the entry, but we set it to null instead.
				if (s_AllCreationValidInstances.Contains(this))
					s_AllCreationValidInstances[Id] = null;
			}
		}
	}

	public partial class NetworkWorld : World
	{
		internal static List<Type> SystemTypes = new List<Type>();

		internal static void AddSystemType(Type type)
		{
			if (!SystemTypes.Contains(type))
			{
				SystemTypes.Add(type);

				Debug.Log("[NetworkWorld.static] Added system type: " + type.FullName);
			}
		}
	}

	public partial class NetworkWorld : World
	{
		public NetworkInstance Instance;

		public new static readonly List<NetworkWorld> AllWorlds = new List<NetworkWorld>();

		public NetworkWorld(NetworkConnectionInfo connectionInfo)
			: base(connectionInfo.ConnectionType + ";" + connectionInfo.Creator.GetAddress())
		{
		}

		internal void CreateSystems()
		{
			// Create systems
			foreach (var systemType in SystemTypes)
			{
				GetOrCreateManager(systemType);
			}

			// Inject update loop
			var list = new List<World>();
			list.Add(Unity.Entities.World.Active);
			list.AddRange(NetworkWorld.AllWorlds);
			ScriptBehaviourUpdateOrder.UpdatePlayerLoop(list.ToArray());
		}
	}
}