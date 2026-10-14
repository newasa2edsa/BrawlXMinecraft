using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class AskForBattleEndMessage : PiranhaMessage
{
    public override void Decode()
    {
        base.Decode();
    }

    public override int GetMessageType() => 14110;
    public override int GetServiceNodeType() => 9;
}
