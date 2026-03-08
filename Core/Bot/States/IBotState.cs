using InsightBot.Core.Configuration;
using InsightBot.Core.Game;
using InsightBot.Core.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot.States;

// ─────────────────────────────────────────────────────────────────────────────
// State pattern interfaces
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One discrete state in the bot's state machine.
/// Each implementation owns the full logic for that state.
/// </summary>
public interface IBotState
{
    BotState StateId { get; }

    /// <summary>Called once when the state is entered.</summary>
    Task OnEnterAsync(StateContext ctx, CancellationToken ct);

    /// <summary>
    /// Called every tick (~200 ms) while this state is active.
    /// Return the next state to transition to, or <see cref="StateId"/> to stay.
    /// </summary>
    Task<BotState> TickAsync(StateContext ctx, CancellationToken ct);

    /// <summary>Called once when the state is exited (even due to cancellation).</summary>
    Task OnExitAsync(StateContext ctx, CancellationToken ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// StateContext — shared data bag passed to every state
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared context injected into every state.
/// States read config, write to status, and send packets via proxy.
/// </summary>
public sealed class StateContext
{
    public BotProfile    Profile    { get; }
    public GameContext   Game       { get; }
    public SroProxy      Proxy      { get; }
    public BotStatus     Status     { get; }

    // Shared transient data between states
    public uint          CurrentTargetUid { get; set; }
    public int           AttackRetries    { get; set; }
    public DateTime      LastSkillCast    { get; set; } = DateTime.MinValue;
    public DateTime      LastHpPotion     { get; set; } = DateTime.MinValue;
    public DateTime      LastMpPotion     { get; set; } = DateTime.MinValue;
    public int           TownTripCount    { get; set; }
    public List<string>  Log              { get; } = new();

    public event Action<string>? LogMessage;

    public StateContext(BotProfile profile, GameContext game, SroProxy proxy, BotStatus status)
    {
        Profile = profile;
        Game    = game;
        Proxy   = proxy;
        Status  = status;
    }

    public void Emit(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Console.WriteLine(line);
        Log.Add(line);
        if (Log.Count > 500) Log.RemoveAt(0);
        LogMessage?.Invoke(line);
    }

    /// <summary>Send a packet to the server through the proxy.</summary>
    public Task SendAsync(Network.Packet packet, CancellationToken ct) =>
        Proxy.InjectToServerAsync(packet, ct);
}
