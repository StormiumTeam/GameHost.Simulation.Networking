using package.stormiumteam.networking;

namespace DefaultNamespace
{
    public struct PatternPeeker
    {
        public MessageReader Reader;
        public MessageIdent Out;

        private ConnectionPatternManager m_PatternManager;
        
        public PatternPeeker(NetworkInstance netInstance, MessageReader reader)
        {
            Reader = reader;
            Out = MessageIdent.Zero;

            m_PatternManager = netInstance.GetPatternManager();
        }

        public void Peek(MessageIdent pattern)
        {
            if (Out != MessageIdent.Zero)
                return;

            Out = m_PatternManager.PeekAndGet(Reader, pattern);
        }
    }
}