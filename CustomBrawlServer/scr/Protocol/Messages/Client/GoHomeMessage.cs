using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class GoHomeMessage : PiranhaMessage
{
    public override void Decode()
    {
        base.Decode();
        _ = ByteStream.ReadBoolean();
    }

    public override int GetMessageType() => 14101;
    public override int GetServiceNodeType() => 9;
}
