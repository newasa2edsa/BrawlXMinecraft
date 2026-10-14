namespace CustomBrawlServer.Protocol.Messages.Client;

public class KeepAliveMessage : PiranhaMessage
{
    public override void Decode()
    {
        base.Decode();
    }

    public override int GetMessageType() => 10108;
    public override int GetServiceNodeType() => 1;
}
