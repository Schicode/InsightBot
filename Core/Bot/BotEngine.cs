using InsightBot.Core.Bot.States;
using InsightBot.Core.Configuration;
using InsightBot.Core.Game;
using InsightBot.Core.Network;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InsightBot.Core.Bot;

/// <summary>
/// Central bot orchestrator — owns lifecycle (Start/Stop/Pause/Resume)
/// and delegates all state logic to <see cref="StateMachine"/>.
/// </summary>
public sealed class BotEngine : IAsyncDisposable
{
    private readonly GameContext _ctx;
    private readonly SroProxy _proxy;

    public BotProfile Profile { get; private set; }
    public BotStatus Status { get; } = new();
    public bool IsRunning => _loopTask is { IsCompleted: false };

    private StateMachine? _sm;
    private StateContext? _smCtx;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<BotState>? StateChanged;
    public event Action<string>? LogMessage;

    public BotEngine(BotProfile profile, SroProxy proxy)
    {
        Profile = profile;
        _ctx = GameContext.Instance;
        _proxy = proxy;

        _ctx.EntityDied += OnEntityDied;
        _ctx.CharacterUpdated += OnCharacterUpdated;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        Status.Reset();
        Status.StartedAt = DateTime.Now;

        _smCtx = new StateContext(Profile, _ctx, _proxy, Status);
        _smCtx.LogMessage += m => LogMessage?.Invoke(m);

        _sm = new StateMachine(_smCtx);
        _sm.Transitioned += (_, to) => StateChanged?.Invoke(to);

        _loopTask = _sm.RunAsync(_cts.Token);
        Log("Bot started.");
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        if (_loopTask != null) await _loopTask.ConfigureAwait(false);
        Status.State = BotState.Idle;
        StateChanged?.Invoke(BotState.Idle);
        Log("Bot stopped.");
    }

    public void Pause()
    {
        if (_sm == null || Status.State == BotState.Paused) return;
        _ = _sm.ForceTransitionAsync(BotState.Paused, _cts?.Token ?? default);
    }

    public void Resume()
    {
        if (_sm == null || Status.State != BotState.Paused) return;
        _ = _sm.ForceTransitionAsync(BotState.Hunting, _cts?.Token ?? default);
    }

    public void LoadProfile(BotProfile profile)
    {
        Profile = profile;
        Log($"Profile updated: {profile.ProfileName} (takes effect on next start)");
    }

    private void OnEntityDied(uint uid)
    {
        var local = _ctx.LocalCharacter;
        if (local != null && uid == local.UniqueId && _sm != null)
        {
            Status.DeathCount++;
            Log("Character died!");
            _ = _sm.ForceTransitionAsync(BotState.Dead, _cts?.Token ?? default);
        }
    }

    private void OnCharacterUpdated(Game.Entities.Character c)
    {
        if (_sm == null) return;
        if (c.HpPercent < Profile.Hunt.MinHpPercent && Status.State == BotState.Attacking)
            _ = _sm.ForceTransitionAsync(BotState.Buffing, _cts?.Token ?? default);
    }

    private void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Console.WriteLine(line);
        LogMessage?.Invoke(line);
    }

    public async ValueTask DisposeAsync()
    {
        _ctx.EntityDied -= OnEntityDied;
        _ctx.CharacterUpdated -= OnCharacterUpdated;
        await StopAsync();
    }
}