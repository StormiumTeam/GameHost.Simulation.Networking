namespace package.stormiumteam.networking
{
    public interface ISelfConnectionCreator : IConnectionCreator
    {
        void Execute(NetworkInstance emptyNetInstance);
    }
}