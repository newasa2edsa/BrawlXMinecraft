using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicUpgradeBrawlerCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Target { get; set; }

    public override int GetCommandType() => 520;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Target = stream.ReadDataReference();
        if (!stream.IsAtEnd())
        {
            stream.ReadBoolean();
        }
    }

    public override void Execute(Player player)
    {
        if (Target.ClassId != 16 || !BrawlerRegistry.IsPlayableBrawler(Target.InstanceId))
        {
            Console.WriteLine($"[Command] Player #{player.LowId} tried to upgrade invalid target ({Target.ClassId}, {Target.InstanceId}). Ignored.");
            return;
        }

        var brawler = player.GetOrCreateBrawler(Target.InstanceId);
        if (brawler.Level < 11)
        {
            brawler.Level++;
            Console.WriteLine($"[Command] Player #{player.LowId} upgraded Brawler {Target.InstanceId} to Level {brawler.Level}.");
            Network.BridgeServer.Instance?.BroadcastCharacterState(player, "upgrade_brawler", online: true);
        }
    }
}
