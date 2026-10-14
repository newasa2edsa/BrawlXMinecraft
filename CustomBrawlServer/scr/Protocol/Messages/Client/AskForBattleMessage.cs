namespace CustomBrawlServer.Protocol.Messages.Client;

public class AskForBattleMessage : PiranhaMessage
{
    public (int ClassId, int InstanceId) Location { get; set; }

    public override void Decode()
    {
        base.Decode();
        Location = ByteStream.ReadDataReference();
    }

    public override int GetMessageType() => 14109;
    public override int GetServiceNodeType() => 9;
}
