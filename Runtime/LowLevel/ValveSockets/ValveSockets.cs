/*
 *  Managed C# wrapper for GameNetworkingSockets library by Valve Software
 *  Copyright (c) 2018 Stanislav Denisov
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a copy
 *  of this software and associated documentation files (the "Software"), to deal
 *  in the Software without restriction, including without limitation the rights
 *  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *  copies of the Software, and to permit persons to whom the Software is
 *  furnished to do so, subject to the following conditions:
 *
 *  The above copyright notice and this permission notice shall be included in all
 *  copies or substantial portions of the Software.
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *  SOFTWARE.
 */

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using UnityEngine;

namespace Valve.Sockets {
	using ListenSocket = UInt32;
	using Connection = UInt32;
	using Microseconds = Int64;

	[Flags]
	public enum SendType {
		Unreliable = 0,
		NoNagle = 1 << 0,
		NoDelay = 1 << 1,
		Reliable = 1 << 3
	}

	public enum IdentityType {
		Invalid = 0,
		IPAddress = 1,
		GenericString = 2,
		GenericBytes = 3,
		SteamID = 16
	}

	public enum ConnectionState {
		None = 0,
		Connecting = 1,
		FindingRoute = 2,
		Connected = 3,
		ClosedByPeer = 4,
		ProblemDetectedLocally = 5
	}

	public enum ConfigurationString {
		ClientForceRelayCluster = 0,
		ClientDebugTicketAddress = 1,
		ClientForceProxyAddr = 2
	}

	public enum ConfigurationValue {
		FakeMessageLossSend = 0,
		FakeMessageLossRecv = 1,
		FakePacketLossSend = 2,
		FakePacketLossRecv = 3,
		FakePacketLagSend = 4,
		FakePacketLagRecv = 5,
		FakePacketReorderSend = 6,
		FakePacketReorderRecv = 7,
		FakePacketReorderTime = 8,
		SendBufferSize = 9,
		MaxRate = 10,
		MinRate = 11,
		NagleTime = 12,
		LogLevelAckRTT = 13,
		LogLevelPacket = 14,
		LogLevelMessage = 15,
		LogLevelPacketGaps = 16,
		LogLevelP2PRendezvous = 17,
		LogLevelRelayPings = 18,
		ClientConsecutitivePingTimeoutsFailInitial = 19,
		ClientConsecutitivePingTimeoutsFail = 20,
		ClientMinPingsBeforePingAccurate = 21,
		ClientSingleSocket = 22,
		IPAllowWithoutAuth = 23,
		TimeoutSecondsInitial = 24,
		TimeoutSecondsConnected = 25,
		FakePacketDupSend = 26,
		FakePacketDupRecv = 27,
		FakePacketDupTimeMax = 28
	}

