using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class GoHomeFromOfflinePractiseMessage : PiranhaMessage
{
    public override void Decode()
    {
        base.Decode();
        // Client sends 1 boolean
        _ = ByteStream.ReadBoolean();
    }

    public override int GetMessageType() => 14109;
    public override int GetServiceNodeType() => 9;
}
