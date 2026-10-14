using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CustomBrawlServer.Config;

namespace CustomBrawlServer.Logic;

public class PlayerManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<long, Player> _activePlayers = new();
    private readonly string _storageDir;
    private long _maxPlayerId = 0;
    private readonly object _idLock = new();

    public PlayerManager(string storageDir = "data/players")
    {
        _storageDir = storageDir;
        Directory.CreateDirectory(_storageDir);
        InitializeMaxId();
    }

    private void InitializeMaxId()
    {
        try
        {
            var files = Directory.GetFiles(_storageDir, "*.json");
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (long.TryParse(name, out var id) && id > _maxPlayerId)
                {
                    _maxPlayerId = id;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerManager] Error scanning storage directory: {ex.Message}");
        }
    }

    private long NextId()
    {
        lock (_idLock)
        {
            return ++_maxPlayerId;
        }
    }

    public void NormalizePlayer(Player player)
    {
        var cfg = ServerConfig.Instance.Gameplay;
        if (cfg.UnlockAllBrawlers)
        {
            foreach (var kvp in BrawlerRegistry.CharacterToUnlockCard)
            {
                var charId = kvp.Key;
                var cardId = kvp.Value;
                if (!player.Brawlers.TryGetValue(charId, out var brawler))
                {
                    player.Brawlers[charId] = new BrawlerData
                    {
                        CharacterId = charId,
                        CardId = cardId,
                        Unlocked = true,
                        Level = cfg.DefaultBrawlerLevel,
                        Trophies = 0,
                        HighestTrophies = 0,
                        SelectedSkin = 0
                    };
                }
                else
                {
                    brawler.CharacterId = charId;
                    brawler.CardId = cardId;
                    brawler.Unlocked = true;
                    if (brawler.Level < 1) brawler.Level = cfg.DefaultBrawlerLevel;
                }
            }

            // Remove any non-playable character entries (dummies, turrets, pets)
            var invalidIds = player.Brawlers.Keys.Where(k => !BrawlerRegistry.IsPlayableBrawler(k)).ToList();
            foreach (var id in invalidIds)
            {
                player.Brawlers.Remove(id);
            }
        }
        else
        {
            if (!player.Brawlers.ContainsKey(0))
            {
                player.Brawlers[0] = new BrawlerData
                {
                    CharacterId = 0,
                    CardId = 0,
                    Unlocked = true,
                    Level = cfg.DefaultBrawlerLevel
                };
            }
        }

        player.SanitizeState();
    }

    public Player GetOrCreatePlayer(long highId, long lowId, string? passToken)
    {
        // If client requested existing ID, always honor its lowId and passToken so switching server builds
        // or starting with a fresh data/players directory never causes a client AccountManager mismatch crash!
        if (lowId > 0)
        {
            lock (_idLock)
            {
                if (lowId > _maxPlayerId)
                {
                    _maxPlayerId = lowId;
                }
            }

            if (_activePlayers.TryGetValue(lowId, out var cached))
            {
                if (!string.IsNullOrEmpty(passToken) && cached.Token != passToken)
                {
                    cached.Token = passToken;
                }
                cached.LastOnline = DateTime.UtcNow;
                NormalizePlayer(cached);
                SavePlayer(cached);
                return cached;
            }

            var filePath = Path.Combine(_storageDir, $"{lowId}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var loaded = JsonSerializer.Deserialize<Player>(json, JsonOptions);
                    if (loaded != null)
                    {
                        loaded.LowId = lowId;
                        loaded.HighId = highId >= 0 ? highId : 0;
                        if (!string.IsNullOrEmpty(passToken) && loaded.Token != passToken)
                        {
                            loaded.Token = passToken;
                        }
                        else if (string.IsNullOrEmpty(loaded.Token))
                        {
                            loaded.Token = GenerateToken();
                        }
                        loaded.LastOnline = DateTime.UtcNow;
                        NormalizePlayer(loaded);
                        SavePlayer(loaded);
                        _activePlayers[lowId] = loaded;
                        return loaded;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PlayerManager] Failed to load player {lowId}: {ex.Message}");
                }
            }
        }

        // Create new player — if the client supplied a lowId > 0 (cached from a previous server build),
        // preserve that exact lowId and passToken so the client never sees an AccountId mismatch!
        var newLowId = lowId > 0 ? lowId : NextId();
        var newToken = !string.IsNullOrEmpty(passToken) ? passToken : GenerateToken();
        var cfg = ServerConfig.Instance.Gameplay;

        var player = new Player
        {
            HighId = highId >= 0 ? highId : 0,
            LowId = newLowId,
            Token = newToken,
            Name = "Steve",
            NameSet = true,
            ProfileIcon = 0,
            NameColor = 0,
            Gems = cfg.StartingGems,
            FreeGems = cfg.StartingGems,
            Gold = cfg.StartingGold,
            StarPoints = cfg.StartingStarPoints,
            Trophies = cfg.StartingTrophies,
            HighestTrophies = cfg.StartingTrophies,
            Level = cfg.StartingLevel,
            SelectedBrawler = cfg.SelectedBrawlerId,
            CreatedAt = DateTime.UtcNow,
            LastOnline = DateTime.UtcNow
        };

        NormalizePlayer(player);

        SavePlayer(player);
        _activePlayers[newLowId] = player;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PlayerManager] Initialized player account #{newLowId} with token {newToken[..Math.Min(6, newToken.Length)]}...");
        Console.ResetColor();

        return player;
    }

    public void SavePlayer(Player player)
    {
        try
        {
            var filePath = Path.Combine(_storageDir, $"{player.LowId}.json");

            // One writer at a time per player, and write-then-rename so a crash or a second session
            // saving the same account can never leave a half-written (unloadable) json behind.
            lock (player)
            {
                var json = JsonSerializer.Serialize(player, JsonOptions);
                var tempPath = $"{filePath}.{Environment.CurrentManagedThreadId}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerManager] Error saving player {player.LowId}: {ex.Message}");
        }
    }

    public ICollection<Player> GetActivePlayers() => _activePlayers.Values;

    public Player? GetPlayerById(long lowId)
    {
        if (_activePlayers.TryGetValue(lowId, out var player))
            return player;

        var filePath = Path.Combine(_storageDir, $"{lowId}.json");
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Player>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string GenerateToken()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = RandomNumberGenerator.GetBytes(32);
        var result = new char[32];
        for (var i = 0; i < 32; i++)
            result[i] = chars[bytes[i] % chars.Length];
        return new string(result);
    }
}
