using System;
using System.Collections.Generic;
using System.Text;

namespace CustomBrawlServer.Protocol;

public class ByteStream
{
    private int _bitIdx;
    private byte[] _buffer;
    private int _length;
    private int _offset;

    public ByteStream(int capacity = 1024)
    {
        _buffer = new byte[capacity];
    }

    public ByteStream(byte[] buffer)
    {
        _length = buffer.Length;
        _buffer = new byte[_length];
        Array.Copy(buffer, _buffer, _length);
    }

    public ByteStream(byte[] buffer, int length)
    {
        _length = Math.Min(length, buffer.Length);
        _buffer = new byte[_length];
        Array.Copy(buffer, _buffer, _length);
    }

    public byte[] GetByteArray() => _buffer;
    public int GetLength() => _offset < _length ? _length : _offset;
    public int GetOffset() => _offset;
    public void SetOffset(int offset) { _offset = Math.Min(offset, _length); _bitIdx = 0; }
    public void ResetOffset() { _offset = 0; _bitIdx = 0; }
    public bool IsAtEnd() => _offset >= _length;

    public void SetByteArray(byte[] buffer, int length)
    {
        _offset = 0;
        _bitIdx = 0;
        _length = Math.Min(length, buffer.Length);
        _buffer = new byte[_length];
        Array.Copy(buffer, _buffer, _length);
    }

    public void EnsureCapacity(int capacity)
    {
        if (_offset + capacity <= _buffer.Length) return;
        var newSize = Math.Max(_buffer.Length * 2, _offset + capacity);
        Array.Resize(ref _buffer, newSize);
    }

    // ----------------- Boolean -----------------
    public bool ReadBoolean()
    {
        if (_bitIdx == 0) ++_offset;
        if (_offset - 1 < 0 || _offset - 1 >= _length)
        {
            _bitIdx = (_bitIdx + 1) & 7;
            return false;
        }

        var value = (_buffer[_offset - 1] & (1 << _bitIdx)) != 0;
        _bitIdx = (_bitIdx + 1) & 7;
        return value;
    }

    public bool WriteBoolean(bool value)
    {
        if (_bitIdx == 0)
        {
            EnsureCapacity(1);
            ++_offset;
        }

        if (value)
            _buffer[_offset - 1] |= (byte)(1 << _bitIdx);

        _bitIdx = (_bitIdx + 1) & 7;
        return value;
    }

    // ----------------- Byte -----------------
    public byte ReadByte()
    {
        _bitIdx = 0;
        if (_offset >= _length) return 0;
        return _buffer[_offset++];
    }

    public byte WriteByte(byte value)
    {
        EnsureCapacity(1);
        _bitIdx = 0;
        _buffer[_offset++] = value;
        return value;
    }

    // ----------------- Short (16-bit) -----------------
    public short ReadShort()
    {
        _bitIdx = 0;
        if (_offset + 2 > _length)
        {
            _offset = _length;
            return 0;
        }
        return (short)((_buffer[_offset++] << 8) | _buffer[_offset++]);
    }

    public void WriteShort(short value)
    {
        EnsureCapacity(2);
        _bitIdx = 0;
        _buffer[_offset++] = (byte)(value >> 8);
        _buffer[_offset++] = (byte)value;
    }

    // ----------------- Int24 (3-byte) -----------------
    public int ReadInt24()
    {
        _bitIdx = 0;
        if (_offset + 3 > _length)
        {
            _offset = _length;
            return 0;
        }
        return (_buffer[_offset++] << 16) | (_buffer[_offset++] << 8) | _buffer[_offset++];
    }

    public void WriteInt24(int value)
    {
        EnsureCapacity(3);
        _bitIdx = 0;
        _buffer[_offset++] = (byte)(value >> 16);
        _buffer[_offset++] = (byte)(value >> 8);
        _buffer[_offset++] = (byte)value;
    }

    // ----------------- Int32 -----------------
    public int ReadInt()
    {
        _bitIdx = 0;
        if (_offset + 4 > _length)
        {
            _offset = _length;
            return 0;
        }
        return (_buffer[_offset++] << 24) |
               (_buffer[_offset++] << 16) |
               (_buffer[_offset++] << 8) |
               _buffer[_offset++];
    }

    public void WriteInt(int value)
    {
        EnsureCapacity(4);
        _bitIdx = 0;
        _buffer[_offset++] = (byte)(value >> 24);
        _buffer[_offset++] = (byte)(value >> 16);
        _buffer[_offset++] = (byte)(value >> 8);
        _buffer[_offset++] = (byte)value;
    }

