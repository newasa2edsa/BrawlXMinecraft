using System;
using System.Collections.Generic;

namespace CustomBrawlServer.Network;

public readonly record struct RawPacket(int MessageType, int Version, byte[] Payload);

public class PacketFramer
{
    private byte[] _buffer = new byte[8192];
    private int _bufferedCount;

    public void Append(byte[] data, int offset, int count)
    {
        EnsureCapacity(_bufferedCount + count);
        Buffer.BlockCopy(data, offset, _buffer, _bufferedCount, count);
        _bufferedCount += count;
    }

    public List<RawPacket> ReadPackets()
    {
        var packets = new List<RawPacket>();

        while (_bufferedCount >= 7)
        {
            var messageType = (_buffer[0] << 8) | _buffer[1];
            var length = (_buffer[2] << 16) | (_buffer[3] << 8) | _buffer[4];
            var version = (_buffer[5] << 8) | _buffer[6];

            if (length < 0 || length > 10 * 1024 * 1024)
            {
                // Invalid length / corrupted stream
                _bufferedCount = 0;
                break;
            }

            var fullPacketSize = 7 + length;
            if (_bufferedCount < fullPacketSize)
            {
                // Incomplete packet; wait for more data
                break;
            }

            var payload = new byte[length];
            if (length > 0)
            {
                Buffer.BlockCopy(_buffer, 7, payload, 0, length);
            }

            packets.Add(new RawPacket(messageType, version, payload));

            // Shift remaining bytes
            var remaining = _bufferedCount - fullPacketSize;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_buffer, fullPacketSize, _buffer, 0, remaining);
            }
            _bufferedCount = remaining;
        }

        return packets;
    }

    public static byte[] CreateFramedPacket(int messageType, int version, byte[] payload)
    {
        var framed = new byte[7 + payload.Length];
        framed[0] = (byte)(messageType >> 8);
        framed[1] = (byte)messageType;

        framed[2] = (byte)(payload.Length >> 16);
        framed[3] = (byte)(payload.Length >> 8);
        framed[4] = (byte)payload.Length;

        framed[5] = (byte)(version >> 8);
        framed[6] = (byte)version;

        if (payload.Length > 0)
        {
            Buffer.BlockCopy(payload, 0, framed, 7, payload.Length);
        }

        return framed;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _buffer.Length) return;
        var newSize = Math.Max(_buffer.Length * 2, needed);
        var newBuffer = new byte[newSize];
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _bufferedCount);
        _buffer = newBuffer;
    }
}
