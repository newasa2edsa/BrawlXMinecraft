using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Server;

public class MatchMakingCancelledMessage : PiranhaMessage
{
    public override void Encode()
    {
        base.Encode();
    }

    public override int GetMessageType() => 20406;
    public override int GetServiceNodeType() => 9;
}