	public enum Result {
		OK = 1,
		Fail = 2,
		NoConnection = 3,
		InvalidPassword = 5,
		LoggedInElsewhere = 6,
		InvalidProtocolVer = 7,
		InvalidParam = 8,
		FileNotFound = 9,
		Busy = 10,
		InvalidState = 11,
		InvalidName = 12,
		InvalidEmail = 13,
		DuplicateName = 14,
		AccessDenied = 15,
		Timeout = 16,
		Banned = 17,
		AccountNotFound = 18,
		InvalidSteamID = 19,
		ServiceUnavailable = 20,
		NotLoggedOn = 21,
		Pending = 22,
		EncryptionFailure = 23,
		InsufficientPrivilege = 24,
		LimitExceeded = 25,
		Revoked = 26,
		Expired = 27,
		AlreadyRedeemed = 28,
		DuplicateRequest = 29,
		AlreadyOwned = 30,
		IPNotFound = 31,
		PersistFailed = 32,
		LockingFailed = 33,
		LogonSessionReplaced = 34,
		ConnectFailed = 35,
		HandshakeFailed = 36,
		IOFailure = 37,
		RemoteDisconnect = 38,
		ShoppingCartNotFound = 39,
		Blocked = 40,
		Ignored = 41,
		NoMatch = 42,
		AccountDisabled = 43,
		ServiceReadOnly = 44,
		AccountNotFeatured = 45,
		AdministratorOK = 46,
		ContentVersion = 47,
		TryAnotherCM = 48,
		PasswordRequiredToKickSession = 49,
		AlreadyLoggedInElsewhere = 50,
		Suspended = 51,
		Cancelled = 52,
		DataCorruption = 53,
		DiskFull = 54,
		RemoteCallFailed = 55,
		PasswordUnset = 56,
		ExternalAccountUnlinked = 57,
		PSNTicketInvalid = 58,
		ExternalAccountAlreadyLinked = 59,
		RemoteFileConflict = 60,
		IllegalPassword = 61,
		SameAsPreviousValue = 62,
		AccountLogonDenied = 63,
		CannotUseOldPassword = 64,
		InvalidLoginAuthCode = 65,
		AccountLogonDeniedNoMail = 66,
		HardwareNotCapableOfIPT = 67,
		IPTInitError = 68,
		ParentalControlRestricted = 69,
		FacebookQueryError = 70,
		ExpiredLoginAuthCode = 71,
		IPLoginRestrictionFailed = 72,
		AccountLockedDown = 73,
		AccountLogonDeniedVerifiedEmailRequired = 74,
		NoMatchingURL = 75,
		BadResponse = 76,
		RequirePasswordReEntry = 77,
		ValueOutOfRange = 78,
		UnexpectedError = 79,
		Disabled = 80,
		InvalidCEGSubmission = 81,
		RestrictedDevice = 82,
		RegionLocked = 83,
		RateLimitExceeded = 84,
		AccountLoginDeniedNeedTwoFactor = 85,
		ItemDeleted = 86,
		AccountLoginDeniedThrottle = 87,
		TwoFactorCodeMismatch = 88,
		TwoFactorActivationCodeMismatch = 89,
		AccountAssociatedToMultiplePartners = 90,
		NotModified = 91,
		NoMobileDevice = 92,
		TimeNotSynced = 93,
		SmsCodeFailed = 94,
		AccountLimitExceeded = 95,
		AccountActivityLimitExceeded = 96,
		PhoneActivityLimitExceeded = 97,
		RefundToWallet = 98,
		EmailSendFailure = 99,
		NotSettled = 100,
		NeedCaptcha = 101,
		GSLTDenied = 102,
		GSOwnerDenied = 103,
		InvalidItemType = 104,
		IPBanned = 105,
		GSLTExpired = 106,
		InsufficientFunds = 107,
		TooManyPending = 108,
		NoSiteLicensesFound = 109,
		WGNetworkSendExceeded = 110
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct StatusInfo {
		private const int callback = Library.socketsCallbacks + 1;
		public Connection connection;
		public ConnectionInfo connectionInfo;
		private int socketState;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ConnectionInfo {
		public NetworkingIdentity identity;
		public long userData;
		public ListenSocket listenSocket;
		public Address address;
		private ushort pad1;
		private uint popRemote;
		private uint popRelay;
		public ConnectionState state;
		public int endReason;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string endDebug;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string connectionDescription;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ConnectionStatus {
		public ConnectionState state;
		public int ping;
		public float connectionQualityLocal;
		public float connectionQualityRemote;
		public float outPacketsPerSecond;
		public float outBytesPerSecond;
		public float inPacketsPerSecond;
		public float inBytesPerSecond;
		public int sendRateBytesPerSecond;
		public int pendingUnreliable;
		public int pendingReliable;
		public int sentUnackedReliable;
		public Microseconds queueTime;
	}

	[StructLayout(LayoutKind.Sequential)]
	public unsafe struct NetworkingIdentity {
		public IdentityType type;
		private int size;
		private fixed byte internals[32];
		
		public bool IsInvalid {
			get {
				return Native.SteamAPI_SteamNetworkingIdentity_IsInvalid(this);
			}
		}

		public ulong GetSteamID() {
			return Native.SteamAPI_SteamNetworkingIdentity_GetSteamID64(this);
		}

		public void SetSteamID(ulong steamID) {
			Native.SteamAPI_SteamNetworkingIdentity_SetSteamID64(ref this, steamID);
		}

		public bool EqualsTo(NetworkingIdentity identity) {
			return Native.SteamAPI_SteamNetworkingIdentity_EqualTo(this, identity);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct NetworkingMessage {
		public NetworkingIdentity identity;
		public long userData;
		public Microseconds timeReceived;
		public long messageNumber;
		internal IntPtr release;
		public IntPtr data;
		public int length;
		public Connection connection;
		public int channel;
		private int padDummy;

		public void CopyTo(byte[] destination) {
			if (destination == null)
				throw new ArgumentNullException("destination");

			Marshal.Copy(data, destination, 0, length);
		}

		public void Destroy() {
			if (release == IntPtr.Zero)
				throw new InvalidOperationException("Message not created");

			Native.SteamAPI_SteamNetworkingMessage_t_Release(release);
		}
	}

	public delegate void StatusCallback(StatusInfo info, IntPtr context);
	public delegate void DebugCallback(int type, string message);

	public class NetworkingSockets {
		private IntPtr nativeSockets;
		private readonly int nativeMessageSize = Marshal.SizeOf(typeof(NetworkingMessage));

		public NetworkingSockets() {
			nativeSockets = Native.SteamNetworkingSockets();

			if (nativeSockets == IntPtr.Zero)
				throw new InvalidOperationException("Networking sockets not created");
		}

		public IntPtr NativeData => nativeSockets;

		public ListenSocket CreateListenSocket(Address address) {
			return Native.SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(nativeSockets, address);
		}

		public Connection Connect(Address address) {
			return Native.SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(nativeSockets, address);
		}

		public Result AcceptConnection(Connection connection) {
			return Native.SteamAPI_ISteamNetworkingSockets_AcceptConnection(nativeSockets, connection);
		}

		public bool CloseConnection(Connection connection) {
			return CloseConnection(connection, 0, String.Empty, false);
		}

		public bool CloseConnection(Connection connection, int reason, string debug, bool enableLinger) {
			if (reason > Library.maxCloseReasonValue)
				throw new ArgumentOutOfRangeException("reason");

			if (debug.Length > Library.maxCloseMessageLength)
				throw new ArgumentOutOfRangeException("debug");

			return Native.SteamAPI_ISteamNetworkingSockets_CloseConnection(nativeSockets, connection, reason, debug, enableLinger);
		}

		public bool CloseListenSocket(ListenSocket socket) {
			return Native.SteamAPI_ISteamNetworkingSockets_CloseListenSocket(nativeSockets, socket);
		}

		public bool SetConnectionUserData(Connection peer, long userData) {
			return Native.SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(nativeSockets, peer, userData);
		}

		public long GetConnectionUserData(Connection peer) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(nativeSockets, peer);
		}

		public void SetConnectionName(Connection peer, string name) {
			Native.SteamAPI_ISteamNetworkingSockets_SetConnectionName(nativeSockets, peer, name);
		}

		public bool GetConnectionName(Connection peer, StringBuilder name, int maxLength) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConnectionName(nativeSockets, peer, name, maxLength);
		}

		public Result SendMessageToConnection(Connection connection, byte[] data) {
			return SendMessageToConnection(connection, data, data.Length, SendType.Unreliable);
		}

		public Result SendMessageToConnection(Connection connection, byte[] data, SendType flags) {
			return SendMessageToConnection(connection, data, data.Length, SendType.Unreliable);
		}

		public Result SendMessageToConnection(Connection connection, byte[] data, int length, SendType flags) {
			return Native.SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(nativeSockets, connection, data, (uint)length, flags);
		}

		public Result FlushMessagesOnConnection(Connection connection) {
			return Native.SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection(nativeSockets, connection);
		}

		public int ReceiveMessagesOnConnection(Connection connection, NetworkingMessage[] messages, int maxMessages) {
			IntPtr nativeMessages = IntPtr.Zero;
			int messagesCount = Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(nativeSockets, connection, out nativeMessages, maxMessages);
			MarshalMessages(nativeMessages, messages, messagesCount);

			return messagesCount;
		}

		public int ReceiveMessagesOnListenSocket(ListenSocket socket, NetworkingMessage[] messages, int maxMessages) {
			IntPtr nativeMessages = IntPtr.Zero;
			int messagesCount = Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnListenSocket(nativeSockets, socket, out nativeMessages, maxMessages);
			MarshalMessages(nativeMessages, messages, messagesCount);

			return messagesCount;
		}

		public bool GetConnectionInfo(Connection connection, ref ConnectionInfo info) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(nativeSockets, connection, ref info);
		}

		public bool GetQuickConnectionStatus(Connection connection, ref ConnectionStatus status) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetQuickConnectionStatus(nativeSockets, connection, ref status);
		}

		public int GetDetailedConnectionStatus(Connection connection, StringBuilder status, int statusLength) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetDetailedConnectionStatus(nativeSockets, connection, status, statusLength);
		}

