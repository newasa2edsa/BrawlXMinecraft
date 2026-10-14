using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Server;

public class MatchMakingStatusMessage : PiranhaMessage
{
    public int Seconds { get; set; }
    public int FoundPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public bool ShowTips { get; set; }

    public MatchMakingStatusMessage(int found = 6, int max = 6, int seconds = 0, bool showTips = true)
    {
        Seconds = seconds;
        FoundPlayers = found;
        MaxPlayers = max;
        ShowTips = showTips;
    }

    public override void Encode()
    {
        base.Encode();
        ByteStream.WriteInt(Seconds);
        ByteStream.WriteInt(FoundPlayers);
        ByteStream.WriteInt(MaxPlayers);
        ByteStream.WriteInt(0);
        ByteStream.WriteInt(0);
        ByteStream.WriteBoolean(ShowTips);
    }

    public override int GetMessageType() => 20405;
    public override int GetServiceNodeType() => 9;
}
