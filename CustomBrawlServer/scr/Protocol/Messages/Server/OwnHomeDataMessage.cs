using System;
using System.Linq;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Server;

public class OwnHomeDataMessage : PiranhaMessage
{
    private readonly Player _player;

    public OwnHomeDataMessage(Player player)
    {
        _player = player;
    }

    public override void Encode()
    {
        base.Encode();

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(-1);

        // ==========================================
        // LogicClientHome start
        // ==========================================

        // LogicDailyData start
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(_player.Gems);
        ByteStream.WriteVInt(_player.FreeGems);
        ByteStream.WriteVInt(_player.Experience);
        ByteStream.WriteVInt(_player.Level);
        ByteStream.WriteVInt(_player.Trophies);
        ByteStream.WriteDataReference(28, _player.ProfileIcon);
        ByteStream.WriteDataReference(43, _player.NameColor);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(100000);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(1);
        ByteStream.WriteBoolean(true);
        ByteStream.WriteVInt(19500);
        ByteStream.WriteVInt(111111);
        ByteStream.WriteVInt(1375134);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(1375134);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteBoolean(true);
        ByteStream.WriteVInt(2);
        ByteStream.WriteVInt(2);
        ByteStream.WriteVInt(2);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(0); // Shop offers count

        ByteStream.WriteVInt(200);
        ByteStream.WriteVInt(-1);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(-1);

        ByteStream.WriteByte(1);
        {
            ByteStream.WriteDataReference(16, _player.SelectedBrawler);
        }

        ByteStream.WriteString("EN");
        ByteStream.WriteString("BSL.v68");

        ByteStream.WriteVInt(8);
        {
            ByteStream.WriteVLong(new LogicLong(1, 9));
            ByteStream.WriteVLong(new LogicLong(1, 22));
            ByteStream.WriteVLong(new LogicLong(3, 25));
            ByteStream.WriteVLong(new LogicLong(1, 24));
            ByteStream.WriteVLong(new LogicLong(2, 15));
            ByteStream.WriteVLong(new LogicLong(32447, 28));
            ByteStream.WriteVLong(new LogicLong(100, 46));
            ByteStream.WriteVLong(new LogicLong(1, 52));
        }

        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(1);
        {
            ByteStream.WriteVInt(52);
            ByteStream.WriteVInt(0);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteVInt(0);

            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(true);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);

            ByteStream.WriteBoolean(true);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);

            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(true);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(0);

            ByteStream.WriteDataReference(0);

