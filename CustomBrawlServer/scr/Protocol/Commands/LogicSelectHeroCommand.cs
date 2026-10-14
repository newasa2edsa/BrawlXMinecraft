using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicSelectHeroCommand : LogicCommand
{
    public override int GetCommandType() => 526;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
    }

    public override void Execute(Player player)
    {
    }
}
