using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CustomBrawlServer.Config;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Network;

public class TcpServer
{
    private readonly ConcurrentDictionary<long, ClientSession> _sessions = new();
    private readonly PlayerManager _playerManager;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _nextSessionId;

    public bool IsRunning { get; private set; }

    public TcpServer(PlayerManager playerManager)
    {
        _playerManager = playerManager;
    }

    public async Task StartAsync()
    {
        var cfg = ServerConfig.Instance.Server;
        var ip = cfg.BindAddress == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(cfg.BindAddress);

        _listener = new TcpListener(ip, cfg.Port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(100);

        _cts = new CancellationTokenSource();
        IsRunning = true;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[TcpServer] Listening on {cfg.BindAddress}:{cfg.Port}...");
        Console.ResetColor();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var socket = await _listener.AcceptSocketAsync(_cts.Token);
                socket.NoDelay = true;

                var sessionId = Interlocked.Increment(ref _nextSessionId);
                var session = new ClientSession(sessionId, socket, this, _playerManager);
                _sessions[sessionId] = session;

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"[TcpServer] Client connected from {socket.RemoteEndPoint} (Session #{sessionId})");
                Console.ResetColor();

                _ = Task.Run(session.StartAsync);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpServer] Accept loop error: {ex.Message}");
        }
        finally
        {
            Stop();
        }
    }

    public void RemoveSession(long sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    public int GetOnlineCount() => _sessions.Count;
    public IEnumerable<ClientSession> GetSessions() => _sessions.Values;

    public ClientSession? GetSessionByPlayerId(long playerId)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Player != null && session.Player.LowId == playerId)
                return session;
        }
        return null;
    }

    public async Task BroadcastAsync(PiranhaMessage message)
    {
        foreach (var session in _sessions.Values)
        {
            await session.SendMessageAsync(message);
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _cts?.Cancel();
        _listener?.Stop();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();

        Console.WriteLine("[TcpServer] Server stopped.");
    }
}