    // ----------------- VInt (Supercell Variable Length Int) -----------------
    public int ReadVInt()
    {
        _bitIdx = 0;
        if (_offset >= _length) return 0;

        var value = 0;
        var byteValue = _buffer[_offset++];

        if ((byteValue & 0x40) != 0)
        {
            value |= byteValue & 0x3F;
            if ((byteValue & 0x80) == 0) return (int)(value | 0xFFFFFFC0);

            if (_offset >= _length) return (int)(value | 0xFFFFFFC0);
            value |= ((byteValue = _buffer[_offset++]) & 0x7F) << 6;
            if ((byteValue & 0x80) == 0) return (int)(value | 0xFFFFE000);

            if (_offset >= _length) return (int)(value | 0xFFFFE000);
            value |= ((byteValue = _buffer[_offset++]) & 0x7F) << 13;
            if ((byteValue & 0x80) == 0) return (int)(value | 0xFFF00000);

            if (_offset >= _length) return (int)(value | 0xFFF00000);
            value |= ((byteValue = _buffer[_offset++]) & 0x7F) << 20;
            if ((byteValue & 0x80) == 0) return (int)(value | 0xF8000000);

            if (_offset >= _length) return (int)(value | 0xF8000000);
            value |= (_buffer[_offset++] & 0x7F) << 27;
            return (int)(value | 0x80000000);
        }

        value |= byteValue & 0x3F;
        if ((byteValue & 0x80) == 0) return value;

        if (_offset >= _length) return value;
        value |= ((byteValue = _buffer[_offset++]) & 0x7F) << 6;
        if ((byteValue & 0x80) == 0) return value;

        if (_offset >= _length) return value;
        value |= ((byteValue = _buffer[_offset++]) & 0x7F) << 13;
        if ((byteValue & 0x80) == 0) return value;

        if (_offset >= _length) return value;
        value |= ((byteValue = _buffer[_offset++]) & 0x7F) << 20;
        if ((byteValue & 0x80) != 0)
        {
            if (_offset < _length)
                value |= (_buffer[_offset++] & 0x7F) << 27;
        }

        return value;
    }

    public int WriteVInt(int value)
    {
        EnsureCapacity(5);
        _bitIdx = 0;

        switch (value)
        {
            case >= 0 and >= 64:
            {
                if (value >= 0x2000)
                {
                    if (value >= 0x100000)
                    {
                        if (value >= 0x8000000)
                        {
                            _buffer[_offset++] = (byte)((value & 0x3F) | 0x80);
                            _buffer[_offset++] = (byte)(((value >> 6) & 0x7F) | 0x80);
                            _buffer[_offset++] = (byte)(((value >> 13) & 0x7F) | 0x80);
                            _buffer[_offset++] = (byte)(((value >> 20) & 0x7F) | 0x80);
                            _buffer[_offset++] = (byte)((value >> 27) & 0xF);
                        }
                        else
                        {
                            _buffer[_offset++] = (byte)((value & 0x3F) | 0x80);
                            _buffer[_offset++] = (byte)(((value >> 6) & 0x7F) | 0x80);
                            _buffer[_offset++] = (byte)(((value >> 13) & 0x7F) | 0x80);
                            _buffer[_offset++] = (byte)((value >> 20) & 0x7F);
                        }
                    }
                    else
                    {
                        _buffer[_offset++] = (byte)((value & 0x3F) | 0x80);
                        _buffer[_offset++] = (byte)(((value >> 6) & 0x7F) | 0x80);
                        _buffer[_offset++] = (byte)((value >> 13) & 0x7F);
                    }
                }
                else
                {
                    _buffer[_offset++] = (byte)((value & 0x3F) | 0x80);
                    _buffer[_offset++] = (byte)(((value >> 6) & 0x7F));
                }
                break;
            }
            case >= 0:
                _buffer[_offset++] = (byte)(value & 0x3F);
                break;
            case <= -0x40 and <= -0x2000:
            {
                if (value <= -0x100000)
                {
                    if (value <= -0x8000000)
                    {
                        _buffer[_offset++] = (byte)((value & 0x3F) | 0xC0);
                        _buffer[_offset++] = (byte)(((value >> 6) & 0x7F) | 0x80);
                        _buffer[_offset++] = (byte)(((value >> 13) & 0x7F) | 0x80);
                        _buffer[_offset++] = (byte)(((value >> 20) & 0x7F) | 0x80);
                        _buffer[_offset++] = (byte)((value >> 27) & 0xF);
                    }
                    else
                    {
                        _buffer[_offset++] = (byte)((value & 0x3F) | 0xC0);
                        _buffer[_offset++] = (byte)(((value >> 6) & 0x7F) | 0x80);
                        _buffer[_offset++] = (byte)(((value >> 13) & 0x7F) | 0x80);
                        _buffer[_offset++] = (byte)((value >> 20) & 0x7F);
                    }
                }
                else
                {
                    _buffer[_offset++] = (byte)((value & 0x3F) | 0xC0);
                    _buffer[_offset++] = (byte)(((value >> 6) & 0x7F) | 0x80);
                    _buffer[_offset++] = (byte)((value >> 13) & 0x7F);
                }
                break;
            }
            case <= -0x40:
                _buffer[_offset++] = (byte)((value & 0x3F) | 0xC0);
                _buffer[_offset++] = (byte)(((value >> 6) & 0x7F));
                break;
            default:
                _buffer[_offset++] = (byte)((value & 0x3F) | 0x40);
                break;
        }

        return value;
    }

