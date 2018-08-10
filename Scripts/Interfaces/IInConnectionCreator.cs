namespace package.stormiumteam.networking
{
    public interface IInConnectionCreator : IConnectionCreator
    {
        void Execute(NetworkInstance emptyNetInstance);
    }
}