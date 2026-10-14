using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Server;

public class UdpConnectionInfoMessage : PiranhaMessage
{
    public int ServerPort { get; set; }
    public string ServerHost { get; set; }

    public UdpConnectionInfoMessage(string serverHost = "127.0.0.1", int serverPort = 9339)
    {
        ServerHost = serverHost;
        ServerPort = serverPort;
    }

    public override void Encode()
    {
        base.Encode();
        ByteStream.WriteVInt(ServerPort);
        ByteStream.WriteString(ServerHost);
        ByteStream.WriteInt(0);
        ByteStream.WriteByte(0);
        ByteStream.WriteInt(0);
        ByteStream.WriteByte(0);
    }

    public override int GetMessageType() => 24112;
    public override int GetServiceNodeType() => 9;
}