		public bool GetListenSocketAddress(ListenSocket socket, ref Address address) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetListenSocketAddress(nativeSockets, socket, ref address);
		}

		public bool CreateSocketPair(Connection connectionOne, Connection connectionTwo, bool useNetworkLoopback, NetworkingIdentity identityOne, NetworkingIdentity identityTwo) {
			return Native.SteamAPI_ISteamNetworkingSockets_CreateSocketPair(nativeSockets, connectionOne, connectionTwo, useNetworkLoopback, identityOne, identityTwo);
		}

		public bool GetConnectionDebugText(Connection connection, StringBuilder debugText, int debugLength) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConnectionDebugText(nativeSockets, connection, debugText, debugLength);
		}

		public int GetConfigurationValue(ConfigurationValue configurationValue) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConfigurationValue(nativeSockets, configurationValue);
		}

		public bool SetConfigurationValue(ConfigurationValue configurationValue, int value) {
			return Native.SteamAPI_ISteamNetworkingSockets_SetConfigurationValue(nativeSockets, configurationValue, value);
		}

		public string GetConfigurationValueName(ConfigurationValue configurationValue) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConfigurationValueName(nativeSockets, configurationValue);
		}

		public int GetConfigurationString(ConfigurationString configurationString, StringBuilder destination, int destinationLength) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConfigurationString(nativeSockets, configurationString, destination, destinationLength);
		}

		public bool SetConfigurationString(ConfigurationString configurationString, string inputString) {
			return Native.SteamAPI_ISteamNetworkingSockets_SetConfigurationString(nativeSockets, configurationString, inputString);
		}

		public string GetConfigurationStringName(ConfigurationString configurationString) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConfigurationStringName(nativeSockets, configurationString);
		}

		public int GetConnectionConfigurationValue(Connection connection, ConfigurationValue configurationValue) {
			return Native.SteamAPI_ISteamNetworkingSockets_GetConnectionConfigurationValue(nativeSockets, connection, configurationValue);
		}

		public bool SetConnectionConfigurationValue(Connection connection, ConfigurationValue configurationValue, int value) {
			return Native.SteamAPI_ISteamNetworkingSockets_SetConnectionConfigurationValue(nativeSockets, connection, configurationValue, value);
		}

		public void DispatchCallback(StatusCallback callback) {
			DispatchCallback(callback, IntPtr.Zero);
		}

		public void DispatchCallback(StatusCallback callback, IntPtr context) {
			Native.SteamAPI_ISteamNetworkingSockets_RunConnectionStatusChangedCallbacks(nativeSockets, callback, context);
		}

		private void MarshalMessages(IntPtr nativeMessages, NetworkingMessage[] messages, int messagesCount) {
			for (int i = 0; i < messagesCount; i++) {
				IntPtr nativeMessage = new IntPtr(nativeMessages.ToInt64() + (nativeMessageSize * i));

				messages[i] = (NetworkingMessage)Marshal.PtrToStructure(nativeMessage, typeof(NetworkingMessage));
				messages[i].release = nativeMessage;
			}
		}
	}

	public static class Library {
		public const int maxCloseMessageLength = 128;
		public const int maxCloseReasonValue = 999;
		public const int maxErrorMessageLength = 1024;
		public const int maxMessageSize = 512 * 1024;
		public const int socketsCallbacks = 1220;

		public static bool Initialize(StringBuilder errorMessage) {
			if (errorMessage.Capacity != maxErrorMessageLength)
				throw new ArgumentOutOfRangeException("Capacity of the error message must be equal to " + maxErrorMessageLength);

			return Native.GameNetworkingSockets_Init(IntPtr.Zero, errorMessage);
		}

		public static void Deinitialize() {
			Debug.Log("Killed");
			Native.GameNetworkingSockets_Kill();
		}

		public static void SetDebugCallback(int detailLevel, DebugCallback callback) {
			Native.SteamNetworkingSockets_SetDebugOutputFunction(detailLevel, callback);
		}

		public static Microseconds Time {
			get {
				return Native.SteamNetworkingSockets_GetLocalTimestamp();
			}
		}
	}
	
	[SuppressUnmanagedCodeSecurity]
	internal static class Native {
		private const string nativeLibrary = "GameNetworkingSockets";

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool GameNetworkingSockets_Init(IntPtr identity, StringBuilder errorMessage);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void GameNetworkingSockets_Kill();

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern IntPtr SteamNetworkingSockets();

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern Microseconds SteamNetworkingSockets_GetLocalTimestamp();

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamNetworkingSockets_SetDebugOutputFunction(int detailLevel, DebugCallback callback);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern ListenSocket SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(IntPtr instance, Address address);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern Connection SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(IntPtr instance, Address address);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern Result SteamAPI_ISteamNetworkingSockets_AcceptConnection(IntPtr instance, Connection connection);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_CloseConnection(IntPtr instance, Connection peer, int reason, string debug, bool enableLinger);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_CloseListenSocket(IntPtr instance, ListenSocket socket);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(IntPtr instance, Connection peer, long userData);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern long SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(IntPtr instance, Connection peer);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamAPI_ISteamNetworkingSockets_SetConnectionName(IntPtr instance, Connection peer, string name);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_GetConnectionName(IntPtr instance, Connection peer, StringBuilder name, int maxLength);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern Result SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(IntPtr instance, Connection connection, byte[] data, uint length, SendType flags);
		
		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern Result SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(IntPtr instance, Connection connection, IntPtr data, uint length, SendType flags);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern Result SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection(IntPtr instance, Connection connection);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(IntPtr instance, Connection connection, out IntPtr messages, int maxMessages);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnListenSocket(IntPtr instance, ListenSocket socket, out IntPtr messages, int maxMessages);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(IntPtr instance, Connection connection, ref ConnectionInfo info);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_GetQuickConnectionStatus(IntPtr instance, Connection connection, ref ConnectionStatus status);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int SteamAPI_ISteamNetworkingSockets_GetDetailedConnectionStatus(IntPtr instance, Connection connection, StringBuilder status, int statusLength);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_GetListenSocketAddress(IntPtr instance, ListenSocket socket, ref Address address);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_CreateSocketPair(IntPtr instance, Connection connectionOne, Connection connectionTwo, bool useNetworkLoopback, NetworkingIdentity identityOne, NetworkingIdentity identityTwo);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_GetConnectionDebugText(IntPtr instance, Connection connection, StringBuilder debugText, int debugLength);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int SteamAPI_ISteamNetworkingSockets_GetConfigurationValue(IntPtr instance, ConfigurationValue configurationValue);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_SetConfigurationValue(IntPtr instance, ConfigurationValue configurationValue, int value);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern string SteamAPI_ISteamNetworkingSockets_GetConfigurationValueName(IntPtr instance, ConfigurationValue configurationValue);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int SteamAPI_ISteamNetworkingSockets_GetConfigurationString(IntPtr instance, ConfigurationString configurationString, StringBuilder destination, int destinationLength);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_SetConfigurationString(IntPtr instance, ConfigurationString configurationString, string inputString);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern string SteamAPI_ISteamNetworkingSockets_GetConfigurationStringName(IntPtr instance, ConfigurationString configurationString);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int SteamAPI_ISteamNetworkingSockets_GetConnectionConfigurationValue(IntPtr instance, Connection connection, ConfigurationValue configurationValue);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_ISteamNetworkingSockets_SetConnectionConfigurationValue(IntPtr instance, Connection connection, ConfigurationValue configurationValue, int value);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamAPI_SteamNetworkingMessage_t_Release(IntPtr nativeMessage);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamAPI_SteamNetworkingIPAddr_SetIPv6(ref Address address, byte[] ip, ushort port);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamAPI_SteamNetworkingIPAddr_SetIPv4(ref Address address, uint ip, ushort port);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern uint SteamAPI_SteamNetworkingIPAddr_GetIPv4(Address address);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamAPI_SteamNetworkingIPAddr_SetIPv6LocalHost(ref Address address, ushort port);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_SteamNetworkingIPAddr_IsLocalHost(Address address);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_SteamNetworkingIdentity_IsInvalid(NetworkingIdentity identity);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamAPI_SteamNetworkingIdentity_SetSteamID64(ref NetworkingIdentity identity, ulong steamID);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern ulong SteamAPI_SteamNetworkingIdentity_GetSteamID64(NetworkingIdentity identity);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool SteamAPI_SteamNetworkingIdentity_EqualTo(NetworkingIdentity identityOne, NetworkingIdentity identityTwo);

		[DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void SteamAPI_ISteamNetworkingSockets_RunConnectionStatusChangedCallbacks(IntPtr instance, StatusCallback callback, IntPtr context);
	}
}
