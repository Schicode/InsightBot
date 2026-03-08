using InsightBot.Core.Game;
using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;

namespace InsightBot.Core.Network.Handlers;

// ── Chat ──────────────────────────────────────────────────────────────────────

public sealed class ChatHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_CHAT;

    protected override void Process(PacketReader r, Packet raw)
    {
        byte channel = r.ReadByte();
        string sender = r.ReadAsciiString();
        string content = r.HasData ? r.ReadUnicodeString() : string.Empty;

        var msg = new ChatMessage
        {
            Channel = (ChatChannel)channel,
            Sender = sender,
            Content = content
        };
        GameContext.Instance.RaiseChatReceived(msg);

        string tag = msg.Channel switch
        {
            ChatChannel.All => "[ALL]",
            ChatChannel.Private => "[PM]",
            ChatChannel.Party => "[PARTY]",
            ChatChannel.Guild => "[GUILD]",
            ChatChannel.Global => "[GLOBAL]",
            ChatChannel.Notice => "[NOTICE]",
            _ => $"[CH{channel}]"
        };
        Console.WriteLine($"{tag} {sender}: {content}");
    }
}

// ── 0xA102 — Gateway login response ──────────────────────────────────────────

/// <summary>
/// Handles S_LOGIN_RESPONSE (0xA102).
///
/// Layout:
///   byte    result   1=ok  2=bad credentials  3=already online  …
/// </summary>
public sealed class LoginResponseHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_LOGIN_RESPONSE;

    protected override void Process(PacketReader r, Packet raw)
    {
        byte result = r.ReadByte();

        if (result == 1)
        {
            Console.WriteLine("[Login] Primary login successful.");
            // Secondary PIN is requested by the server via 0xA120 if the account
            // has PassKey enabled. The bot responds automatically in PassKeyRequestHandler.
        }
        else
        {
            string reason = result switch
            {
                2 => "Invalid credentials",
                3 => "Account already online",
                4 => "Server maintenance",
                5 => "IP blocked",
                8 => "Too many connections",
                9 => "Blocked account",
                _ => $"Unknown error (code {result})"
            };
            Console.WriteLine($"[Login] Failed: {reason}");
            GameContext.Instance.RaiseLoginFailed(reason);
        }
    }
}

// ── 0xA120 — PassKey / secondary PIN request ──────────────────────────────────

/// <summary>
/// Handles S_PASSKEY_REQUEST (0xA120).
/// The server sends this after primary login to ask for the secondary security PIN.
///
/// The bot replies with C_PASSKEY_RESPONSE (0x6117):
///   byte    mode    (mirror of request mode)
///   uint16  length
///   bytes   encrypted PIN (Blowfish ECB, key 0x0F,0x07,0x3D,0x20,0x56,0x62,0xC9,0xEB)
///
/// Sending is done via GameContext.SendPassKeyResponse which holds the
/// SroConnection injected by BotService at startup.
/// </summary>
public sealed class PassKeyRequestHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_PASSKEY_REQUEST;

    // Fixed 8-byte Blowfish key used to encrypt the PIN
    private static readonly byte[] PinEncryptKey =
    {
        0x0F, 0x07, 0x3D, 0x20, 0x56, 0x62, 0xC9, 0xEB
    };

    protected override void Process(PacketReader r, Packet raw)
    {
        byte mode = r.ReadByte();
        Console.WriteLine($"[PassKey] Server requests secondary PIN (mode={mode}).");

        string? pin = GameContext.Instance.GetSecondaryPin();
        if (string.IsNullOrEmpty(pin))
        {
            Console.WriteLine("[PassKey] WARNING: No secondary PIN configured. Login may fail.");
            return;
        }

        // Encrypt the PIN using Blowfish ECB with the fixed 8-byte key
        byte[] pinBytes = System.Text.Encoding.ASCII.GetBytes(pin);
        byte[] padded = PadTo8(pinBytes);

        var bf = new Security.Blowfish();
        bf.Initialize(PinEncryptKey);
        byte[] encrypted = bf.Encode(padded);

        // Build response packet 0x6117
        var w = new PacketWriter();
        w.WriteByte(mode);
        w.WriteUInt16((ushort)encrypted.Length);
        w.WriteBytes(encrypted);
        Packet response = w.Build(Opcodes.C_PASSKEY_RESPONSE);

        // Send via the injected sender delegate
        GameContext.Instance.SendPacket?.Invoke(response);
        Console.WriteLine("[PassKey] Secondary PIN sent.");
    }

    private static byte[] PadTo8(byte[] data)
    {
        int len = (data.Length + 7) & ~7;
        if (len == data.Length) return data;
        byte[] padded = new byte[len];
        Array.Copy(data, padded, data.Length);
        return padded;
    }
}

// ── 0xA121 — PassKey response result ─────────────────────────────────────────

public sealed class PassKeyResultHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_PASSKEY_RESULT;

    protected override void Process(PacketReader r, Packet raw)
    {
        byte result = r.ReadByte();

        switch (result)
        {
            case 1:
                Console.WriteLine("[PassKey] Secondary PIN accepted. Login complete.");
                GameContext.Instance.RaisePassKeyAccepted();
                break;
            case 2:
                Console.WriteLine("[PassKey] Wrong PIN.");
                GameContext.Instance.RaiseLoginFailed("Wrong secondary PIN.");
                break;
            case 3:
                Console.WriteLine("[PassKey] Account blocked — too many wrong PINs.");
                GameContext.Instance.RaiseLoginFailed("Account blocked (too many wrong PINs).");
                break;
            default:
                Console.WriteLine($"[PassKey] Unknown result: {result}");
                break;
        }
    }
}

// ── 0xA103 — Agent handoff ────────────────────────────────────────────────────

public sealed class AgentAuthHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_AGENT_AUTH;

    protected override void Process(PacketReader r, Packet raw)
    {
        byte result = r.ReadByte();
        if (result != 1)
        {
            Console.WriteLine($"[AgentAuth] Failed, code={result}");
            return;
        }

        if (r.Remaining >= 10)
        {
            uint token = r.ReadUInt32();
            string ip = r.ReadAsciiString();
            ushort port = r.ReadUInt16();
            Console.WriteLine($"[AgentAuth] Handoff → {ip}:{port} token=0x{token:X8}");
            GameContext.Instance.RaiseAgentHandoff(ip, port, token);
        }
        else
        {
            Console.WriteLine("[AgentAuth] Agent auth successful.");
        }
    }
}

// ── 0xB034 — Inventory snapshot ───────────────────────────────────────────────

public sealed class InventoryHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_INVENTORY_DATA;

    protected override void Process(PacketReader r, Packet raw)
    {
        byte slotCount = r.ReadByte();
        int items = 0;
        for (int i = 0; i < slotCount && r.HasData; i++)
        {
            r.ReadByte(); // slot index
            if (!r.HasData) break;
            bool hasItem = r.ReadBool();
            if (!hasItem) continue;
            r.ReadUInt32(); // item refId
            items++;
        }
        Console.WriteLine($"[Inventory] {items} items in {slotCount} slots.");
    }
}