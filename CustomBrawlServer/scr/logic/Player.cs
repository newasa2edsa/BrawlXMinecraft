using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Logic;

public class Player
{
    public long HighId { get; set; }
    public long LowId { get; set; }

    [JsonIgnore]
    public LogicLong AccountId => new((int)HighId, (int)LowId);

    public string Token { get; set; } = string.Empty;
    public string Name { get; set; } = "Steve";
    public bool NameSet { get; set; } = true;

    public int ProfileIcon { get; set; } = 0;
    public int NameColor { get; set; } = 0;

    public int Gems { get; set; } = 999999;
    public int FreeGems { get; set; } = 999999;
    public int Gold { get; set; } = 999999;
    public int StarPoints { get; set; } = 50000;
    public int Trophies { get; set; } = 1337;
    public int HighestTrophies { get; set; } = 1337;
    public int Level { get; set; } = 50;
    public int Experience { get; set; } = 10000;

    public int SelectedBrawler { get; set; } = 0;

    public Dictionary<int, BrawlerData> Brawlers { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastOnline { get; set; } = DateTime.UtcNow;

    public BrawlerData GetOrCreateBrawler(int characterId)
    {
        if (Brawlers.TryGetValue(characterId, out var brawler))
            return brawler;

        brawler = new BrawlerData
        {
            CharacterId = characterId,
            CardId = BrawlerRegistry.GetUnlockCardId(characterId),
            Unlocked = true,
            Level = 11,
            Trophies = 0,
            HighestTrophies = 0,
            SelectedSkin = 0
        };
        Brawlers[characterId] = brawler;
        return brawler;
    }

    /// <summary>True if the player owns a star power / gadget card (all of them when UnlockAllCards is on).</summary>
    public bool OwnsAbilityCard(int cardId)
    {
        var cfg = Config.ServerConfig.Instance.Gameplay;
        if (!cfg.UnlockAllCards || cardId > cfg.MaxCardId) return false;
        return CardRegistry.IsStarPower(cardId) || CardRegistry.IsGadget(cardId);
    }

    public void SanitizeState()
    {
        if (SelectedBrawler < 0 || !BrawlerRegistry.IsPlayableBrawler(SelectedBrawler))
        {
            SelectedBrawler = 0;
        }

        var invalidBrawlers = new List<int>();
        foreach (var (charId, brawler) in Brawlers)
        {
            if (!BrawlerRegistry.IsPlayableBrawler(charId))
            {
                invalidBrawlers.Add(charId);
                continue;
            }

            if (brawler.SelectedSkin > 0)
            {
                var owner = SkinRegistry.GetCharacterForSkin(brawler.SelectedSkin);
                if (owner != charId)
                {
                    Console.WriteLine($"[Sanitize] Fixed invalid SelectedSkin {brawler.SelectedSkin} for Brawler {charId} (actual owner: {owner}). Reset to 0.");
                    brawler.SelectedSkin = 0;
                }
            }

            if (brawler.SelectedStarPower != 0 && !CardRegistry.IsStarPower(brawler.SelectedStarPower))
            {
                Console.WriteLine($"[Sanitize] Fixed invalid SelectedStarPower {brawler.SelectedStarPower} for Brawler {charId}. Reset to 0.");
                brawler.SelectedStarPower = 0;
            }

            if (brawler.SelectedGadget != 0 && !CardRegistry.IsGadget(brawler.SelectedGadget))
            {
                Console.WriteLine($"[Sanitize] Fixed invalid SelectedGadget {brawler.SelectedGadget} for Brawler {charId}. Reset to 0.");
                brawler.SelectedGadget = 0;
            }
        }

        foreach (var id in invalidBrawlers)
        {
            Brawlers.Remove(id);
            Console.WriteLine($"[Sanitize] Removed disabled/invalid Brawler {id} from player state.");
        }
    }
}

