using Unity.Entities;

namespace GameImplementation
{
    public class RuntimeGameNetwork : ComponentSystem
    {
        public ReadOnlyGameSession CurrentSession { get; private set; }

        protected override void OnUpdate()
        {
            
        }

        public GameSession NewSession()
        {
            var gameSession = new GameSession();
            CurrentSession = new ReadOnlyGameSession(gameSession);
            
            return gameSession;
        }
    }
}    