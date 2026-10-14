using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public abstract class LogicCommand
{
    public int TickWhenGiven { get; set; }
    public int ExecuteTick { get; set; }
    public LogicLong ExecutorAccountId { get; set; } = new(0, 0);

    public abstract int GetCommandType();

    public virtual void Decode(ByteStream stream)
    {
        TickWhenGiven = stream.ReadVInt();
        ExecuteTick = stream.ReadVInt();
        ExecutorAccountId = stream.ReadVLong();
    }

    public virtual void Execute(Player player)
    {
    }
}
