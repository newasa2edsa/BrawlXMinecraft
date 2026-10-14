using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CustomBrawlServer.Config;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Network;
using CustomBrawlServer.Protocol;
using CustomBrawlServer.Protocol.Messages.Server;

namespace CustomBrawlServer.Admin;

public class ConsoleCommandHandler
{
    private readonly TcpServer _server;
    private readonly PlayerManager _playerManager;
    private readonly DateTime _startTime = DateTime.UtcNow;

    public ConsoleCommandHandler(TcpServer server, PlayerManager playerManager)
    {
        _server = server;
        _playerManager = playerManager;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        while (!cancellationToken.IsCancellationRequested && _server.IsRunning)
        {
            try
            {
                var input = Console.ReadLine();
                if (input == null)
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(input)) continue;

                var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();
                var args = parts.Skip(1).ToArray();

                switch (cmd)
                {
                    case "help":
                        PrintHelp();
                        break;

                    case "status":
                        PrintStatus();
                        break;

                    case "players":
                        PrintPlayers();
                        break;

                    case "givegems":
                        HandleGiveGems(args);
                        break;

                    case "givegold":
                        HandleGiveGold(args);
                        break;

                    case "settrophies":
                        HandleSetTrophies(args);
                        break;

                    case "unlockall":
                        HandleUnlockAll(args);
                        break;

                    case "unhandled":
                        UnhandledTracker.Print();
                        break;

                    case "rename":
                        await HandleRenameAsync(args);
                        break;

                    case "kick":
                        HandleKick(args);
                        break;

                    case "reload":
                        ServerConfig.Load();
                        Console.WriteLine("[Admin] Configuration reloaded successfully.");
                        break;

                    case "bridge":
                        Console.WriteLine($"[Bridge] Listening: {BridgeServer.Instance?.IsRunning ?? false} | Port: {ServerConfig.Instance.Server.BridgePort} | Connected Minecraft clients: {BridgeServer.Instance?.ClientCount ?? 0}");
                        break;

                    case "move":
                        HandleBridgeMove(args);
                        break;

                    case "attack":
                        HandleBridgeAttack(args);
                        break;

                    case "super":
                        HandleBridgeSuper(args);
                        break;

                    case "gadget":
                        BattleManager.Instance.TriggerTestEvent("gadget");
                        Console.WriteLine("[Bridge] Broadcasted gadget activation event.");
                        break;

                    case "hypercharge":
                        BattleManager.Instance.TriggerTestEvent("hypercharge");
                        Console.WriteLine("[Bridge] Broadcasted hypercharge activation event.");
                        break;

                    case "sim":
                        HandleSim(args);
                        break;

                    case "stop":
                    case "exit":
                    case "quit":
                        Console.WriteLine("[Admin] Stopping server...");
                        _server.Stop();
                        return;

                    default:
                        Console.WriteLine($"[Admin] Unknown command '{cmd}'. Type 'help' for available commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Admin] Command error: {ex.Message}");
            }
        }
    }

