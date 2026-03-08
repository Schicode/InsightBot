using InsightBot.Core.Network;
using InsightBot.Core.Network.Security;
using System;
using System.Linq;
using System.Security.Cryptography;

namespace InsightBot.Core.Network.Security;

/// <summary>
/// Implements the Silkroad Online security layer:
/// - Blowfish encryption (key exchange via Diffie-Hellman-like handshake)
/// - Packet counting (rolling byte counter, used for CRC)
/// - Handshake state machine (opcode 0x5000 / 0x9000)
///
/// Based on the reverse-engineered "Security API" by PushEDX.
/// </summary>
public sealed class SecurityManager
{
    // ── State ───────────────────────────────────────────────────────────────

    private Blowfish _blowfish = new();
    private bool _encryptionActive;
    private bool _handshakeDone;

    private ulong _clientKey;
    private ulong _serverKey;
    private ulong _generatorA;
    private ulong _generatorB;
    private ulong _generatorP;
    private ulong _generatorX;

    private byte _clientCount;
    private byte _serverCount;
    private byte _clientCRC;
    private byte _serverCRC;

    public bool HandshakeDone => _handshakeDone;
    public bool EncryptionActive => _encryptionActive;

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Process an incoming 0x5000 (handshake) packet from the server.
    /// Returns the 0x9000 response packet the client must send back, or null
    /// when no response is needed (final step).
    /// </summary>
    public Packet? HandleHandshake(Packet serverHandshake)
    {
        var reader = new PacketReader(serverHandshake);

        byte securityBytes = reader.ReadByte();  // flag byte
        byte encryptionActive = reader.ReadByte();

        if (securityBytes == 0 && encryptionActive == 0)
        {
            // No security — dummy handshake
            _handshakeDone = true;
            return null;
        }

        ulong a = reader.ReadUInt64();
        ulong b = reader.ReadUInt64();
        ulong p = reader.ReadUInt64();

        _generatorA = a;
        _generatorB = b;
        _generatorP = p;

        // Compute X = b^a mod p  (server public key)
        _generatorX = PowMod(b, a, p);

        // Derive Blowfish key from the shared secret
        ulong sharedSecret = PowMod(_generatorX, _generatorA, _generatorP);
        byte[] blowfishKey = BuildBlowfishKey(sharedSecret);
        _blowfish.Initialize(blowfishKey);

        // Build the 0x9000 response
        var writer = new PacketWriter();
        writer.WriteUInt64(_generatorX);

        if (encryptionActive != 0)
        {
            _encryptionActive = true;
            // Encrypt the key we're sending back
            byte[] encrypted = _blowfish.Encode(BuildBlowfishKey(a));
            writer.WriteBytes(encrypted);
        }

        _handshakeDone = true;
        return writer.Build(Opcodes.HANDSHAKE_RESPONSE);
    }

    /// <summary>Encrypt data using the negotiated Blowfish key.</summary>
    public byte[] Encrypt(byte[] data) => _blowfish.Encode(data);

    /// <summary>Decrypt data using the negotiated Blowfish key.</summary>
    public byte[] Decrypt(byte[] data) => _blowfish.Decode(data);

    /// <summary>
    /// Encapsulate a packet: apply encryption and increment the rolling counter.
    /// </summary>
    public byte[] EncodePacket(Packet packet)
    {
        var raw = packet.Serialize();
        if (_encryptionActive)
        {
            // Encrypt only the payload portion
            int payloadStart = Packet.HeaderSize;
            if (raw.Length > payloadStart)
            {
                byte[] payload = raw[payloadStart..];
                byte[] enc = _blowfish.Encode(PadToBlock(payload));
                raw = raw[..payloadStart].Concat(enc).ToArray();
                raw[5] |= 0x01; // Mark as encrypted
            }
        }
        raw[4] = _clientCount++;
        return raw;
    }

    /// <summary>
    /// Decode a raw incoming byte buffer: strip encryption, return Packet.
    /// Uses byte[]+offset to remain callable from async methods in C# 12.
    /// </summary>
    public bool TryDecodePacket(byte[] raw, int offset, int length, out Packet? packet, out int consumed)
    {
        if (!Packet.TryParse(raw, offset, length, out packet, out consumed))
            return false;

        if (packet!.IsEncrypted && _encryptionActive)
        {
            byte[] decrypted = _blowfish.Decode(packet.Data.ToArray());
            int actualLen = raw[offset] | (raw[offset + 1] << 8); // original data length from header
            int trimLen = Math.Min(actualLen, decrypted.Length);
            packet = new Packet(packet.Opcode, decrypted[..trimLen]);
        }

        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ulong PowMod(ulong b, ulong e, ulong mod)
    {
        ulong result = 1;
        b %= mod;
        while (e > 0)
        {
            if ((e & 1) == 1) result = result * b % mod;
            e >>= 1;
            b = b * b % mod;
        }
        return result;
    }

    private static byte[] BuildBlowfishKey(ulong value)
    {
        // SRO-specific key derivation from the DH shared secret
        byte[] key = new byte[8];
        for (int i = 0; i < 8; i++)
            key[i] = (byte)((value >> (i * 8)) & 0xFF);

        // XOR transform as defined by the protocol
        uint a = ((uint)key[0] | ((uint)key[1] << 8) | ((uint)key[2] << 16) | ((uint)key[3] << 24));
        uint b = ((uint)key[4] | ((uint)key[5] << 8) | ((uint)key[6] << 16) | ((uint)key[7] << 24));
        a ^= b;
        b ^= a;
        a ^= b;

        key[0] = (byte)(a & 0xFF);
        key[1] = (byte)((a >> 8) & 0xFF);
        key[2] = (byte)((a >> 16) & 0xFF);
        key[3] = (byte)((a >> 24) & 0xFF);
        key[4] = (byte)(b & 0xFF);
        key[5] = (byte)((b >> 8) & 0xFF);
        key[6] = (byte)((b >> 16) & 0xFF);
        key[7] = (byte)((b >> 24) & 0xFF);
        return key;
    }

    private static byte[] PadToBlock(byte[] data)
    {
        int padded = (data.Length + 7) & ~7;
        if (padded == data.Length) return data;
        var result = new byte[padded];
        data.CopyTo(result, 0);
        return result;
    }
}