namespace CustomBrawlServer.Protocol.Messages.Server;

public class LoginFailedMessage : PiranhaMessage
{
    public int ErrorCode { get; set; } = 10; // 10 = Custom message, 1 = Outdated version, 8 = Maintenance
    public string Reason { get; set; } = string.Empty;
    public string ResourceFingerprintData { get; set; } = string.Empty;
    public string RedirectDomain { get; set; } = string.Empty;
    public string ContentUrl { get; set; } = string.Empty;
    public string UpdateUrl { get; set; } = string.Empty;
    public int SecondsUntilMaintenanceEnds { get; set; } = 0;

    public LoginFailedMessage()
    {
    }

    public LoginFailedMessage(int errorCode, string reason)
    {
        ErrorCode = errorCode;
        Reason = reason;
    }

    public override void Encode()
    {
        base.Encode();
        ByteStream.WriteInt(ErrorCode);
        ByteStream.WriteString(ResourceFingerprintData);
        ByteStream.WriteString(RedirectDomain);
        ByteStream.WriteString(ContentUrl);
        ByteStream.WriteString(UpdateUrl);
        ByteStream.WriteString(Reason);
        ByteStream.WriteInt(SecondsUntilMaintenanceEnds);
        ByteStream.WriteByte(0);
        ByteStream.WriteString(string.Empty);
    }

    public override int GetMessageType() => 20103;
    public override int GetServiceNodeType() => 1;
}
