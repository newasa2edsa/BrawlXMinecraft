using System;
using System.IO;
using System.Text.Json;

namespace CustomBrawlServer.Config;

public class ServerSettings
{
    public string ServerName { get; set; } = "CustomBrawlServer v68";
    public string BindAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 9339;
    public string Environment { get; set; } = "dev";
    public bool DebugLogging { get; set; } = true;
    public bool EnableBridge { get; set; } = true;
    public string BridgeBindAddress { get; set; } = "127.0.0.1";
    public int BridgePort { get; set; } = 8080;
    public bool SimulateOfflineBattles { get; set; } = false;
}

public class GameplaySettings
{
    public int StartingGems { get; set; } = 999999;
    public int StartingGold { get; set; } = 999999;
    public int StartingStarPoints { get; set; } = 50000;
    public int StartingTrophies { get; set; } = 1337;
    public int StartingLevel { get; set; } = 50;
    public bool UnlockAllBrawlers { get; set; } = true;
    public int DefaultBrawlerLevel { get; set; } = 11;
    public int SelectedBrawlerId { get; set; } = 0;

    /// <summary>Own every star power + gadget card up to MaxCardId (sent in the home data).</summary>
    public bool UnlockAllCards { get; set; } = true;

    /// <summary>Highest card id that exists in the client data. See CardRegistry.DefaultMaxCardId.</summary>
    public int MaxCardId { get; set; } = 1321;
}

public class ServerConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ServerSettings Server { get; set; } = new();
    public GameplaySettings Gameplay { get; set; } = new();

    public static ServerConfig Instance { get; private set; } = new();

    public static void Load(string filePath = "config.json")
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<ServerConfig>(json, JsonOptions);
                if (loaded != null)
                {
                    Instance = loaded;
                    return;
                }
            }

            // If not found or failed, save defaults
            Instance = new ServerConfig();
            Save(filePath);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Config] Warning: Failed to load '{filePath}': {ex.Message}. Using defaults.");
            Console.ResetColor();
            Instance = new ServerConfig();
        }
    }

    public static void Save(string filePath = "config.json")
    {
        try
        {
            var json = JsonSerializer.Serialize(Instance, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Error saving '{filePath}': {ex.Message}");
        }
    }
}
