using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CustomBrawlServer.Config;

namespace CustomBrawlServer.Network;

public class UdpServer
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;

    public void Start()
    {
        try
        {
            var port = ServerConfig.Instance.Server.Port;
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            _cts = new CancellationTokenSource();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[UdpServer] Listening for battle traffic on UDP port {port}...");
            Console.ResetColor();

            _ = Task.Run(ReceiveLoopAsync);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UdpServer] Warning: Failed to bind UDP listener on port {ServerConfig.Instance.Server.Port}: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync()
    {
        while (_cts != null && !_cts.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(_cts.Token);
                var data = result.Buffer;
                var endpoint = result.RemoteEndPoint;

                Logic.BattleManager.Instance.HandleUdpPacket(data);

                if (ServerConfig.Instance.Server.DebugLogging)
                {
                    var previewLen = Math.Min(data.Length, 32);
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"[UdpServer] <-- Received {data.Length} bytes from {endpoint} (Hex: {Convert.ToHexString(data[..previewLen])})");
                    Console.ResetColor();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ServerConfig.Instance.Server.DebugLogging)
                    Console.WriteLine($"[UdpServer] Error receiving: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udpClient?.Close();
        _udpClient = null;
    }
}
