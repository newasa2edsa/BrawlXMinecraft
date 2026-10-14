namespace CustomBrawlServer.Protocol.Messages.Server;

public class KeepAliveOkMessage : PiranhaMessage
{
    public override void Encode()
    {
        base.Encode();
    }

    public override int GetMessageType() => 20108;
    public override int GetServiceNodeType() => 1;
}
