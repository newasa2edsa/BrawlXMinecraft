namespace CustomBrawlServer.Protocol.Messages.Client;

public class ClientHelloMessage : PiranhaMessage
{
    public int Protocol { get; set; }
    public int KeyVersion { get; set; }
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }
    public int BuildVersion { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public int DeviceType { get; set; }
    public int AppStore { get; set; }

    public override void Decode()
    {
        base.Decode();
        Protocol = ByteStream.ReadInt();
        KeyVersion = ByteStream.ReadInt();
        MajorVersion = ByteStream.ReadInt();
        MinorVersion = ByteStream.ReadInt();
        BuildVersion = ByteStream.ReadInt();
        ContentHash = ByteStream.ReadStringReference();
        DeviceType = ByteStream.ReadInt();
        AppStore = ByteStream.ReadInt();
    }

    public override int GetMessageType() => 10100;
    public override int GetServiceNodeType() => 1;
}
