using System;
using System.Buffers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CustomBrawlServer.Config;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Network.Crypto;
using CustomBrawlServer.Protocol;
using CustomBrawlServer.Protocol.Messages.Client;
using CustomBrawlServer.Protocol.Messages.Server;

namespace CustomBrawlServer.Network;

public class ClientSession : IDisposable
{
    private readonly Socket _socket;
    private readonly TcpServer _server;
    private readonly PlayerManager _playerManager;
    private readonly PacketFramer _framer = new();

    private readonly byte[] _sessionToken = new byte[24];
    private readonly byte[] _secretKey = new byte[32];
    private readonly byte[] _sNonce = new byte[24];
    private byte[] _clientPk = Array.Empty<byte>();
    private byte[] _rNonce = Array.Empty<byte>();

    private PepperEncrypter? _decryptStream;
    private PepperEncrypter? _encryptStream;

    // 0 = Plaintext, 2 = Connected, 3 = HelloReceived, 4 = LoginReceived (Send LoginOk via Box), 5 = Streaming
    private int _pepperState = 2;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    public long SessionId { get; }
    public Player? Player { get; private set; }
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    public ClientSession(long sessionId, Socket socket, TcpServer server, PlayerManager playerManager)
    {
        SessionId = sessionId;
        _socket = socket;
        _server = server;
        _playerManager = playerManager;

        RandomNumberGenerator.Fill(_sessionToken);
        RandomNumberGenerator.Fill(_secretKey);
        RandomNumberGenerator.Fill(_sNonce);
    }

    public async Task StartAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
            while (!_cts.IsCancellationRequested && _socket.Connected)
            {
                var bytesRead = await _socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, _cts.Token);
                if (bytesRead <= 0) break;

                LastActivity = DateTime.UtcNow;
                _framer.Append(buffer, 0, bytesRead);