            ByteStream.WriteVInt(0);
        }

        if (ByteStream.WriteBoolean(true))
        {
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteString(string.Empty);
        }

        if (ByteStream.WriteBoolean(true))
            ByteStream.WriteVInt(0);

        ByteStream.WriteBoolean(false);

        ByteStream.WriteInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteDataReference(16, 0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(-1);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteDataReference(2, 0);
        ByteStream.WriteDataReference(2, 0);
        ByteStream.WriteDataReference(2, 0);
        ByteStream.WriteDataReference(2, 0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteDataReference(2, 0);
        ByteStream.WriteVInt(770);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);

        // LogicDailyData end

        // LogicConfData start
        var es = 38;
        ByteStream.WriteVInt(es);
        for (var i = 0; i < es; i++)
            ByteStream.WriteVInt(i + 1);

        ByteStream.WriteVInt(1); // Event slots count
        {
            ByteStream.WriteVInt(-1);
            ByteStream.WriteVInt(1);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(85926);
            ByteStream.WriteVInt(5);
            ByteStream.WriteDataReference(15, 13); // Map: Survival1
            ByteStream.WriteDataReference(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteString(null);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteVInt(0);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteVInt(-1);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteDataReference(0);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteVInt(-1);
            ByteStream.WriteVInt(-1);
            ByteStream.WriteVInt(-1);
            ByteStream.WriteVInt(-1);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(false);
        }

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(10);
        foreach (var i in new[] { 20, 35, 75, 140, 290, 480, 800, 1250, 1875, 2800 })
            ByteStream.WriteVInt(i);

        ByteStream.WriteVInt(4);
        foreach (var i in new[] { 30, 80, 170, 360 })
            ByteStream.WriteVInt(i);

        ByteStream.WriteVInt(4);
        foreach (var i in new[] { 300, 880, 2040, 4680 })
            ByteStream.WriteVInt(i);

        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(21);
        {
            ByteStream.WriteVLong(new LogicLong(501, 10008));
            ByteStream.WriteVLong(new LogicLong(0, 10046));
            ByteStream.WriteVLong(new LogicLong(30, 10050));
            ByteStream.WriteVLong(new LogicLong(0, 10051));
            ByteStream.WriteVLong(new LogicLong(5600, 10060));
            ByteStream.WriteVLong(new LogicLong(200, 117));
            ByteStream.WriteVLong(new LogicLong(1, 128));
            ByteStream.WriteVLong(new LogicLong(0, 65));
            ByteStream.WriteVLong(new LogicLong(41000000 + 174, 1));
            ByteStream.WriteVLong(new LogicLong(99999999, 131));
            ByteStream.WriteVLong(new LogicLong(100000, 138));
            ByteStream.WriteVLong(new LogicLong(1, 95));
            ByteStream.WriteVLong(new LogicLong(55598, 47));
            ByteStream.WriteVLong(new LogicLong(1, 123));
            ByteStream.WriteVLong(new LogicLong(200, 124));
            ByteStream.WriteVLong(new LogicLong(55598, 48));
            ByteStream.WriteVLong(new LogicLong(3, 50));
            ByteStream.WriteVLong(new LogicLong(500, 1100));
            ByteStream.WriteVLong(new LogicLong(500, 1101));
            ByteStream.WriteVLong(new LogicLong(1, 1002));
            ByteStream.WriteVLong(new LogicLong(500, 1102));
        }

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(6);
        foreach (var i in new[] { 0, 29, 79, 169, 349, 699 })
            ByteStream.WriteVInt(i);

        ByteStream.WriteVInt(6);
        foreach (var i in new[] { 0, 160, 450, 500, 1250, 2500 })
            ByteStream.WriteVInt(i);

        ByteStream.WriteVInt(5);
        foreach (var i in new[] { 0, 100, 400, 1000, 3000 })
            ByteStream.WriteVInt(i);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        // LogicConfData end

        new LogicLong((int)_player.HighId, (int)_player.LowId).Encode(ByteStream);

        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(-1);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);

        ByteStream.WriteVInt(0);

        ByteStream.WriteBoolean(true);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(0);

        ByteStream.WriteDataReference(0);
        ByteStream.WriteDataReference(0);
        ByteStream.WriteDataReference(0);
        ByteStream.WriteDataReference(0);
        ByteStream.WriteDataReference(0);
        ByteStream.WriteDataReference(0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);

        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteInt(-1488);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(_player.StarPoints);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(2);
        ByteStream.WriteDataReference(95, 0);
        ByteStream.WriteVInt(1);
        ByteStream.WriteDataReference(95, 1);
        ByteStream.WriteVInt(1);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);

        // ==========================================
        // LogicClientHome end
        // ==========================================

        // ==========================================
        // LogicClientAvatar start
        // ==========================================
        ByteStream.WriteVLong(new LogicLong((int)_player.HighId, (int)_player.LowId));
        ByteStream.WriteVLong(new LogicLong((int)_player.HighId, (int)_player.LowId));
        ByteStream.WriteVLong(new LogicLong(0, 0));

        ByteStream.WriteStringReference(_player.Name);
        ByteStream.WriteBoolean(_player.NameSet);
        ByteStream.WriteInt(-1);

        // Commodities (35 arrays in v68 LogicClientAvatar::decode 0xa4ec60)
        ByteStream.WriteVInt(35);
        {
            // Commodity 0: Resources / Cards fallback
            ByteStream.WriteVInt(0);
            WriteCardCommodity(ByteStream, _player);

            // Commodity 1: Unlocked Cards (ClassId=23: brawler unlock cards, star powers, gadgets)
            // LogicClientAvatar::isHeroUnlocked (0xa50610) & getCardCount (0xa51a30) read Commodity 1!
            ByteStream.WriteVInt(0);
            WriteCardCommodity(ByteStream, _player);

            // Commodity 2, 3, 4: Trophies / Highest Trophies / Power Points
            for (var x = 2; x <= 4; x++)
            {
                ByteStream.WriteVInt(0);
                ByteStream.WriteVInt(0);
            }

            // Commodity 5: Brawler Power Levels (ClassId=16, value = Level - 1, read at 0xa4fc20 & 0xa50814)
            ByteStream.WriteVInt(0);
            WriteBrawlerLevelCommodity(ByteStream, _player);

            // Commodity 6 & 7: Brawler Seen State (ClassId=16, value = 2 = already seen, read at 0xa5015c & 0xa51d80
            // so the client never crashes or locks up playing unseen-brawler unlock animations on first load!)
            ByteStream.WriteVInt(0);
            WriteBrawlerSeenCommodity(ByteStream, _player);
            ByteStream.WriteVInt(0);
            WriteBrawlerSeenCommodity(ByteStream, _player);

            // Remaining 27 commodity categories (8..34)
            for (var x = 8; x < 35; x++)
            {
                ByteStream.WriteVInt(0);
                ByteStream.WriteVInt(0);
            }
        }

        ByteStream.WriteVInt(_player.Trophies);
        ByteStream.WriteVInt(_player.HighestTrophies);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(_player.Experience);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(2);
        ByteStream.WriteVInt(1);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteString(null);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(2);
        ByteStream.WriteVInt(0);
        ByteStream.WriteBoolean(false);

        // ==========================================
        // LogicClientAvatar end
        // ==========================================
    }

    /// <summary>
    /// Writes the "cards" commodity body: a count, then (card ref, -1, amount) for every owned card.
    /// Brawler unlock cards carry the brawler's power level; star powers and gadgets carry 1 (owned).
    /// Kept separate so it can be unit tested without building the whole home data packet.
    /// </summary>
    public static void WriteCardCommodity(ByteStream stream, Player player)
    {
        var unlockedBrawlers = player.Brawlers.Values
            .Where(b => b.Unlocked && BrawlerRegistry.IsPlayableBrawler(b.CharacterId))
            .OrderBy(b => b.CharacterId)
            .ToList();

        var cfg = Config.ServerConfig.Instance.Gameplay;
        var abilityCards = cfg.UnlockAllCards
            ? CardRegistry.GetAbilityCards(cfg.MaxCardId)
            : Array.Empty<int>();

        stream.WriteVInt(unlockedBrawlers.Count + abilityCards.Count);

        foreach (var brawler in unlockedBrawlers)
        {
            stream.WriteDataReference(CardRegistry.CardClassId, BrawlerRegistry.GetUnlockCardId(brawler.CharacterId));
            stream.WriteVInt(-1);
            stream.WriteVInt(brawler.Level);
        }

        foreach (var cardId in abilityCards)
        {
            stream.WriteDataReference(CardRegistry.CardClassId, cardId);
            stream.WriteVInt(-1);
            stream.WriteVInt(1);
        }
    }

    public static void WriteBrawlerLevelCommodity(ByteStream stream, Player player)
    {
        var unlockedBrawlers = player.Brawlers.Values
            .Where(b => b.Unlocked && BrawlerRegistry.IsPlayableBrawler(b.CharacterId))
            .OrderBy(b => b.CharacterId)
            .ToList();

        stream.WriteVInt(unlockedBrawlers.Count);
        foreach (var brawler in unlockedBrawlers)
        {
            stream.WriteDataReference(16, brawler.CharacterId);
            stream.WriteVInt(-1);
            stream.WriteVInt(Math.Clamp(brawler.Level - 1, 0, 10));
        }
    }

    public static void WriteBrawlerSeenCommodity(ByteStream stream, Player player)
    {
        var unlockedBrawlers = player.Brawlers.Values
            .Where(b => b.Unlocked && BrawlerRegistry.IsPlayableBrawler(b.CharacterId))
            .OrderBy(b => b.CharacterId)
            .ToList();

        stream.WriteVInt(unlockedBrawlers.Count);
        foreach (var brawler in unlockedBrawlers)
        {
            stream.WriteDataReference(16, brawler.CharacterId);
            stream.WriteVInt(-1);
            stream.WriteVInt(2); // 2 = already seen (prevents unseen-brawler unlock popups/crashes on first load)
        }
    }

    public override int GetMessageType() => 24101;
    public override int GetServiceNodeType() => 9;
}
