using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CustomBrawlServer.Config;
using CustomBrawlServer.Logic;

namespace CustomBrawlServer.Network;

/// <summary>
/// Dual-mode WebSocket (RFC 6455, ws://localhost:8080) and raw TCP JSON-line server on port 8080.
/// Broadcasts live BSL v68 match & character events to connected Minecraft Java Edition clients.
/// </summary>
public sealed class BridgeServer
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public static BridgeServer? Instance { get; private set; }

    private readonly PlayerManager _playerManager;
    private readonly ConcurrentDictionary<long, BridgeClient> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _nextClientId;

    public bool IsRunning { get; private set; }
    public int ClientCount => _clients.Count;

    public BridgeServer(PlayerManager playerManager)
    {
        _playerManager = playerManager;
        Instance = this;
    }

    public async Task StartAsync()
    {
        var cfg = ServerConfig.Instance.Server;
        if (!cfg.EnableBridge)
        {
            Console.WriteLine("[BridgeServer] Disabled in config.json.");
            return;
        }

        var ip = cfg.BridgeBindAddress is "0.0.0.0" or "*"
            ? IPAddress.Any
            : (cfg.BridgeBindAddress == "localhost" ? IPAddress.Loopback : IPAddress.Parse(cfg.BridgeBindAddress));

        _listener = new TcpListener(ip, cfg.BridgePort);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(64);

        _cts = new CancellationTokenSource();
        IsRunning = true;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[BridgeServer] WebSocket/TCP event bridge listening on ws://localhost:{cfg.BridgePort} ({ip}:{cfg.BridgePort})...");
        Console.ResetColor();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var socket = await _listener.AcceptSocketAsync(_cts.Token);
                socket.NoDelay = true;

                var id = Interlocked.Increment(ref _nextClientId);
                var client = new BridgeClient(id, socket);
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BridgeServer] Listener error: {ex.Message}");
        }
        finally
        {
            Stop();
        }
    }

    private async Task HandleClientAsync(BridgeClient client, CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(TimeSpan.FromSeconds(2));

            int read = 0;
            try
            {
                read = await client.Socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, probeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Raw TCP client that connected and waits passively for server broadcasts without sending first
                client.IsWebSocket = false;
            }

            if (read > 0)
            {
                var headerText = Encoding.ASCII.GetString(buffer, 0, read);
                if (headerText.StartsWith("GET ", StringComparison.OrdinalIgnoreCase) &&
                    headerText.Contains("Upgrade: websocket", StringComparison.OrdinalIgnoreCase))
                {
                    var wsKey = ExtractWebSocketKey(headerText);
                    if (!string.IsNullOrEmpty(wsKey))
                    {
                        var acceptKey = ComputeWebSocketAccept(wsKey);
                        var response =
                            "HTTP/1.1 101 Switching Protocols\r\n" +
                            "Upgrade: websocket\r\n" +
                            "Connection: Upgrade\r\n" +
                            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
                        var respBytes = Encoding.ASCII.GetBytes(response);
                        await client.Socket.SendAsync(respBytes.AsMemory(), SocketFlags.None, ct);
                        client.IsWebSocket = true;
                    }
                }
            }

            _clients[client.Id] = client;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[BridgeServer] Minecraft/Bridge client #{client.Id} connected ({(client.IsWebSocket ? "WebSocket" : "Raw TCP")}) from {client.Socket.RemoteEndPoint}");
            Console.ResetColor();

            // Send initial hello + current active brawler states
            var cfg = ServerConfig.Instance.Server;
            var helloJson = JsonSerializer.Serialize(new
            {
                @event = "hello",
                v = 1,
                server = cfg.ServerName,
                gamePort = cfg.Port,
                bridgePort = cfg.BridgePort,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            await client.SendAsync(helloJson);

            foreach (var player in _playerManager.GetActivePlayers())
            {
                var stateJson = BuildCharacterStateJson(player, "sync", online: true);
                await client.SendAsync(stateJson);
            }

            // If we already read a non-HTTP JSON command during probe, process it
            if (read > 0 && !client.IsWebSocket)
            {
                var initialText = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                if (initialText.StartsWith("{"))
                {
                    HandleIncomingBridgeMessage(initialText);
                }
            }

            if (client.IsWebSocket)
            {
                await ReadWebSocketLoopAsync(client, ct);
            }
            else
            {
                await ReadRawTcpLoopAsync(client, ct);
            }
        }
        catch (Exception ex)
        {
            if (ServerConfig.Instance.Server.DebugLogging && ex is not OperationCanceledException and not SocketException)
            {
                Console.WriteLine($"[BridgeServer] Client #{client.Id} error: {ex.Message}");
            }
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            client.Dispose();
            Console.WriteLine($"[BridgeServer] Client #{client.Id} disconnected.");
        }
    }

    private async Task ReadWebSocketLoopAsync(BridgeClient client, CancellationToken ct)
    {
        var header = new byte[2];
        while (!ct.IsCancellationRequested && client.Socket.Connected)
        {
            if (!await ReceiveExactAsync(client.Socket, header, ct)) break;

            var opcode = header[0] & 0x0F;
            var masked = (header[1] & 0x80) != 0;
            long payloadLen = header[1] & 0x7F;

            if (payloadLen == 126)
            {
                var ext = new byte[2];
                if (!await ReceiveExactAsync(client.Socket, ext, ct)) break;
                payloadLen = BinaryPrimitives.ReadUInt16BigEndian(ext);
            }
            else if (payloadLen == 127)
            {
                var ext = new byte[8];
                if (!await ReceiveExactAsync(client.Socket, ext, ct)) break;
                payloadLen = (long)BinaryPrimitives.ReadUInt64BigEndian(ext);
            }

            var maskKey = new byte[4];
            if (masked && !await ReceiveExactAsync(client.Socket, maskKey, ct)) break;

            if (payloadLen < 0 || payloadLen > 1024 * 1024) break;
            var payload = new byte[payloadLen];
            if (payloadLen > 0 && !await ReceiveExactAsync(client.Socket, payload, ct)) break;

            if (masked)
            {
                for (var i = 0; i < payload.Length; i++)
                    payload[i] ^= maskKey[i & 3];
            }

            if (opcode == 0x8) // Close
            {
                break;
            }
            else if (opcode == 0x9) // Ping -> Pong
            {
                await client.SendRawWebSocketFrameAsync(0xA, payload);
            }
            else if (opcode == 0x1 && payload.Length > 0) // Text frame
            {
                var text = Encoding.UTF8.GetString(payload);
                HandleIncomingBridgeMessage(text);
            }
        }
    }

    private async Task ReadRawTcpLoopAsync(BridgeClient client, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested && client.Socket.Connected)
        {
            var read = await client.Socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct);
            if (read <= 0) break;

            var lines = Encoding.UTF8.GetString(buffer, 0, read)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("{"))
                    HandleIncomingBridgeMessage(line);
            }
        }
    }

    private void HandleIncomingBridgeMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var evt = root.TryGetProperty("event", out var eProp) ? eProp.GetString()
                    : (root.TryGetProperty("t", out var tProp) ? tProp.GetString() : null);

            if (evt == "mc_damage" && root.TryGetProperty("playerId", out var pidProp))
            {
                var pid = pidProp.GetInt64();
                var dmg = root.TryGetProperty("damage", out var dProp) ? dProp.GetInt32() : 400;
                BattleManager.Instance.ApplyExternalDamage(pid, dmg);
            }
            else if (evt == "trigger_test")
            {
                var kind = root.TryGetProperty("kind", out var kProp) ? kProp.GetString() ?? "attack" : "attack";
                BattleManager.Instance.TriggerTestEvent(kind);
            }
        }
        catch
        {
            // Ignore malformed client messages
        }
    }

    public void BroadcastJson(string json)
    {
        if (!IsRunning || _clients.IsEmpty) return;

        foreach (var client in _clients.Values)
        {
            _ = client.SendAsync(json);
        }
    }

    public void BroadcastCharacterState(
        Player player,
        string action,
        bool online = true,
        bool? inBattleOverride = null,
        int? xOverride = null,
        int? yOverride = null,
        int? zOverride = null,
        double? yawOverride = null,
        int emoteId = 0)
    {
        var json = BuildCharacterStateJson(player, action, online, inBattleOverride, xOverride, yOverride, zOverride, yawOverride, emoteId);
        BroadcastJson(json);
    }

    public string BuildCharacterStateJson(
        Player player,
        string action,
        bool online = true,
        bool? inBattleOverride = null,
        int? xOverride = null,
        int? yOverride = null,
        int? zOverride = null,
        double? yawOverride = null,
        int emoteId = 0)
    {
        var brawlerId = player.SelectedBrawler;
        var brawlerData = player.GetOrCreateBrawler(brawlerId);
        var spec = BrawlerCatalog.Get(brawlerId);
        var bState = BattleManager.Instance.GetOrCreateState(player);

        var levelMultiplier = 1.0 + Math.Max(0, brawlerData.Level - 1) * 0.10;
        var scaledMaxHp = (int)Math.Round(spec.BaseHp * levelMultiplier);
        if (bState.MaxHp != scaledMaxHp)
        {
            bState.MaxHp = scaledMaxHp;
            if (bState.Hp <= 0 || bState.Hp > scaledMaxHp)
                bState.Hp = scaledMaxHp;
        }

        return JsonSerializer.Serialize(new
        {
            @event = "character_state",
            v = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            playerId = player.LowId,
            playerName = player.Name,
            action,
            online,
            inBattle = inBattleOverride ?? bState.InBattle,
            brawlerId,
            brawlerName = spec.CodeName,
            displayName = spec.DisplayName,
            skinId = brawlerData.SelectedSkin,
            level = brawlerData.Level,
            starPowerId = brawlerData.SelectedStarPower,
            gadgetId = brawlerData.SelectedGadget,
            emoteId,
            hp = bState.Hp,
            maxHp = bState.MaxHp,
            speed = spec.Speed,
            superCharge = bState.SuperCharge,
            hypercharged = bState.Hypercharged,
            x = xOverride ?? bState.X,
            y = yOverride ?? bState.Y,
            z = zOverride ?? bState.Z,
            yaw = Math.Round(yawOverride ?? bState.Yaw, 2)
        });
    }

    public void BroadcastMove(Player player, int brawlerId, int x, int y, int z, int targetX, int targetY, double yaw)
    {
        var spec = BrawlerCatalog.Get(brawlerId);
        var json = JsonSerializer.Serialize(new
        {
            @event = "move",
            v = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            playerId = player.LowId,
            playerName = player.Name,
            brawlerId,
            brawlerName = spec.CodeName,
            displayName = spec.DisplayName,
            x,
            y,
            z,
            targetX,
            targetY,
            yaw = Math.Round(yaw, 2),
            speed = spec.Speed
        });
        BroadcastJson(json);
    }

    public void BroadcastAttack(Player player, int brawlerId, int x, int y, int z, int targetX, int targetY, int targetZ, double yaw)
    {
        var brawlerData = player.GetOrCreateBrawler(brawlerId);
        var spec = BrawlerCatalog.Get(brawlerId);
        var levelMultiplier = 1.0 + Math.Max(0, brawlerData.Level - 1) * 0.10;
        var scaledDamage = (int)Math.Round(spec.WeaponDamage * levelMultiplier);

        var json = JsonSerializer.Serialize(new
        {
            @event = "attack",
            v = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            playerId = player.LowId,
            playerName = player.Name,
            brawlerId,
            brawlerName = spec.CodeName,
            displayName = spec.DisplayName,
            skillName = spec.WeaponSkill,
            behavior = spec.WeaponBehavior,
            damage = scaledDamage,
            range = spec.WeaponRange,
            explosive = spec.ExplosiveWeapon,
            x,
            y,
            z,
            targetX,
            targetY,
            targetZ,
            yaw = Math.Round(yaw, 2)
        });
        BroadcastJson(json);
    }

    public void BroadcastSuper(Player player, int brawlerId, int x, int y, int z, int targetX, int targetY, int targetZ, double yaw)
    {
        var brawlerData = player.GetOrCreateBrawler(brawlerId);
        var spec = BrawlerCatalog.Get(brawlerId);
        var levelMultiplier = 1.0 + Math.Max(0, brawlerData.Level - 1) * 0.10;
        var scaledDamage = (int)Math.Round(spec.UltiDamage * levelMultiplier);

        var json = JsonSerializer.Serialize(new
        {
            @event = "super",
            v = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            playerId = player.LowId,
            playerName = player.Name,
            brawlerId,
            brawlerName = spec.CodeName,
            displayName = spec.DisplayName,
            skillName = spec.UltiSkill,
            behavior = spec.UltiBehavior,
            damage = scaledDamage,
            range = spec.UltiRange,
            explosive = spec.ExplosiveUlti,
            summon = spec.SummonCharacter,
            x,
            y,
            z,
            targetX,
            targetY,
            targetZ,
            yaw = Math.Round(yaw, 2)
        });
        BroadcastJson(json);
    }

    public void BroadcastAbility(Player player, int brawlerId, string subType, int cardId, int x, int y, int z)
    {
        var spec = BrawlerCatalog.Get(brawlerId);
        var json = JsonSerializer.Serialize(new
        {
            @event = "ability",
            v = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            subType,
            playerId = player.LowId,
            playerName = player.Name,
            brawlerId,
            brawlerName = spec.CodeName,
            displayName = spec.DisplayName,
            cardId,
            x,
            y,
            z
        });
        BroadcastJson(json);
    }

    public void BroadcastMatchState(Player player, string state, int brawlerId, int skinId, int locationId)
    {
        var spec = BrawlerCatalog.Get(brawlerId);
        var json = JsonSerializer.Serialize(new
        {
            @event = "match_state",
            v = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            state,
            playerId = player.LowId,
            playerName = player.Name,
            brawlerId,
            brawlerName = spec.CodeName,
            displayName = spec.DisplayName,
            skinId,
            locationId,
            arenaWidth = 6300,
            arenaHeight = 9900
        });
        BroadcastJson(json);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _cts?.Cancel();
        _listener?.Stop();

        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();

        Console.WriteLine("[BridgeServer] Bridge stopped.");
    }

    private static string? ExtractWebSocketKey(string request)
    {
        var lines = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                return line["Sec-WebSocket-Key:".Length..].Trim();
            }
        }
        return null;
    }

    private static string ComputeWebSocketAccept(string key)
    {
        var combined = Encoding.ASCII.GetBytes(key + WebSocketGuid);
        var hash = SHA1.HashData(combined);
        return Convert.ToBase64String(hash);
    }

    private static async Task<bool> ReceiveExactAsync(Socket socket, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(offset, buffer.Length - offset), SocketFlags.None, ct);
            if (read <= 0) return false;
            offset += read;
        }
        return true;
    }

    private sealed class BridgeClient : IDisposable
    {
        public long Id { get; }
        public Socket Socket { get; }
        public bool IsWebSocket { get; set; }
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private bool _disposed;

        public BridgeClient(long id, Socket socket)
        {
            Id = id;
            Socket = socket;
        }

        public async Task SendAsync(string json)
        {
            if (_disposed || !Socket.Connected) return;

            if (IsWebSocket)
            {
                var payload = Encoding.UTF8.GetBytes(json);
                await SendRawWebSocketFrameAsync(0x1, payload);
            }
            else
            {
                var payload = Encoding.UTF8.GetBytes(json + "\n");
                await _sendLock.WaitAsync();
                try
                {
                    await Socket.SendAsync(payload.AsMemory(), SocketFlags.None);
                }
                catch
                {
                    // Disconnected
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }

        public async Task SendRawWebSocketFrameAsync(byte opcode, byte[] payload)
        {
            if (_disposed || !Socket.Connected) return;

            byte[] frame;
            if (payload.Length < 126)
            {
                frame = new byte[2 + payload.Length];
                frame[0] = (byte)(0x80 | (opcode & 0x0F));
                frame[1] = (byte)payload.Length;
                Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                frame = new byte[4 + payload.Length];
                frame[0] = (byte)(0x80 | (opcode & 0x0F));
                frame[1] = 126;
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), (ushort)payload.Length);
                Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            }
            else
            {
                frame = new byte[10 + payload.Length];
                frame[0] = (byte)(0x80 | (opcode & 0x0F));
                frame[1] = 127;
                BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(2, 8), (ulong)payload.Length);
                Buffer.BlockCopy(payload, 0, frame, 10, payload.Length);
            }

            await _sendLock.WaitAsync();
            try
            {
                await Socket.SendAsync(frame.AsMemory(), SocketFlags.None);
            }
            catch
            {
                // Disconnected
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Socket.Shutdown(SocketShutdown.Both); } catch { }
            try { Socket.Close(); } catch { }
            Socket.Dispose();
            _sendLock.Dispose();
        }
    }
}
