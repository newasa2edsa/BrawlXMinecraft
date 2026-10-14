using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicGenericSettingCommand : LogicCommand
{
    public int Value { get; set; }
    public bool Flag { get; set; }

    public override int GetCommandType() => 557;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        if (!stream.IsAtEnd())
        {
            Value = stream.ReadVInt();
        }
        if (!stream.IsAtEnd())
        {
            Flag = stream.ReadBoolean();
        }
    }

    public override void Execute(Player player)
    {
        Console.WriteLine($"[Command] Player #{player.LowId} setting 557: Value={Value}, Flag={Flag}.");
    }
}