    // ----------------- String -----------------
    public string? ReadString(int maxCapacity = 2048)
    {
        var length = ReadBytesLength();
        if (length < 0) return null;
        if (length > maxCapacity) return null;
        if (_offset + length > _length)
        {
            _offset = _length;
            return null;
        }

        var value = Encoding.UTF8.GetString(_buffer, _offset, length);
        _offset += length;
        return value;
    }

    public string ReadStringReference(int maxCapacity = 2048)
    {
        var length = ReadBytesLength();
        if (length <= 0) return string.Empty;
        if (length > maxCapacity) return string.Empty;
        if (_offset + length > _length)
        {
            _offset = _length;
            return string.Empty;
        }

        var value = Encoding.UTF8.GetString(_buffer, _offset, length);
        _offset += length;
        return value;
    }

    public void WriteString(string? value)
    {
        if (value == null)
        {
            WriteInt(-1);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            EnsureCapacity(bytes.Length + 4);
            WriteInt(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _buffer, _offset, bytes.Length);
            _offset += bytes.Length;
        }
    }

    public void WriteStringReference(string? value)
    {
        WriteString(value ?? string.Empty);
    }

    // ----------------- Raw Bytes -----------------
    public int ReadBytesLength()
    {
        _bitIdx = 0;
        if (_offset + 4 > _length)
        {
            _offset = _length;
            return -1;
        }
        return (_buffer[_offset++] << 24) |
               (_buffer[_offset++] << 16) |
               (_buffer[_offset++] << 8) |
               _buffer[_offset++];
    }

    public byte[]? ReadBytes(int length, int maxCapacity = 1048576)
    {
        _bitIdx = 0;
        if (length < 0) return null;
        if (length > maxCapacity) return null;
        if (_offset + length > _length)
        {
            _offset = _length;
            return null;
        }

        var array = new byte[length];
        Array.Copy(_buffer, _offset, array, 0, length);
        _offset += length;
        return array;
    }

    public void WriteBytes(byte[]? value, int length)
    {
        if (value == null)
        {
            WriteInt(-1);
        }
        else
        {
            EnsureCapacity(length + 4);
            WriteInt(length);
            Array.Copy(value, 0, _buffer, _offset, length);
            _offset += length;
        }
    }

    public void WriteBytesWithoutLength(byte[]? value, int length)
    {
        if (value == null || length <= 0) return;
        EnsureCapacity(length);
        Array.Copy(value, 0, _buffer, _offset, length);
        _offset += length;
    }

    // ----------------- VLong & LogicLong Helpers -----------------
    public void WriteVLong(LogicLong value)
    {
        WriteVInt(value.GetHigherInt());
        WriteVInt(value.GetLowerInt());
    }

    public LogicLong ReadVLong()
    {
        var high = ReadVInt();
        var low = ReadVInt();
        return new LogicLong(high, low);
    }

    // ----------------- DataReference Helpers -----------------
    public void WriteDataReference(int classId, int instanceId = 0)
    {
        if (classId > 0)
        {
            WriteVInt(classId);
            WriteVInt(instanceId);
        }
        else
        {
            WriteVInt(0);
        }
    }

    public (int ClassId, int InstanceId) ReadDataReference()
    {
        var classId = ReadVInt();
        if (classId <= 0) return (0, 0);
        var instanceId = ReadVInt();
        return (classId, instanceId);
    }

    // ----------------- IntList Helpers -----------------
    public void WriteIntList(IList<int> list)
    {
        WriteVInt(list.Count);
        foreach (var item in list)
            WriteVInt(item);
    }

    public List<int> ReadIntList()
    {
        var count = ReadVInt();
        var list = new List<int>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            if (IsAtEnd()) break;
            list.Add(ReadVInt());
        }
        return list;
    }
}
