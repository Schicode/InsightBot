using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace InsightBot.Core.Network;

/// <summary>
/// Fluent builder for constructing Silkroad packet payloads.
/// All integers are written little-endian.
/// </summary>
public sealed class PacketWriter
{
    private readonly List<byte> _buffer = new();

    // ── Primitives ─────────────────────────────────────────────────────────

    public PacketWriter WriteByte(byte value)
    {
        _buffer.Add(value);
        return this;
    }

    public PacketWriter WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public PacketWriter WriteInt16(short value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(tmp, value);
        _buffer.AddRange(tmp.ToArray());
        return this;
    }

    public PacketWriter WriteUInt16(ushort value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
        _buffer.AddRange(tmp.ToArray());
        return this;
    }

    public PacketWriter WriteInt32(int value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(tmp, value);
        _buffer.AddRange(tmp.ToArray());
        return this;
    }

    public PacketWriter WriteUInt32(uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
        _buffer.AddRange(tmp.ToArray());
        return this;
    }

    public PacketWriter WriteInt64(long value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(tmp, value);
        _buffer.AddRange(tmp.ToArray());
        return this;
    }

    public PacketWriter WriteUInt64(ulong value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
        _buffer.AddRange(tmp.ToArray());
        return this;
    }

    public PacketWriter WriteSingle(float value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(tmp, value);
        _buffer.AddRange(tmp.ToArray());
        return this;
    }

    // ── Strings ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a length-prefixed UTF-16LE string (2-byte char count prefix).
    /// </summary>
    public PacketWriter WriteUnicodeString(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        WriteUInt16((ushort)(bytes.Length / 2));
        _buffer.AddRange(bytes);
        return this;
    }

    /// <summary>
    /// Writes a length-prefixed ASCII string (2-byte byte count prefix).
    /// </summary>
    public PacketWriter WriteAsciiString(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteUInt16((ushort)bytes.Length);
        _buffer.AddRange(bytes);
        return this;
    }

    // ── Raw bytes ──────────────────────────────────────────────────────────

    public PacketWriter WriteBytes(byte[] data)
    {
        _buffer.AddRange(data);
        return this;
    }

    public PacketWriter WriteBytes(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
        return this;
    }

    // ── SRO-specific helpers ───────────────────────────────────────────────

    /// <summary>
    /// Writes a 3D world position in SRO's packed format.
    /// </summary>
    public PacketWriter WritePosition(float x, float y, float z, ushort region)
    {
        WriteInt16((short)(x * 10));
        WriteInt16((short)(z * 10));
        WriteInt16((short)(y * 10));
        WriteUInt16(region);
        return this;
    }

    // ── Build ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the final Packet with the given opcode.
    /// </summary>
    public Packet Build(ushort opcode) => new(opcode, _buffer.ToArray());

    public byte[] ToArray() => _buffer.ToArray();
    public int Length => _buffer.Count;
}
