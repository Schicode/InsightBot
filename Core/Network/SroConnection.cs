using InsightBot.Core.Network;
using InsightBot.Core.Network.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace InsightBot.Core.Network;

/// <summary>
/// Manages a single TCP connection to a Silkroad server (Gateway or Agent).
/// Handles the receive loop, packet framing and Blowfish decryption.
/// </summary>
public sealed class SroConnection : IAsyncDisposable
{
    private const int BufferSize = 4096;

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly SecurityManager _security;
    private readonly byte[] _recvBuffer = new byte[BufferSize];
    private readonly List<byte> _leftover = new(); // Incomplete packet bytes

    public string Host { get; }
    public int Port { get; }
    public bool IsConnected => _tcp.Connected;

    public event Action<Packet>? PacketReceived;
    public event Action? Disconnected;

    private SroConnection(TcpClient tcp, string host, int port)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _security = new SecurityManager();
        Host = host;
        Port = port;
    }

    // ── Factory ─────────────────────────────────────────────────────────────

    public static async Task<SroConnection> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        tcp.NoDelay = true;
        await tcp.ConnectAsync(host, port, ct);
        return new SroConnection(tcp, host, port);
    }

    // ── Receive loop ────────────────────────────────────────────────────────

    public async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                int read = await _stream.ReadAsync(_recvBuffer, ct);
                if (read == 0) break; // Connection closed gracefully

                _leftover.AddRange(_recvBuffer.AsSpan(0, read).ToArray());
                ProcessBuffer();
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (IOException) { /* socket closed */ }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    private void ProcessBuffer()
    {
        byte[] data = _leftover.ToArray();
        int totalConsumed = 0;

        while (true)
        {
            int remaining = data.Length - totalConsumed;
            if (!_security.TryDecodePacket(data, totalConsumed, remaining, out var packet, out int consumed))
                break;

            totalConsumed += consumed;

            if (packet!.Opcode == Opcodes.HANDSHAKE)
            {
                var response = _security.HandleHandshake(packet);
                if (response != null)
                    _ = SendRawAsync(response.Serialize());
            }
            else
            {
                PacketReceived?.Invoke(packet);
            }
        }

        if (totalConsumed > 0)
            _leftover.RemoveRange(0, totalConsumed);
    }

    // ── Send ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a packet, applying encryption if the handshake is complete.
    /// </summary>
    public async Task SendAsync(Packet packet, CancellationToken ct = default)
    {
        byte[] data = _security.HandshakeDone
            ? _security.EncodePacket(packet)
            : packet.Serialize();

        await _stream.WriteAsync(data, ct);
    }

    /// <summary>Send raw bytes without any security processing.</summary>
    public Task SendRawAsync(byte[] data, CancellationToken ct = default) =>
        _stream.WriteAsync(data, ct).AsTask();

    // ── Disposal ────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _tcp.Dispose();
    }
}