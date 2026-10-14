using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicSetPlayerNameColorCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Color { get; set; }

    public override int GetCommandType() => 527;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Color = stream.ReadDataReference();
    }

    public override void Execute(Player player)
    {
        player.NameColor = Color.InstanceId;
        Console.WriteLine($"[Command] Player #{player.LowId} updated name color to {Color.InstanceId}.");
    }
}