                var packets = _framer.ReadPackets();
                foreach (var packet in packets)
                {
                    await ProcessPacketAsync(packet);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (SocketException)
        {
            // Client closed connection
        }
        catch (Exception ex)
        {
            if (ServerConfig.Instance.Server.DebugLogging)
                Console.WriteLine($"[Session #{SessionId}] Error in receive loop: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            Dispose();
        }
    }

    private async Task ProcessPacketAsync(RawPacket packet)
    {
        try
        {
            var type = packet.MessageType;
            var payload = packet.Payload;

            // Decrypt payload if in pepper mode
            switch (_pepperState)
            {
                case 2:
                    if (type == 10100)
                    {
                        _pepperState = 3;
                    }
                    else if (type == 10101)
                    {
                        _pepperState = 0; // Direct login without hello -> plaintext mode
                    }
                    break;

                case 3:
                    if (type == 10101)
                    {
                        if (payload.Length >= 32 + 16)
                        {
                            var decrypted = HandlePepperLogin(payload);
                            if (decrypted != null)
                            {
                                payload = decrypted;
                                _pepperState = 4;
                            }
                            else
                            {
                                _pepperState = 0;
                            }
                        }
                        else
                        {
                            _pepperState = 0;
                        }
                    }
                    break;

                case 5:
                    if (_decryptStream != null && payload.Length > 0)
                    {
                        var overhead = _decryptStream.GetEncryptionOverhead();
                        if (payload.Length < overhead)
                        {
                            return; // Malformed payload; skip
                        }

                        var decrypted = new byte[payload.Length - overhead];
                        var res = _decryptStream.Decrypt(payload, decrypted, payload.Length);
                        if (res != 0)
                        {
                            Console.WriteLine($"[Session #{SessionId}] Warning: Decryption failed for message {type} (skipping).");
                            return;
                        }
                        payload = decrypted;
                    }
                    break;
            }

            // Detect match start / end from PlayerStatusMessage (14366)
            // Note: In Brawl Stars v68, status=3 is Home Screen / Lobby Idle! Status=4 or 8 is Battle / Practice.
            if (type == 14366 && Player != null && payload.Length > 0)
            {
                try
                {
                    var bs = new ByteStream(payload, payload.Length);
                    var status = bs.ReadVInt();
                    var state = BattleManager.Instance.GetOrCreateState(Player);
                    if (status is 4 or 8 or 10 && !state.InBattle)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[Session #{SessionId}] Player #{Player.LowId} entered battle (status={status}).");
                        Console.ResetColor();
                        var brawlerData = Player.GetOrCreateBrawler(Player.SelectedBrawler);
                        BattleManager.Instance.StartMatch(Player, Player.SelectedBrawler, brawlerData.SelectedSkin, 15);
                    }
                    else if (status is 0 or 1 or 2 or 3 or 5 or 6 or 7 or 9 && state.InBattle)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[Session #{SessionId}] Player #{Player.LowId} returned to lobby (status={status}).");
                        Console.ResetColor();
                        BattleManager.Instance.EndMatch(Player);
                    }
                }
                catch
                {
                    // Ignore malformed status telemetry
                }
                return;
            }

            // Filter out common background telemetry messages silently
            if (IsBackgroundTelemetry(type))
            {
                return;
            }

            if (ServerConfig.Instance.Server.DebugLogging)
                Console.WriteLine($"[Session #{SessionId}] <-- Received packet ID {type} ({payload.Length} bytes)");

            // Handle decoded message
            var message = MessageFactory.CreateMessage(type);
            if (message == null)
            {
                Admin.UnhandledTracker.RecordMessage(type, payload);
                if (ServerConfig.Instance.Server.DebugLogging)
                    Console.WriteLine($"[Session #{SessionId}] Ignored unhandled message type: {type}");
                return;
            }

            if (payload.Length > 0)
            {
                message.ByteStream.SetByteArray(payload, payload.Length);
                message.Decode();
            }

            await HandleMessageAsync(message);
        }
        catch (Exception ex)
        {
            // Defensive: an error in one packet MUST NEVER crash the session or close the socket!
            Console.WriteLine($"[Session #{SessionId}] Warning: Error handling packet {packet.MessageType}: {ex.Message}");
        }
    }

    private static bool IsBackgroundTelemetry(int messageType)
    {
        return messageType is 10110  // AnalyticsEventMessage
            or 14366                 // PlayerStatusMessage
            or 13654                 // SocialStatusRequestMessage
            or 15081                 // NewsRequestMessage
            or 10618                 // EventSeenMessage
            or 17502                 // CatalogCheckMessage
            or 10099                 // LatencyPingMessage
            or 39004;                // TelemetryMessage
    }

    private async Task HandleMessageAsync(PiranhaMessage message)
    {
        switch (message)
        {
            case ClientHelloMessage:
                var serverHello = new ServerHelloMessage(_sessionToken);
                await SendMessageAsync(serverHello);
                break;

            case LoginMessage login:
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[Session #{SessionId}] Player login: ID=({login.HighId},{login.LowId}), v{login.ClientMajorVersion}.{login.ClientMinor}.{login.ClientBuild}");
                Console.ResetColor();

                if (login.ClientMajorVersion != 68)
                {
                    Console.WriteLine($"[Session #{SessionId}] Unsupported client version: {login.ClientMajorVersion}");
                    await SendMessageAsync(new LoginFailedMessage(1, $"Please use Brawl Stars client version 68 (connected: v{login.ClientMajorVersion})"));
                    return;
                }

                Player = _playerManager.GetOrCreatePlayer(login.HighId, login.LowId, login.PassToken);

                // Send LoginOkMessage mirroring the client's minor and build versions
                var loginOk = new LoginOkMessage(Player, login.ClientMinor, login.ClientBuild);
                await SendMessageAsync(loginOk);

                // Brief delay so LoginOkMessage (state 4 -> 5) is processed before OwnHomeDataMessage arrives
                await Task.Delay(20);

                // Send OwnHomeDataMessage
                var ownHomeData = new OwnHomeDataMessage(Player);
                await SendMessageAsync(ownHomeData);

                BattleManager.Instance.EndMatch(Player);
                BridgeServer.Instance?.BroadcastCharacterState(Player, "login", online: true, inBattleOverride: false);
                break;

            case KeepAliveMessage:
                await SendMessageAsync(new KeepAliveOkMessage());
                break;

            case ChangeAvatarNameMessage changeName:
                if (Player != null && !string.IsNullOrWhiteSpace(changeName.AvatarName))
                {
                    Player.Name = changeName.AvatarName.Trim();
                    Player.NameSet = true;
                    _playerManager.SavePlayer(Player);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[Session #{SessionId}] Player #{Player.LowId} changed name to: '{Player.Name}'");
                    Console.ResetColor();

                    // Send AvailableServerCommandMessage 201 (LogicChangeAvatarNameCommand)
                    var cmdMsg = new AvailableServerCommandMessage(201, stream =>
                    {
                        stream.WriteString(Player.Name);
                        stream.WriteVInt(0);
                        // LogicServerCommand header
                        stream.WriteVInt(0);
                        stream.WriteVInt(0);
                        stream.WriteVLong(new LogicLong(0, 0));
                    });
                    await SendMessageAsync(cmdMsg);
                    BridgeServer.Instance?.BroadcastCharacterState(Player, "rename", online: true);
                }
                break;

            case EndClientTurnMessage turn:
                if (Player != null && turn.Commands.Count > 0)
                {
                    turn.Execute(Player);
                    _playerManager.SavePlayer(Player);
                }
                break;

            case ClientInputMessage clientInput:
                if (Player != null && clientInput.Inputs.Count > 0)
                {
                    clientInput.Execute(Player);
                }
                break;

            case GoHomeFromOfflinePractiseMessage:
            case GoHomeMessage:
            case ExitTrainingGroundMessage:
            case AskForBattleEndMessage:
                if (Player != null)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[Session #{SessionId}] Player #{Player.LowId} returning home from battle/practice cave (resetting session for 87% -> 100% transition).");
                    Console.ResetColor();
                    BattleManager.Instance.EndMatch(Player);
                    Disconnect();
                }
                break;

            case CancelMatchmakingMessage:
                if (Player != null)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[Session #{SessionId}] Player #{Player.LowId} cancelled matchmaking / exited battle screen.");
                    Console.ResetColor();
                    BattleManager.Instance.EndMatch(Player);
                    await SendMessageAsync(new MatchMakingCancelledMessage());
                    await SendMessageAsync(new OwnHomeDataMessage(Player));
                }
                break;

            case MatchmakeMessage:
                if (Player != null)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[Session #{SessionId}] Player #{Player.LowId} requested matchmaking (Solo Showdown / Battle).");
                    Console.ResetColor();
                    var brawlerData = Player.GetOrCreateBrawler(Player.SelectedBrawler);
                    BattleManager.Instance.StartMatch(Player, Player.SelectedBrawler, brawlerData.SelectedSkin, 13);
                }
                break;

            case SinglePlayerMatchRequestMessage singlePlayer:
                if (Player != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Session #{SessionId}] Single-player match request received (Brawler: {singlePlayer.BrawlerId}, Skin: {singlePlayer.SkinId}, Location: {singlePlayer.LocationId}).");
                    Console.ResetColor();

                    if (singlePlayer.BrawlerClass == 16 && singlePlayer.BrawlerId >= 0)
                    {
                        Player.SelectedBrawler = singlePlayer.BrawlerId;
                        _playerManager.SavePlayer(Player);
                    }

                    BattleManager.Instance.StartMatch(Player, Player.SelectedBrawler, singlePlayer.SkinId, singlePlayer.LocationId);

                    // In BSL with 'offlineBattles': true, the client hooks battle initialization and runs it locally.
                    // Staying silent on the game socket allows the client transition into the Training Cave to complete smoothly without crashing.
                }
                break;
        }
    }

    public async Task SendMessageAsync(PiranhaMessage message)
    {
        if (_isDisposed || !_socket.Connected) return;

        try
        {
            if (message.GetEncodingLength() == 0)
            {
                message.Encode();
            }

            var payload = message.GetMessageBytes();

            // Encrypt payload if pepper is enabled
            switch (_pepperState)
            {
                case 4:
                    payload = SendPepperLoginResponse(payload);
                    break;

                case 5:
                    if (_encryptStream != null)
                    {
                        var overhead = _encryptStream.GetEncryptionOverhead();
                        var encrypted = new byte[payload.Length + overhead];
                        _encryptStream.Encrypt(payload, encrypted, payload.Length);
                        payload = encrypted;
                    }
                    break;
            }

            var framed = PacketFramer.CreateFramedPacket(message.GetMessageType(), message.MessageVersion, payload);

            await _sendLock.WaitAsync();
            try
            {
                await _socket.SendAsync(framed.AsMemory(), SocketFlags.None);
                if (ServerConfig.Instance.Server.DebugLogging)
                    Console.WriteLine($"[Session #{SessionId}] --> Sent packet ID {message.GetMessageType()} ({payload.Length} bytes)");
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception ex)
        {
            if (ServerConfig.Instance.Server.DebugLogging)
                Console.WriteLine($"[Session #{SessionId}] Send error: {ex.Message}");
        }
    }

    private byte[]? HandlePepperLogin(byte[] payload)
    {
        try
        {
            _clientPk = payload[..32];
            var hasher = new Blake2BHasher();
            hasher.Update(_clientPk);
            hasher.Update(PepperKey.ServerPublicKey);

            var nonce = hasher.Finish();
            var encryptedBox = payload[32..];

            var decrypted = TweetNaCl.CryptoBoxOpen(encryptedBox, nonce, PepperKey.ServerPublicKey, PepperKey.ClientSecretKey);
            if (decrypted == null || decrypted.Length < 25) return null;

            _rNonce = decrypted[1..25];
            return decrypted[25..];
        }
        catch
        {
            return null;
        }
    }

    private byte[] SendPepperLoginResponse(byte[] payload)
    {
        var packet = new byte[_sNonce.Length + _secretKey.Length + payload.Length];
        Buffer.BlockCopy(_sNonce, 0, packet, 0, _sNonce.Length);
        Buffer.BlockCopy(_secretKey, 0, packet, _sNonce.Length, _secretKey.Length);
        Buffer.BlockCopy(payload, 0, packet, _sNonce.Length + _secretKey.Length, payload.Length);

        var hasher = new Blake2BHasher();
        hasher.Update(_rNonce);
        hasher.Update(_clientPk);
        hasher.Update(PepperKey.ServerPublicKey);

        var nonce = hasher.Finish();

        _decryptStream = new PepperEncrypter(_secretKey, _rNonce);
        _encryptStream = new PepperEncrypter(_secretKey, _sNonce);

        _pepperState = 5;

        return TweetNaCl.CryptoBox(packet, nonce, PepperKey.ServerPublicKey, PepperKey.ClientSecretKey);
    }

    public void Disconnect()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (Player != null)
        {
            BattleManager.Instance.EndMatch(Player);
            _playerManager.SavePlayer(Player);
            BridgeServer.Instance?.BroadcastCharacterState(Player, "disconnect", online: false, inBattleOverride: false);
            Console.WriteLine($"[Session #{SessionId}] Saved player #{Player.LowId} state on disconnect.");
        }

        try { _cts.Cancel(); } catch { }
        try { _socket.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket.Close(); } catch { }
        _socket.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();

        _server.RemoveSession(SessionId);
        Console.WriteLine($"[Session #{SessionId}] Client disconnected.");
    }
}
