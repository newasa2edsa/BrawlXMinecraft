using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class ExitTrainingGroundMessage : PiranhaMessage
{
    public override void Decode()
    {
        base.Decode();
    }

    public override int GetMessageType() => 17750;
    public override int GetServiceNodeType() => 9;
}
