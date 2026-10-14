using System;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class SinglePlayerMatchRequestMessage : PiranhaMessage
{
    public byte[] RawPayload { get; private set; } = Array.Empty<byte>();

    public int BrawlerClass { get; private set; } = 16;
    public int BrawlerId { get; private set; } = 0;
    public int SkinClass { get; private set; } = 29;
    public int SkinId { get; private set; } = 0;
    public int LocationClass { get; private set; } = 15;
    public int LocationId { get; private set; } = 121;

    public override void Decode()
    {
        base.Decode();
        RawPayload = ByteStream.GetByteArray();

        try
        {
            if (RawPayload.Length > 0)
            {
                BrawlerClass = ByteStream.ReadVInt();
                BrawlerId = ByteStream.ReadVInt();
                SkinClass = ByteStream.ReadVInt();
                SkinId = ByteStream.ReadVInt();
                ByteStream.ReadVInt(); // 8
                ByteStream.ReadVInt(); // 0
                LocationClass = ByteStream.ReadVInt();
                LocationId = ByteStream.ReadVInt();
            }
        }
        catch
        {
            // Fallback to default values
        }
    }

    public override int GetMessageType() => 14118;
    public override int GetServiceNodeType() => 9;
}
