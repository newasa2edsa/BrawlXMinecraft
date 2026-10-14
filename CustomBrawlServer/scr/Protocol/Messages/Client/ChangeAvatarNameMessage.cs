namespace CustomBrawlServer.Protocol.Messages.Client;

public class ChangeAvatarNameMessage : PiranhaMessage
{
    public string AvatarName { get; set; } = string.Empty;
    public int State { get; set; }

    public override void Decode()
    {
        base.Decode();
        AvatarName = ByteStream.ReadString(1024) ?? string.Empty;
        State = ByteStream.ReadVInt();
    }

    public override int GetMessageType() => 10212;
    public override int GetServiceNodeType() => 9;
}
