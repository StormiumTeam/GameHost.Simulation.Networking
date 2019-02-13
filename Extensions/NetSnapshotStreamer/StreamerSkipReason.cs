namespace StormiumShared.Core.Networking
{
    public enum StreamerSkipReason : byte
    {
        NoSkip      = 0,
        Delta       = 1,
        NoComponent = 2
    }
}