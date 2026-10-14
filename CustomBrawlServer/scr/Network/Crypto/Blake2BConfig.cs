namespace CustomBrawlServer.Network.Crypto;

public sealed class Blake2BConfig
{
    public static int OutputSize => 24;

    public byte[]? Key { get; init; }
    public byte[]? Salt { get; init; }
    public byte[]? Personalization { get; init; }
}
