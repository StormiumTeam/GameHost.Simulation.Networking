using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace Unity.NetCode.Editor
{
	public class MultiplayerBuild
	{
		[MenuItem("Multiplayer/Switch to Client only")]
		public static void SwitchToClientOnly()
		{
			var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
			defines = AddCompilerDefines(defines, "UNITY_CLIENT");
			defines = RemoveCompilerDefines(defines, "UNITY_SERVER");
			
			Debug.Log("Set Compilation defines=" + defines);
			
			PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);
		}
		
		[MenuItem("Multiplayer/Switch to Server only")]
		public static void SwitchToServerOnly()
		{
			var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
			defines = AddCompilerDefines(defines, "UNITY_SERVER");
			defines = RemoveCompilerDefines(defines, "UNITY_CLIENT");
			
			Debug.Log("Set Compilation defines=" + defines);
			
			PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);
		}
		
		[MenuItem("Multiplayer/Switch to Normal")]
		public static void SwitchToNormal()
		{
			var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
			defines = RemoveCompilerDefines(defines, "UNITY_SERVER", "UNITY_CLIENT");
			
			Debug.Log("Set Compilation defines=" + defines); 
			
			PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);
		}


		private static string AddCompilerDefines(string defines, params string[] toAdd)
		{
			var splitDefines = new List<string>(defines.Split(new char[] {';'}, System.StringSplitOptions.RemoveEmptyEntries));
			foreach (var add in toAdd)
				if (!splitDefines.Contains(add))
					splitDefines.Add(add);

			return string.Join(";", splitDefines.ToArray());
		}

		private static string RemoveCompilerDefines(string defines, params string[] toRemove)
		{
			var splitDefines = new List<string>(defines.Split(new char[] {';'}, System.StringSplitOptions.RemoveEmptyEntries));
			foreach (var remove in toRemove)
				splitDefines.Remove(remove);

			return string.Join(";", splitDefines.ToArray());
		}
	}
}