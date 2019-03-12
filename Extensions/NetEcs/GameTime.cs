using Unity.Entities;

namespace StormiumShared.Core
{
    public struct GameTime
    {
        public int Frame;
        public int Tick;
        public int DeltaTick;
        public int FixedTickPerSecond;
        public double Time;
        public float DeltaTime;
    }

    public struct GameTimeComponent : IComponentData
    {
        public GameTime Value;

        public GameTimeComponent(GameTime value)
        {
            Value = value;
        }
    }

    public struct SingletonGameTime : IComponentData
    {
        public int    Frame;
        public int    Tick;
        public int    DeltaTick;
        public int    FixedTickPerSecond;
        public double Time;
        public float  DeltaTime;

        public GameTime ToGameTime()
        {
            return new GameTime
            {
                Frame              = Frame,
                Tick               = Tick,
                DeltaTick          = DeltaTick,
                FixedTickPerSecond = FixedTickPerSecond,
                Time               = Time,
                DeltaTime          = DeltaTime
            };
        }
    }
}