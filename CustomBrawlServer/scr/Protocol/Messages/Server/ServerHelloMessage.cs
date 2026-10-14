namespace CustomBrawlServer.Protocol.Messages.Server;

public class ServerHelloMessage : PiranhaMessage
{
    private byte[] _serverHelloToken = new byte[24];

    public ServerHelloMessage()
    {
    }

    public ServerHelloMessage(byte[] token)
    {
        _serverHelloToken = token;
    }

    public void SetServerHelloToken(byte[] token)
    {
        _serverHelloToken = token;
    }

    public byte[] GetServerHelloToken() => _serverHelloToken;

    public override void Encode()
    {
        base.Encode();
        ByteStream.WriteBytes(_serverHelloToken, _serverHelloToken.Length);
    }

    public override int GetMessageType() => 20100;
    public override int GetServiceNodeType() => 1;
}
