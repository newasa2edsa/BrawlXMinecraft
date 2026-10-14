using System;

namespace CustomBrawlServer.Protocol;

public class LogicLong
{
    private int _highInteger;
    private int _lowInteger;

    public LogicLong()
    {
    }

    public LogicLong(int highInteger, int lowInteger)
    {
        _highInteger = highInteger;
        _lowInteger = lowInteger;
    }

    public void Set(int highInteger, int lowInteger)
    {
        _highInteger = highInteger;
        _lowInteger = lowInteger;
    }

    public long ToLong()
    {
        return ((long)_highInteger << 32) | (uint)_lowInteger;
    }

    public int GetHigherInt() => _highInteger;
    public int GetLowerInt() => _lowInteger;

    public bool IsZero() => _highInteger == 0 && _lowInteger == 0;

    public LogicLong Decode(ByteStream byteStream)
    {
        _highInteger = byteStream.ReadInt();
        _lowInteger = byteStream.ReadInt();
        return this;
    }

    public LogicLong Encode(ByteStream byteStream)
    {
        byteStream.WriteInt(_highInteger);
        byteStream.WriteInt(_lowInteger);
        return this;
    }

    public LogicLong Clone() => new(_highInteger, _lowInteger);

    public override bool Equals(object? obj)
    {
        if (obj is LogicLong logicLong)
            return logicLong._highInteger == _highInteger && logicLong._lowInteger == _lowInteger;
        return false;
    }

    public static bool Equals(LogicLong? a1, LogicLong? a2)
    {
        if (a1 is null || a2 is null)
            return a1 is null && a2 is null;
        return a1._highInteger == a2._highInteger && a1._lowInteger == a2._lowInteger;
    }

    public static implicit operator LogicLong(long value)
    {
        return new LogicLong((int)(value >> 32), (int)value);
    }

    public static implicit operator long(LogicLong logicLong)
    {
        return ((long)logicLong._highInteger << 32) | (uint)logicLong._lowInteger;
    }

    public override int GetHashCode() => _lowInteger + 31 * _highInteger;

    public override string ToString() => $"LogicLong({_highInteger}, {_lowInteger})";
}
