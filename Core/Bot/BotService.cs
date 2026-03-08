using InsightBot.Core.Configuration;
using InsightBot.Core.Game;
using InsightBot.Core.Network;
using InsightBot.Core.Network.Handlers;
using InsightBot.Core.Pk2;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InsightBot.Core.Bot;

/// <summary>
/// Top-level facade the WinUI 3 application talks to.
/// Wires together: BotProfile, SroProxy, PacketDispatcher, GameContext, BotEngine.
///
/// Architecture:
///   SroProxy (localHost:proxyPort ↔ remoteHost:gatewayPort)
///     → PacketDispatcher (routes server packets to handlers)
///     → GameContext.Instance (shared game state)
///     → BotEngine (state machine: hunt / loot / buff / town …)
/// </summary>
public sealed class BotService : IAsyncDisposable
{
    // ── Subsystems ────────────────────────────────────────────────────────────

    public BotProfile Profile { get; private set; } = new();
    public GameContext Context => GameContext.Instance;
    public BotStatus Status => _engine?.Status ?? _idleStatus;
    public bool IsRunning => _engine?.IsRunning ?? false;

    private readonly BotStatus _idleStatus = new();
    private readonly PacketDispatcher _dispatcher = new();
    private SroProxy? _proxy;
    private BotEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _networkTask;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<BotState>? StateChanged;
    public event Action<string>? LogMessage;

    // ── Profile management ────────────────────────────────────────────────────

    public void LoadProfile(string nameOrPath)
    {
        string path = System.IO.File.Exists(nameOrPath)
            ? nameOrPath
            : BotProfile.ProfilePath(nameOrPath);

        Profile = BotProfile.LoadOrCreate(path);
        Log($"Profile '{Profile.ProfileName}' loaded.");
    }

    public void SaveProfile()
    {
        string path = BotProfile.ProfilePath(Profile.ProfileName);
        Profile.SaveTo(path);
        Log("Profile saved.");
    }

    public IEnumerable<string> ListProfiles() => BotProfile.ListProfiles();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        if (IsRunning) return;

        // Validate
        if (string.IsNullOrEmpty(Profile.Connection.Username))
        {
            Log("ERROR: No username configured.");
            return;
        }

        // Push secondary PIN into GameContext so PassKeyRequestHandler can send it
        GameContext.Instance.SetSecondaryPin(
            string.IsNullOrEmpty(Profile.Connection.SecondaryPin)
                ? null
                : Profile.Connection.SecondaryPin);

        // Register packet handlers
        HandlerRegistry.BuildDefault(_dispatcher);

        // Auto-load connection info from PK2 if not already set
        if (GameDataService.Instance.IsLoaded &&
            GameDataService.Instance.ConnectionInfo is { Gateways.Count: > 0 } info &&
            string.IsNullOrEmpty(Profile.Connection.GatewayHost))
        {
            var (host, port) = info.Gateways[0];
            Profile.Connection.GatewayHost = host;
            Profile.Connection.GatewayPort = port;
            Log($"Auto-configured gateway from PK2: {host}:{port}");
        }

        string remoteHost = Profile.Connection.GatewayHost ?? "localhost";
        int remotePort = Profile.Connection.GatewayPort > 0
            ? Profile.Connection.GatewayPort : 15779;
        int localPort = Profile.Connection.ProxyPort > 0
            ? Profile.Connection.ProxyPort : 15778;

        // Create proxy (localHost, localPort, remoteHost, remotePort)
        _proxy = new SroProxy("127.0.0.1", localPort, remoteHost, remotePort);

        // Wire dispatcher to proxy's server→client filter so we can read packets
        _proxy.ServerPacketFilter += p =>
        {
            _dispatcher.Dispatch(p);
            return true; // always forward
        };

        // Inject a send delegate so handlers can send packets to the server
        GameContext.Instance.SendPacket = packet =>
            _ = _proxy.InjectToServerAsync(packet);

        _cts = new CancellationTokenSource();
        _engine = new BotEngine(Profile, _proxy);
        _engine.StateChanged += s => StateChanged?.Invoke(s);
        _engine.LogMessage += m => Log(m);

        _proxy.Start();
        _networkTask = _proxy.AcceptAndRunAsync(_cts.Token);

        _engine.Start();
        Log("Bot started.");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        if (_networkTask is not null)
            try { await _networkTask.ConfigureAwait(false); } catch { /* expected */ }
        await _engine!.StopAsync();
        GameContext.Instance.SendPacket = null;
        Log("Bot stopped.");
    }

    public void Pause() => _engine?.Pause();
    public void Resume() => _engine?.Resume();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_networkTask is not null)
            try { await _networkTask.ConfigureAwait(false); } catch { /* expected */ }
        if (_proxy != null)
            await _proxy.DisposeAsync();
    }
}