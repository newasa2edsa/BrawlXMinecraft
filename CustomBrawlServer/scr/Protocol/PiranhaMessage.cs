using System;

namespace CustomBrawlServer.Protocol;

public abstract class PiranhaMessage
{
    public ByteStream ByteStream { get; set; } = new(1024);
    public int MessageVersion { get; set; }

    public abstract int GetMessageType();
    public virtual int GetServiceNodeType() => 1;

    public virtual void Encode()
    {
        ByteStream.ResetOffset();
    }

    public virtual void Decode()
    {
        ByteStream.ResetOffset();
    }

    public virtual bool IsServerToClientMessage()
    {
        return GetMessageType() >= 20000;
    }

    public int GetEncodingLength() => ByteStream.GetLength();

    public byte[] GetMessageBytes()
    {
        var len = GetEncodingLength();
        var buffer = ByteStream.GetByteArray();
        if (buffer.Length == len) return buffer;
        var result = new byte[len];
        Array.Copy(buffer, 0, result, 0, len);
        return result;
    }
}
