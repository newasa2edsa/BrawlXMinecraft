using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicSetPlayerThumbnailCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Thumbnail { get; set; }

    public override int GetCommandType() => 505;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Thumbnail = stream.ReadDataReference();
    }

    public override void Execute(Player player)
    {
        player.ProfileIcon = Thumbnail.InstanceId;
        Console.WriteLine($"[Command] Player #{player.LowId} updated profile thumbnail to {Thumbnail.InstanceId}.");
    }
}
