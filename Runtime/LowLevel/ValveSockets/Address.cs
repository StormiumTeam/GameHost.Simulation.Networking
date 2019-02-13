using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Valve.Sockets
{
	/*[StructLayout(LayoutKind.Sequential)]
	public unsafe struct Address
	{
		private const byte IpSize = 16;

		public fixed byte   Ip[IpSize];
		public       ushort Port;

		public Address(int port)
		{
			Port = default;

			SetLocalHost((ushort) port);
		}

		public Address(IPAddress ipAddress, int port)
		{
			Port = default;

			var addrStr = ipAddress.ToString();
			if (addrStr.Contains(":"))
			{
				SetIPv6(addrStr, (ushort) port);
			}
			else
			{
				SetIPv4(addrStr, (ushort) port);
			}
		}

		public bool IsLocalHost()
		{
			return Native.SteamAPI_SteamNetworkingIPAddr_IsLocalHost(this);
		}

		public string GetIpAsString()
		{
			fixed (byte* fixedBuffer = Ip)
			{
				return ParseIp(fixedBuffer);
			}
		}

		public void SetLocalHost(ushort port)
		{
			Native.SteamAPI_SteamNetworkingIPAddr_SetIPv6LocalHost(ref this, port);
		}

		public void SetIPv4(string ip, ushort port)
		{
			Native.SteamAPI_SteamNetworkingIPAddr_SetIPv4(ref this, ParseIPv4(ip), port);
		}

		public void SetIPv6(string ip, ushort port)
		{
			Native.SteamAPI_SteamNetworkingIPAddr_SetIPv6(ref this, ParseIPv6(ip), port);
		}

		// Extracted from extension class
		private static uint ParseIPv4(string ip)
		{
			if (IPAddress.TryParse(ip, out var address))
			{
				if (address.AddressFamily != AddressFamily.InterNetwork)
					throw new Exception("Incorrect format of an IPv4 address");
			}

			var bytes = address.GetAddressBytes();

			Array.Reverse(bytes);

			return BitConverter.ToUInt32(bytes, 0);
		}

		private static byte[] ParseIPv6(string ip)
		{
			if (!IPAddress.TryParse(ip, out var address))
				return address.GetAddressBytes();

			if (address.AddressFamily != AddressFamily.InterNetworkV6)
				throw new Exception("Incorrect format of an IPv6 address");

			return address.GetAddressBytes();
		}

		private static readonly byte[] s_IpTempArray = new byte[IpSize];

		private static string ParseIp(byte* ipUnsafe)
		{
			fixed (byte* tempBuffer = s_IpTempArray)
			{
				Unsafe.CopyBlock(tempBuffer, ipUnsafe, IpSize);
			}

			var address   = new IPAddress(s_IpTempArray);
			var converted = address.ToString();

			if (converted.Length <= 7 || converted.Remove(7) != "::ffff:")
				return address.ToString();

			var ipv4 = new Address {Ip = ipUnsafe};
			FillIIntFromByteArray((int) Native.SteamAPI_SteamNetworkingIPAddr_GetIPv4(ipv4));

			return new IPAddress(s_ByteFromIntTempArray).ToString();
		}

		private static readonly byte[] s_ByteFromIntTempArray = new byte[sizeof(int)];

		private static void FillIIntFromByteArray(int value)
		{
			fixed (byte* numPtr = s_ByteFromIntTempArray)
				*(int*) numPtr = value;
		}
	}*/
}