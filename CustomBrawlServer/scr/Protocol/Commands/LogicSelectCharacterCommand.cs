using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicSelectCharacterCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Character { get; set; }

    public override int GetCommandType() => 525;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Character = stream.ReadDataReference();
        if (!stream.IsAtEnd())
        {
            stream.ReadByte();
        }
    }

    public override void Execute(Player player)
    {
        if (Character.ClassId != 16) return;

        var id = Character.InstanceId;
        if (!BrawlerRegistry.IsPlayableBrawler(id) ||
            !player.Brawlers.TryGetValue(id, out var owned) || !owned.Unlocked)
        {
            Console.WriteLine($"[Command] Player #{player.LowId} tried to select unavailable Brawler {id}. Ignored.");
            return;
        }

        player.SelectedBrawler = id;
        Console.WriteLine($"[Command] Player #{player.LowId} selected Brawler {id}.");
        Network.BridgeServer.Instance?.BroadcastCharacterState(player, "select_brawler", online: true);
    }
}
