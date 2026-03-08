using InsightBot.Core.Network;
using InsightBot.Core.Network.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace InsightBot.Core.Network;

/// <summary>
/// TCP proxy that intercepts traffic between the Silkroad game client
/// and the real server (Gateway or Agent).
///
/// Flow:
///   Game Client → [SroProxy.Listen] → [real server]
///   Game Client ← [SroProxy.Listen] ← [real server]
///
/// The proxy allows the bot to:
///   1. Read all packets in both directions
///   2. Inject packets (client→server or server→client)
///   3. Block or modify specific packets
/// </summary>
public sealed class SroProxy : IAsyncDisposable
{
    // ── Config ──────────────────────────────────────────────────────────────

    public string LocalHost { get; }
    public int LocalPort { get; }
    public string RemoteHost { get; }
    public int RemotePort { get; }

    // ── Events (both directions) ─────────────────────────────────────────────

    /// <summary>
    /// Fired for every packet the CLIENT sends to the server.
    /// Return false to BLOCK the packet (it will not be forwarded).
    /// </summary>
    public event Func<Packet, bool>? ClientPacketFilter;

    /// <summary>
    /// Fired for every packet the SERVER sends to the client.
    /// Return false to BLOCK the packet.
    /// </summary>
    public event Func<Packet, bool>? ServerPacketFilter;

    /// <summary>Raised when a new client connection is accepted.</summary>
    public event Action? ClientConnected;

    /// <summary>Raised when the connection is terminated.</summary>
    public event Action? Disconnected;

    // ── State ───────────────────────────────────────────────────────────────

    private TcpListener? _listener;
    private SroProxySession? _session;
    private bool _running;

    public bool IsRunning => _running;

    // ── Constructor ─────────────────────────────────────────────────────────

    public SroProxy(string localHost, int localPort, string remoteHost, int remotePort)
    {
        LocalHost = localHost;
        LocalPort = localPort;
        RemoteHost = remoteHost;
        RemotePort = remotePort;
    }

    // ── Control ─────────────────────────────────────────────────────────────

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Parse(LocalHost), LocalPort);
        _listener.Start();
        _running = true;
    }

    public void Stop()
    {
        _running = false;
        _listener?.Stop();
        _session?.Dispose();
    }

    /// <summary>
    /// Accept one client connection, set up the proxy session, and run it.
    /// Call this in a loop if you want to support multiple sequential connections.
    /// </summary>
    public async Task AcceptAndRunAsync(CancellationToken ct)
    {
        if (_listener == null) throw new InvalidOperationException("Call Start() first.");

        TcpClient clientTcp = await _listener.AcceptTcpClientAsync(ct);
        clientTcp.NoDelay = true;
        ClientConnected?.Invoke();

        // Open the connection to the real server
        TcpClient serverTcp = new() { NoDelay = true };
        await serverTcp.ConnectAsync(RemoteHost, RemotePort, ct);

        _session = new SroProxySession(clientTcp, serverTcp, ClientPacketFilter, ServerPacketFilter);
        _session.Disconnected += () => Disconnected?.Invoke();

        await _session.RunAsync(ct);
    }

    // ── Inject ──────────────────────────────────────────────────────────────

    /// <summary>Inject a packet as if the bot were the game client (send to server).</summary>
    public Task InjectToServerAsync(Packet packet, CancellationToken ct = default) =>
        _session?.SendToServerAsync(packet, ct) ?? Task.CompletedTask;

    /// <summary>Inject a packet as if the server sent it to the client.</summary>
    public Task InjectToClientAsync(Packet packet, CancellationToken ct = default) =>
        _session?.SendToClientAsync(packet, ct) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_session != null)
            _session.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages the bidirectional relay for one client↔proxy↔server session.
/// Each direction runs on its own Task to achieve full-duplex streaming.
/// </summary>
internal sealed class SroProxySession : IDisposable
{
    private readonly TcpClient _clientTcp;
    private readonly TcpClient _serverTcp;
    private readonly NetworkStream _clientStream;
    private readonly NetworkStream _serverStream;

    private readonly SecurityManager _clientSecurity = new(); // Client ↔ Proxy
    private readonly SecurityManager _serverSecurity = new(); // Proxy ↔ Server

    private readonly Func<Packet, bool>? _clientFilter;
    private readonly Func<Packet, bool>? _serverFilter;

    private readonly List<byte> _clientBuffer = new();
    private readonly List<byte> _serverBuffer = new();

