using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicChangeSkinCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Skin { get; set; }
    public int Variation { get; set; }

    public override int GetCommandType() => 506;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Skin = stream.ReadDataReference();
        if (!stream.IsAtEnd())
        {
            Variation = stream.ReadVInt();
        }
    }

    public override void Execute(Player player)
    {
        if (Skin.ClassId == 29)
        {
            var targetCharId = SkinRegistry.GetCharacterForSkin(Skin.InstanceId);
            if (targetCharId >= 0 && BrawlerRegistry.IsPlayableBrawler(targetCharId))
            {
                var brawler = player.GetOrCreateBrawler(targetCharId);
                brawler.SelectedSkin = Skin.InstanceId;
                Console.WriteLine($"[Command] Player #{player.LowId} changed Skin for Brawler {targetCharId} to Skin {Skin.InstanceId}.");
                Network.BridgeServer.Instance?.BroadcastCharacterState(player, "change_skin", online: true);
            }
            else
            {
                Console.WriteLine($"[Command] Player #{player.LowId} attempted to set unmapped skin {Skin.InstanceId}. Skipped.");
            }
        }
    }
}
