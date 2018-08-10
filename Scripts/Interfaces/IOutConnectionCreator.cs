namespace package.stormiumteam.networking
{
    public interface IOutConnectionCreator : IConnectionCreator
    {
        void Execute(NetworkInstance emptyNetInstance);
    }
}