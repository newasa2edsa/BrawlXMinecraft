using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class MatchmakeMessage : PiranhaMessage
{
    public override void Decode()
    {
        base.Decode();
    }

    public override int GetMessageType() => 14104;
    public override int GetServiceNodeType() => 9;
}
