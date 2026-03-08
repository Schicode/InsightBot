using System;
using System.Buffers.Binary;
using System.Text;

namespace InsightBot.Core.Network;

/// <summary>
/// Represents a Silkroad Online network packet.
/// 
/// Wire format (little-endian):
/// [2 bytes] Data length (excludes header)
/// [2 bytes] Opcode
/// [1 byte]  Count (rolling counter, 0 if encryption disabled)
/// [1 byte]  Flag (0x00 = plain, 0x01 = encrypted, 0x02 = massive)
/// [N bytes] Payload
/// </summary>
public sealed class Packet
{
    public const int HeaderSize = 6;

    public ushort Opcode { get; }
    public byte Count { get; }
    public bool IsEncrypted { get; }
    public bool IsMassive { get; }
    private readonly byte[] _data;

    public ReadOnlySpan<byte> Data => _data;
    public int DataLength => _data.Length;

    public Packet(ushort opcode, byte[] data, byte count = 0, bool encrypted = false, bool massive = false)
    {
        Opcode = opcode;
        _data = data;
        Count = count;
        IsEncrypted = encrypted;
        IsMassive = massive;
    }

    public Packet(ushort opcode) : this(opcode, Array.Empty<byte>()) { }

    /// <summary>
    /// Deserialize a packet from raw bytes (must include the 6-byte header).
    /// Uses byte[] + offset to remain callable from async methods in C# 12.
    /// </summary>
    public static bool TryParse(byte[] raw, int offset, int length, out Packet? packet, out int consumed)
    {
        packet = null;
        consumed = 0;

        if (length < HeaderSize)
            return false;

        ushort dataLen = (ushort)(raw[offset] | (raw[offset + 1] << 8));
        ushort opcode = (ushort)(raw[offset + 2] | (raw[offset + 3] << 8));
        byte count = raw[offset + 4];
        byte flag = raw[offset + 5];

        int totalLen = HeaderSize + dataLen;
        if (length < totalLen)
            return false;

        bool encrypted = (flag & 0x01) != 0;
        bool massive = (flag & 0x02) != 0;

        byte[] data = new byte[dataLen];
        Buffer.BlockCopy(raw, offset + HeaderSize, data, 0, dataLen);

        packet = new Packet(opcode, data, count, encrypted, massive);
        consumed = totalLen;
        return true;
    }

    /// <summary>Convenience overload for a complete byte array.</summary>
    public static bool TryParse(byte[] raw, out Packet? packet, out int consumed)
        => TryParse(raw, 0, raw.Length, out packet, out consumed);

    /// <summary>
    /// Serialize the packet to bytes ready for sending on the wire.
    /// </summary>
    public byte[] Serialize()
    {
        var buf = new byte[HeaderSize + _data.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), (ushort)_data.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), Opcode);
        buf[4] = Count;
        buf[5] = (byte)((IsEncrypted ? 0x01 : 0x00) | (IsMassive ? 0x02 : 0x00));
        _data.CopyTo(buf.AsSpan(HeaderSize));
        return buf;
    }

    public override string ToString() =>
        $"[0x{Opcode:X4}] Len={_data.Length} Encrypted={IsEncrypted} Massive={IsMassive}";
}