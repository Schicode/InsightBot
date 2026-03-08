using System;
using System.Buffers.Binary;
using System.Text;

namespace InsightBot.Core.Network;

/// <summary>
/// Sequential binary reader for Silkroad packet payloads.
/// All integers are little-endian as per the SRO protocol.
/// </summary>
public sealed class PacketReader
{
    private readonly ReadOnlyMemory<byte> _buffer;
    private int _position;

    public int Position => _position;
    public int Remaining => _buffer.Length - _position;
    public bool HasData => Remaining > 0;

    public PacketReader(Packet packet) : this(packet.Data.ToArray()) { }
    public PacketReader(byte[] data) => _buffer = data;
    public PacketReader(ReadOnlyMemory<byte> data) => _buffer = data;

    // ── Primitives ─────────────────────────────────────────────────────────

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _buffer.Span[_position++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public short ReadInt16()
    {
        EnsureAvailable(2);
        var v = BinaryPrimitives.ReadInt16LittleEndian(_buffer.Span[_position..]);
        _position += 2;
        return v;
    }

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        var v = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Span[_position..]);
        _position += 2;
        return v;
    }

    public int ReadInt32()
    {
        EnsureAvailable(4);
        var v = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Span[_position..]);
        _position += 4;
        return v;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Span[_position..]);
        _position += 4;
        return v;
    }

    public long ReadInt64()
    {
        EnsureAvailable(8);
        var v = BinaryPrimitives.ReadInt64LittleEndian(_buffer.Span[_position..]);
        _position += 8;
        return v;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(8);
        var v = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Span[_position..]);
        _position += 8;
        return v;
    }

    public float ReadSingle()
    {
        EnsureAvailable(4);
        var v = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Span[_position..]);
        _position += 4;
        return v;
    }

    // ── Strings ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a length-prefixed UTF-16LE string (2-byte length prefix = char count).
    /// Used by SRO for most in-game strings.
    /// </summary>
    public string ReadUnicodeString()
    {
        ushort charCount = ReadUInt16();
        if (charCount == 0) return string.Empty;
        EnsureAvailable(charCount * 2);
        var str = Encoding.Unicode.GetString(_buffer.Span.Slice(_position, charCount * 2));
        _position += charCount * 2;
        return str;
    }

    /// <summary>
    /// Reads a length-prefixed ASCII string (2-byte length prefix = byte count).
    /// Used by SRO for identifiers, usernames, etc.
    /// </summary>
    public string ReadAsciiString()
    {
        ushort byteCount = ReadUInt16();
        if (byteCount == 0) return string.Empty;
        EnsureAvailable(byteCount);
        var str = Encoding.ASCII.GetString(_buffer.Span.Slice(_position, byteCount));
        _position += byteCount;
        return str;
    }

    // ── Raw bytes ──────────────────────────────────────────────────────────

    public byte[] ReadBytes(int count)
    {
        EnsureAvailable(count);
        var v = _buffer.Span.Slice(_position, count).ToArray();
        _position += count;
        return v;
    }

    public void Skip(int count)
    {
        EnsureAvailable(count);
        _position += count;
    }

    // ── SRO-specific helpers ───────────────────────────────────────────────

    /// <summary>
    /// Reads a 3D position as two Int16 values packed as (X*10, Z*10, Y*10) + region.
    /// </summary>
    public (float X, float Y, float Z, ushort Region) ReadPosition()
    {
        short x = ReadInt16();
        short z = ReadInt16();
        short y = ReadInt16();
        ushort region = ReadUInt16();
        return (x / 10f, y / 10f, z / 10f, region);
    }

    /// <summary>
    /// Reads a UniqueID (4 bytes, used for entity identification in the world).
    /// </summary>
    public uint ReadUniqueId() => ReadUInt32();

    // ── Internal ───────────────────────────────────────────────────────────

    private void EnsureAvailable(int count)
    {
        if (Remaining < count)
            throw new InvalidOperationException(
                $"PacketReader underflow: need {count} bytes, only {Remaining} remaining at position {_position}.");
    }
}
