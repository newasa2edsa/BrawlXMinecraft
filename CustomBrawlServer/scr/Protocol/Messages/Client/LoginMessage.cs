namespace CustomBrawlServer.Protocol.Messages.Client;

public class LoginMessage : PiranhaMessage
{
    public long HighId { get; set; }
    public long LowId { get; set; }
    public string PassToken { get; set; } = string.Empty;
    public int ClientMajorVersion { get; set; }
    public int ClientMinor { get; set; }
    public int ClientBuild { get; set; }
    public string ResourceSha { get; set; } = string.Empty;

    public override void Decode()
    {
        base.Decode();
        HighId = ByteStream.ReadInt();
        LowId = ByteStream.ReadInt();
        PassToken = ByteStream.ReadString(1024) ?? string.Empty;
        ClientMajorVersion = ByteStream.ReadInt();
        ClientMinor = ByteStream.ReadInt();
        ClientBuild = ByteStream.ReadInt();
        ResourceSha = ByteStream.ReadString(1024) ?? string.Empty;
    }

    public override int GetMessageType() => 10101;
    public override int GetServiceNodeType() => 1;
}
