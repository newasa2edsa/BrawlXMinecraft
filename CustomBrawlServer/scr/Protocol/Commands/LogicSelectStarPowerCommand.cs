using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicSelectStarPowerCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Card { get; set; }

    public override int GetCommandType() => 532;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Card = stream.ReadDataReference();
    }

    public override void Execute(Player player)
    {
        if (Card.ClassId != CardRegistry.CardClassId)
        {
            Console.WriteLine($"[Command] Player #{player.LowId} sent select-card with unexpected class {Card.ClassId} (instance {Card.InstanceId}). Ignored.");
            return;
        }

        var cardId = Card.InstanceId;
        var isStarPower = CardRegistry.IsStarPower(cardId);
        var isGadget = CardRegistry.IsGadget(cardId);

        if (!isStarPower && !isGadget)
        {
            Console.WriteLine($"[Command] Player #{player.LowId} selected card {cardId}, which is neither a known star power nor gadget. Ignored.");
            return;
        }

        if (!player.OwnsAbilityCard(cardId))
        {
            Console.WriteLine($"[Command] Player #{player.LowId} tried to equip card {cardId} without owning it. Ignored.");
            return;
        }

        // Newer brawlers can be attributed from the card id layout; older ones fall back to the active brawler.
        var brawlerId = CardRegistry.GetBrawlerForCard(cardId);
        if (brawlerId < 0)
            brawlerId = player.SelectedBrawler;

        var brawler = player.GetOrCreateBrawler(brawlerId);
        if (isStarPower)
            brawler.SelectedStarPower = cardId;
        else
            brawler.SelectedGadget = cardId;

        Console.WriteLine($"[Command] Player #{player.LowId} equipped {(isStarPower ? "star power" : "gadget")} {cardId} on Brawler {brawlerId}.");
        Network.BridgeServer.Instance?.BroadcastCharacterState(player, isStarPower ? "equip_starpower" : "equip_gadget", online: true);
    }
}