    private void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- Available Admin Commands ---");
        Console.WriteLine("  help                     - Show this command reference");
        Console.WriteLine("  status                   - Display server status, uptime, and memory usage");
        Console.WriteLine("  players                  - List all active and registered players");
        Console.WriteLine("  givegems <id> <amount>   - Add gems to player account");
        Console.WriteLine("  givegold <id> <amount>   - Add gold to player account");
        Console.WriteLine("  settrophies <id> <count> - Set trophies for player");
        Console.WriteLine("  unlockall <id>           - Unlock all brawlers (Lv 11) for player");
        Console.WriteLine("  rename <id> <name>       - Rename player");
        Console.WriteLine("  kick <id>                - Disconnect player session");
        Console.WriteLine("  unhandled                - Show client messages/commands the server has no handler for");
        Console.WriteLine("  bridge                   - Show Minecraft WebSocket/TCP bridge status (port 8080)");
        Console.WriteLine("  move [x] [y]             - Broadcast BSL movement event to Minecraft");
        Console.WriteLine("  attack [tx] [ty]         - Broadcast BSL basic attack event to Minecraft");
        Console.WriteLine("  super [tx] [ty]          - Broadcast BSL super ability event to Minecraft");
        Console.WriteLine("  gadget / hypercharge     - Broadcast BSL gadget or hypercharge event to Minecraft");
        Console.WriteLine("  sim <start|stop> [brawlerId] - Start/stop live 20 Hz arena match simulation");
        Console.WriteLine("  reload                   - Reload config.json");
        Console.WriteLine("  stop / exit              - Gracefully shut down server\n");
        Console.ResetColor();
    }

    private void PrintStatus()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var process = Process.GetCurrentProcess();
        var memoryMb = process.WorkingSet64 / (1024 * 1024);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n--- Server Status ---");
        Console.WriteLine($"  Uptime:           {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
        Console.WriteLine($"  Online Sessions:  {_server.GetOnlineCount()}");
        Console.WriteLine($"  Bridge Clients:   {BridgeServer.Instance?.ClientCount ?? 0} (ws://localhost:{ServerConfig.Instance.Server.BridgePort})");
        Console.WriteLine($"  Active Players:   {_playerManager.GetActivePlayers().Count}");
        Console.WriteLine($"  Memory (RAM):     {memoryMb} MB");
        Console.WriteLine($"  Port:             {ServerConfig.Instance.Server.Port}");
        Console.WriteLine($"  Environment:      {ServerConfig.Instance.Server.Environment}\n");
        Console.ResetColor();
    }

    private void PrintPlayers()
    {
        var active = _playerManager.GetActivePlayers().ToList();
        if (active.Count == 0)
        {
            Console.WriteLine("[Admin] No players currently loaded.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n--- Loaded Players ({active.Count}) ---");
        foreach (var p in active)
        {
            var isOnline = _server.GetSessionByPlayerId(p.LowId) != null;
            var statusStr = isOnline ? "[ONLINE]" : "[OFFLINE]";
            Console.WriteLine($"  {statusStr} #{p.LowId} | Name: '{p.Name}' | Gems: {p.Gems} | Gold: {p.Gold} | Trophies: {p.Trophies} | Brawlers: {p.Brawlers.Count(b => b.Value.Unlocked)}");
        }
        Console.WriteLine();
        Console.ResetColor();
    }

    private void HandleGiveGems(string[] args)
    {
        if (args.Length < 2 || !long.TryParse(args[0], out var id) || !int.TryParse(args[1], out var amount))
        {
            Console.WriteLine("[Admin] Usage: givegems <playerId> <amount>");
            return;
        }

        var player = _playerManager.GetPlayerById(id);
        if (player == null)
        {
            Console.WriteLine($"[Admin] Player #{id} not found.");
            return;
        }

        player.Gems += amount;
        player.FreeGems += amount;
        _playerManager.SavePlayer(player);
        Console.WriteLine($"[Admin] Gave {amount} gems to Player #{id}. New balance: {player.Gems}");
    }

    private void HandleGiveGold(string[] args)
    {
        if (args.Length < 2 || !long.TryParse(args[0], out var id) || !int.TryParse(args[1], out var amount))
        {
            Console.WriteLine("[Admin] Usage: givegold <playerId> <amount>");
            return;
        }

        var player = _playerManager.GetPlayerById(id);
        if (player == null)
        {
            Console.WriteLine($"[Admin] Player #{id} not found.");
            return;
        }

        player.Gold += amount;
        _playerManager.SavePlayer(player);
        Console.WriteLine($"[Admin] Gave {amount} gold to Player #{id}. New balance: {player.Gold}");
    }

    private void HandleSetTrophies(string[] args)
    {
        if (args.Length < 2 || !long.TryParse(args[0], out var id) || !int.TryParse(args[1], out var count))
        {
            Console.WriteLine("[Admin] Usage: settrophies <playerId> <count>");
            return;
        }

        var player = _playerManager.GetPlayerById(id);
        if (player == null)
        {
            Console.WriteLine($"[Admin] Player #{id} not found.");
            return;
        }

        player.Trophies = count;
        if (count > player.HighestTrophies)
            player.HighestTrophies = count;
        _playerManager.SavePlayer(player);
        Console.WriteLine($"[Admin] Updated Player #{id} trophies to {count}.");
    }

    private void HandleUnlockAll(string[] args)
    {
        if (args.Length < 1 || !long.TryParse(args[0], out var id))
        {
            Console.WriteLine("[Admin] Usage: unlockall <playerId>");
            return;
        }

        var player = _playerManager.GetPlayerById(id);
        if (player == null)
        {
            Console.WriteLine($"[Admin] Player #{id} not found.");
            return;
        }

        foreach (var characterId in BrawlerRegistry.CharacterToUnlockCard.Keys)
        {
            var brawler = player.GetOrCreateBrawler(characterId);
            brawler.Unlocked = true;
            brawler.Level = 11;
        }

        _playerManager.SavePlayer(player);
        Console.WriteLine($"[Admin] Unlocked all {BrawlerRegistry.BrawlerCount} brawlers at Level 11 for Player #{id}.");

        var session = _server.GetSessionByPlayerId(id);
        if (session != null)
        {
            _ = session.SendMessageAsync(new OwnHomeDataMessage(player));
        }
    }

    private async Task HandleRenameAsync(string[] args)
    {
        if (args.Length < 2 || !long.TryParse(args[0], out var id))
        {
            Console.WriteLine("[Admin] Usage: rename <playerId> <newName>");
            return;
        }

        var newName = string.Join(" ", args.Skip(1));
        var player = _playerManager.GetPlayerById(id);
        if (player == null)
        {
            Console.WriteLine($"[Admin] Player #{id} not found.");
            return;
        }

        player.Name = newName;
        player.NameSet = true;
        _playerManager.SavePlayer(player);
        Console.WriteLine($"[Admin] Renamed Player #{id} to '{newName}'.");

        var session = _server.GetSessionByPlayerId(id);
        if (session != null)
        {
            var cmdMsg = new AvailableServerCommandMessage(201, stream =>
            {
                stream.WriteString(player.Name);
                stream.WriteVInt(0);
                stream.WriteVInt(0);
                stream.WriteVInt(0);
                stream.WriteVLong(new LogicLong(0, 0));
            });
            await session.SendMessageAsync(cmdMsg);
        }
    }

    private void HandleKick(string[] args)
    {
        if (args.Length < 1 || !long.TryParse(args[0], out var id))
        {
            Console.WriteLine("[Admin] Usage: kick <playerId>");
            return;
        }

        var session = _server.GetSessionByPlayerId(id);
        if (session != null)
        {
            session.Disconnect();
            Console.WriteLine($"[Admin] Kicked Player #{id}.");
        }
        else
        {
            Console.WriteLine($"[Admin] Player #{id} is not online.");
        }
    }

    private Player GetDefaultPlayer()
    {
        return _playerManager.GetActivePlayers().FirstOrDefault()
               ?? _playerManager.GetOrCreatePlayer(0, 1, null);
    }

    private void HandleBridgeMove(string[] args)
    {
        var player = GetDefaultPlayer();
        var state = BattleManager.Instance.GetOrCreateState(player);
        var x = args.Length > 0 && int.TryParse(args[0], out var px) ? px : state.X + 300;
        var y = args.Length > 1 && int.TryParse(args[1], out var py) ? py : state.Y + 300;
        BattleManager.Instance.TriggerMove(player, x, y);
        Console.WriteLine($"[Bridge] Broadcasted move for Player #{player.LowId} to ({x}, {y}).");
    }

    private void HandleBridgeAttack(string[] args)
    {
        var player = GetDefaultPlayer();
        var state = BattleManager.Instance.GetOrCreateState(player);
        var tx = args.Length > 0 && int.TryParse(args[0], out var px) ? px : state.X + 1200;
        var ty = args.Length > 1 && int.TryParse(args[1], out var py) ? py : state.Y;
        BattleManager.Instance.TriggerAttack(player, tx, ty);
        Console.WriteLine($"[Bridge] Broadcasted basic attack for Player #{player.LowId} (Brawler {player.SelectedBrawler}) -> ({tx}, {ty}).");
    }

    private void HandleBridgeSuper(string[] args)
    {
        var player = GetDefaultPlayer();
        var state = BattleManager.Instance.GetOrCreateState(player);
        var tx = args.Length > 0 && int.TryParse(args[0], out var px) ? px : state.X + 1800;
        var ty = args.Length > 1 && int.TryParse(args[1], out var py) ? py : state.Y + 600;
        BattleManager.Instance.TriggerSuper(player, tx, ty);
        Console.WriteLine($"[Bridge] Broadcasted SUPER ability for Player #{player.LowId} (Brawler {player.SelectedBrawler}) -> ({tx}, {ty}).");
    }

    private void HandleSim(string[] args)
    {
        var player = GetDefaultPlayer();
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "start";
        if (mode == "stop")
        {
            BattleManager.Instance.EndMatch(player);
            Console.WriteLine($"[Bridge] Stopped live arena match simulation for Player #{player.LowId}.");
            return;
        }

        if (args.Length > 1 && int.TryParse(args[1], out var brawlerId) && BrawlerRegistry.IsPlayableBrawler(brawlerId))
        {
            player.SelectedBrawler = brawlerId;
            _playerManager.SavePlayer(player);
        }

        var brawlerData = player.GetOrCreateBrawler(player.SelectedBrawler);
        BattleManager.Instance.StartMatch(player, player.SelectedBrawler, brawlerData.SelectedSkin, 121);
        Console.WriteLine($"[Bridge] Started live 20 Hz arena match simulation for Player #{player.LowId} (Brawler {player.SelectedBrawler} - {BrawlerCatalog.Get(player.SelectedBrawler).DisplayName}).");
    }
}
