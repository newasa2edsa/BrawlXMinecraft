using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CustomBrawlServer.Admin;
using CustomBrawlServer.Config;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Network;

namespace CustomBrawlServer;

public static class Program
{
    private static TcpServer? _server;
    private static readonly CancellationTokenSource ShutdownCts = new();

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "CustomBrawlServer v68 - Brawl Stars Private Server";

        PrintBanner();

        // 1. Load Configuration
        Console.WriteLine("[Init] Loading configuration...");
        ServerConfig.Load();
        var cfg = ServerConfig.Instance;
        Console.WriteLine($"[Init] Server Name: {cfg.Server.ServerName}");
        Console.WriteLine($"[Init] Target Port: {cfg.Server.Port} (Bind: {cfg.Server.BindAddress})");
        Console.WriteLine($"[Init] Environment: {cfg.Server.Environment}");

        // 2. Initialize Player Manager & Storage
        Console.WriteLine("[Init] Initializing player database...");
        var playerManager = new PlayerManager("data/players");
        Console.WriteLine($"[Init] Player storage ready at data/players/ (Found {playerManager.GetActivePlayers().Count} existing accounts)");

        // Pre-warm BrawlerCatalog & OwnHomeDataMessage so the very first client login has zero JIT latency
        try
        {
            var warmupPlayer = playerManager.GetOrCreatePlayer(0, 1, null);
            var warmupHome = new CustomBrawlServer.Protocol.Messages.Server.OwnHomeDataMessage(warmupPlayer);
            warmupHome.Encode();
        }
        catch
        {
        }

        // 3. Initialize TCP, UDP, Battle Manager, and Minecraft WebSocket/TCP Bridge
        BattleManager.Instance.Initialize(playerManager);
        _server = new TcpServer(playerManager);
        var udpServer = new UdpServer();
        udpServer.Start();
        var bridgeServer = new BridgeServer(playerManager);
        var bridgeTask = bridgeServer.StartAsync();

        // 4. Hook graceful shutdown
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[System] Shutdown requested via Ctrl+C...");
            ShutdownCts.Cancel();
            BattleManager.Instance.Stop();
            bridgeServer.Stop();
            _server.Stop();
            udpServer.Stop();
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            BattleManager.Instance.Stop();
            bridgeServer.Stop();
            _server?.Stop();
            udpServer.Stop();
        };

        // 5. Start Server and Admin Console
        var serverTask = _server.StartAsync();
        var adminHandler = new ConsoleCommandHandler(_server, playerManager);
        var adminTask = Task.Run(() => adminHandler.RunAsync(ShutdownCts.Token));

        // Give the listener a moment to bind so a port-in-use / permission error is reported
        // instead of the "Successfully started" banner followed by a silent exit.
        await Task.Delay(300);
        if (serverTask.IsFaulted)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[Server] FAILED to start on {cfg.Server.BindAddress}:{cfg.Server.Port}: {serverTask.Exception?.GetBaseException().Message}");
            Console.WriteLine("[Server] Is another instance already running, or is the port blocked?");
            Console.ResetColor();
            BattleManager.Instance.Stop();
            bridgeServer.Stop();
            udpServer.Stop();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[Server] Successfully started! Ready for BSL v68 client connections on port {cfg.Server.Port}.");
        Console.WriteLine($"[Server] Minecraft WebSocket/TCP Bridge active at ws://localhost:{cfg.Server.BridgePort}.");
        Console.WriteLine("[Server] Type 'help' in this terminal for available admin commands.\n");
        Console.ResetColor();

        // Wait until server or admin stops
        await Task.WhenAny(serverTask, adminTask);

        BattleManager.Instance.Stop();
        bridgeServer.Stop();
        Console.WriteLine("[System] Server shutdown complete. Goodbye!");
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(@"
╔════════════════════════════════════════════════════════════════════╗
║                                                                    ║
║   ██████╗ ██████╗  █████╗ ██╗    ██╗██╗     ███████╗████████╗      ║
║   ██╔══██╗██╔══██╗██╔══██╗██║    ██║██║     ██╔════╝╚══██╔══╝      ║
║   ██████╔╝██████╔╝███████║██║ █╗ ██║██║     ███████╗   ██║         ║
║   ██╔══██╗██╔══██╗██╔══██║██║███╗██║██║     ╚════██║   ██║         ║
║   ██████╔╝██║  ██║██║  ██║╚███╔███╔╝███████╗███████║   ██║         ║
║   ╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝ ╚══╝╚══╝ ╚══════╝╚══════╝   ╚═╝         ║
║                                                                    ║
║               CUSTOM SERVER EMULATOR FOR BSL v68                   ║
║                  Built from Scratch with .NET 10                   ║
╚════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }
}
