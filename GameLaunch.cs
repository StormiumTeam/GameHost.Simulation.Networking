using System;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public static class GameLaunch
    {
        public static bool HasGraphics { get; private set; }
        public static bool IsServer { get; private set; }

        static GameLaunch()
        {
            var commandLineOptions = Environment.CommandLine;
            
            IsServer = Application.isBatchMode || commandLineOptions.Contains("-batchmode");
            HasGraphics = !(SystemInfo.graphicsDeviceID == 0 || commandLineOptions.Contains("-nographics"));
        }

        public static void Force()
        {
            IsServer = true;
        }

        public static void Unforce()
        {
            IsServer = false;
        }
    }
}