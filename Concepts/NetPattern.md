MessagePattern mv_w_input;
MessagePattern mv_r_input;

void Main()
{
    var _register = GetSystem<PatternRegisterSystem>();
    mv_w_input = _register.Add(new MessagePattern("package.stormium.default/movement/w_input", 0));
    mv_r_input = _register.Add(new MessagePattern("package.stormium.default/movement/r_input", 0));

    var _network = GetSystem<NetworkSystem>();
    _network.OnNewMessage += OnNewMessage;
}

void OnNewMessage(NetMsg msg)
{
    MessagePattern pattern;
    if (msg.PeekPattern(out pattern))
    {
        ReadPattern(pattern);
    }
}

void ReadPattern(MessagePattern pattern)
{
    // The first one check if the pattern is the same as the write move input pattern
    // The last check if for checking if we are a server
    // This message should only be received by the server
    if (pattern.SameAs(mv_w_input) && CServerInfo.IsHostOf(ServerInfo))
    {
        // Do server stuff...

        // then send the player input to other players...
        foreach (player in __players)
        {
            var playerPatternVersion = player.GetPatternVersion(pattern);
            if (playerPatternVersion == 0)
                ...

            var msg = new NetMsg(...);
            msg.Write(mv_r_input);
            msg.Write(value1);
            msg.Write(value2);
            msg.Write(...);

            // We could also do it like that (it's clearer)
            var msg = new NetMsg();
            new MoveReadInputPWritter(value1, value2, ...)
                .WriteTo(msg);

            SendMessage(msg, player);
        }
    }

    // *
    // This message can be received by a player and a server
    // Well, it highly depend on what the message should be
    // but for reading an input, maybe a server listener may
    // need to know.
    if (pattern.SameAs(mv_r_input))
    {
        ...
    }
}