using System;
using System.Collections.Generic;
using System.IO;

namespace InsightBot.Core.Network;

/// <summary>
/// Logs packet traffic to the console and optionally to a file.
/// Useful during development to reverse-engineer unknown packets.
/// </summary>
public sealed class PacketLogger
{
    private readonly string _logPath;
    private readonly StreamWriter? _writer;
    private readonly HashSet<ushort> _ignoredOpcodes = new();
    private bool _logAll = true;

    public bool IsEnabled { get; set; } = true;

    public PacketLogger(string? logFile = null)
    {
        _logPath = logFile ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packets.log");
        if (logFile != null)
        {
            _writer = new StreamWriter(logFile, append: false) { AutoFlush = true };
        }
    }

    /// <summary>Ignore common "noise" opcodes (pings, movement, etc.).</summary>
    public PacketLogger IgnoreCommon()
    {
        Ignore(Opcodes.PING);
        Ignore(Opcodes.S_MOVEMENT);
        Ignore(Opcodes.C_MOVEMENT);
        return this;
    }

    public PacketLogger Ignore(ushort opcode)
    {
        _ignoredOpcodes.Add(opcode);
        return this;
    }

    public PacketLogger OnlyLog(params ushort[] opcodes)
    {
        _logAll = false;
        foreach (var op in opcodes)
            _ignoredOpcodes.Remove(op); // Re-allow these
        return this;
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    public void LogClientPacket(Packet packet) => Log("C→S", packet);
    public void LogServerPacket(Packet packet) => Log("S→C", packet);

    private void Log(string direction, Packet packet)
    {
        if (!IsEnabled) return;
        if (_ignoredOpcodes.Contains(packet.Opcode)) return;

        string hex  = Convert.ToHexString(packet.Data.ToArray());
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {direction} 0x{packet.Opcode:X4} ({packet.DataLength} bytes)";

        if (packet.DataLength > 0)
            line += $"\n    HEX: {InsertSpaces(hex)}";

        Console.ForegroundColor = direction.StartsWith("C") ? ConsoleColor.Cyan : ConsoleColor.Green;
        Console.WriteLine(line);
        Console.ResetColor();

        _writer?.WriteLine(line);
    }

    private static string InsertSpaces(string hex)
    {
        if (hex.Length == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < hex.Length; i += 2)
        {
            if (i > 0 && i % 32 == 0) sb.Append("\n    ");
            sb.Append(hex[i..(i + 2)]);
            sb.Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    public void Dispose() => _writer?.Dispose();
}