    private readonly SemaphoreSlim _clientSend = new(1, 1);
    private readonly SemaphoreSlim _serverSend = new(1, 1);

    public event Action? Disconnected;

    public SroProxySession(
        TcpClient clientTcp, TcpClient serverTcp,
        Func<Packet, bool>? clientFilter, Func<Packet, bool>? serverFilter)
    {
        _clientTcp = clientTcp;
        _serverTcp = serverTcp;
        _clientStream = clientTcp.GetStream();
        _serverStream = serverTcp.GetStream();
        _clientFilter = clientFilter;
        _serverFilter = serverFilter;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            await Task.WhenAny(
                RelayAsync(_serverStream, _serverBuffer, _serverSecurity, _clientStream, _clientSecurity, _serverFilter, cts.Token),
                RelayAsync(_clientStream, _clientBuffer, _clientSecurity, _serverStream, _serverSecurity, _clientFilter, cts.Token)
            );
        }
        finally
        {
            cts.Cancel();
            Disconnected?.Invoke();
        }
    }

    /// <summary>
    /// Reads from <paramref name="source"/>, decrypts with <paramref name="readSecurity"/>,
    /// fires the filter, then re-encrypts with <paramref name="writeSecurity"/> and
    /// writes to <paramref name="dest"/>.
    /// </summary>
    private async Task RelayAsync(
        NetworkStream source, List<byte> readBuffer, SecurityManager readSecurity,
        NetworkStream dest, SecurityManager writeSecurity,
        Func<Packet, bool>? filter,
        CancellationToken ct)
    {
        byte[] tmp = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await source.ReadAsync(tmp, ct);
                if (read == 0) return;

                readBuffer.AddRange(tmp.AsSpan(0, read).ToArray());
                await ProcessRelayBufferAsync(readBuffer, readSecurity, dest, writeSecurity, filter, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private async Task ProcessRelayBufferAsync(
        List<byte> buffer, SecurityManager readSec,
        NetworkStream dest, SecurityManager writeSec,
        Func<Packet, bool>? filter,
        CancellationToken ct)
    {
        // ToArray() avoids any Span-in-async restriction (C# 12 compatible)
        byte[] data = buffer.ToArray();
        int totalConsumed = 0;

        while (true)
        {
            int remaining = data.Length - totalConsumed;
            if (!readSec.TryDecodePacket(data, totalConsumed, remaining, out var packet, out int consumed))
                break;

            totalConsumed += consumed;

            if (packet!.Opcode == Opcodes.HANDSHAKE)
            {
                var response = readSec.HandleHandshake(packet);
                if (response != null)
                    await WriteToStream(dest, response.Serialize(), ct);

                byte[] fwd = packet.Serialize();
                await WriteToStream(dest, fwd, ct);
                continue;
            }

            bool forward = filter?.Invoke(packet) ?? true;
            if (!forward) continue;

            byte[] encoded = writeSec.HandshakeDone
                ? writeSec.EncodePacket(packet)
                : packet.Serialize();

            await WriteToStream(dest, encoded, ct);
        }

        if (totalConsumed > 0)
            buffer.RemoveRange(0, totalConsumed);
    }

    private static SemaphoreSlim _writeLock = new(1, 1);

    private async Task WriteToStream(NetworkStream stream, byte[] data, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try { await stream.WriteAsync(data, ct); }
        finally { _writeLock.Release(); }
    }

    // ── Injection ────────────────────────────────────────────────────────────

    public async Task SendToServerAsync(Packet packet, CancellationToken ct)
    {
        byte[] data = _serverSecurity.HandshakeDone
            ? _serverSecurity.EncodePacket(packet)
            : packet.Serialize();

        await _serverSend.WaitAsync(ct);
        try { await _serverStream.WriteAsync(data, ct); }
        finally { _serverSend.Release(); }
    }

    public async Task SendToClientAsync(Packet packet, CancellationToken ct)
    {
        byte[] data = _clientSecurity.HandshakeDone
            ? _clientSecurity.EncodePacket(packet)
            : packet.Serialize();

        await _clientSend.WaitAsync(ct);
        try { await _clientStream.WriteAsync(data, ct); }
        finally { _clientSend.Release(); }
    }

    public void Dispose()
    {
        _clientStream.Dispose();
        _serverStream.Dispose();
        _clientTcp.Dispose();
        _serverTcp.Dispose();
    }
}