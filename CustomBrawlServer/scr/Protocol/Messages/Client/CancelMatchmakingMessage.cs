using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class CancelMatchmakingMessage : PiranhaMessage
{
    public override void Decode()
    {
        base.Decode();
    }

    public override int GetMessageType() => 14106;
    public override int GetServiceNodeType() => 9;
}
