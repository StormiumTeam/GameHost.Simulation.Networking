namespace package.stormiumteam.networking
{
    public interface ISystemAbleToUseChannel
    {
        bool AbleToUseChannels();
        NetworkChannel CreateChannelAndBroadcastIt(string id, int requestedPort = 0);
    }
}