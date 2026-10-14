using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Server;

public class AvailableServerCommandMessage : PiranhaMessage
{
    private readonly int _commandType;
    private readonly Action<ByteStream> _encodeAction;

    public AvailableServerCommandMessage(int commandType, Action<ByteStream> encodeAction)
    {
        _commandType = commandType;
        _encodeAction = encodeAction;
    }

    public override void Encode()
    {
        base.Encode();
        ByteStream.WriteVInt(_commandType);
        _encodeAction(ByteStream);
    }

    public override int GetMessageType() => 24111;
    public override int GetServiceNodeType() => 9;
}
