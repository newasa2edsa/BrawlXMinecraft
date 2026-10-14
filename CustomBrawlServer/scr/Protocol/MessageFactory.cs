using System;
using System.Collections.Generic;
using CustomBrawlServer.Protocol.Messages.Client;

namespace CustomBrawlServer.Protocol;

public static class MessageFactory
{
    private static readonly Dictionary<int, Func<PiranhaMessage>> ClientMessages = new()
    {
        [10100] = () => new ClientHelloMessage(),
        [10101] = () => new LoginMessage(),
        [10108] = () => new KeepAliveMessage(),
        [10212] = () => new ChangeAvatarNameMessage(),
        [10555] = () => new ClientInputMessage(),
        [14101] = () => new GoHomeMessage(),
        [14102] = () => new EndClientTurnMessage(),
        [14103] = () => new MatchmakeMessage(),
        [14104] = () => new MatchmakeMessage(),
        [14106] = () => new CancelMatchmakingMessage(),
        [14107] = () => new CancelMatchmakingMessage(),
        [14109] = () => new GoHomeFromOfflinePractiseMessage(),
        [14110] = () => new AskForBattleEndMessage(),
        [14118] = () => new SinglePlayerMatchRequestMessage(),
        [17750] = () => new ExitTrainingGroundMessage(),
        [18977] = () => new MatchmakeMessage()
    };

    public static bool IsHandled(int messageType) => ClientMessages.ContainsKey(messageType);

    public static PiranhaMessage? CreateMessage(int messageType)
    {
        if (ClientMessages.TryGetValue(messageType, out var factory))
            return factory();
        return null;
    }
}
