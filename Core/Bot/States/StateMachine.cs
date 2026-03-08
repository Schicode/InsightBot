using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot.States;

/// <summary>
/// Drives the bot's state machine by maintaining a registry of <see cref="IBotState"/>
/// implementations and calling their lifecycle methods on each tick.
///
/// Replace the <c>switch</c> in <see cref="BotEngine"/> with:
/// <code>
///   var sm = new StateMachine(context);
///   await sm.RunAsync(cancellationToken);
/// </code>
/// </summary>
public sealed class StateMachine
{
    private readonly Dictionary<BotState, IBotState> _states;
    private readonly StateContext _ctx;

    private IBotState _current;

    public BotState  CurrentState  => _current.StateId;
    public BotStatus Status        => _ctx.Status;

    /// <summary>Raised on every state transition.</summary>
    public event Action<BotState, BotState>? Transitioned; // (from, to)

    public StateMachine(StateContext ctx)
    {
        _ctx = ctx;

        // Register all concrete states
        _states = new Dictionary<BotState, IBotState>
        {
            [BotState.Hunting]   = new HuntingState(),
            [BotState.Attacking] = new AttackingState(),
            [BotState.Looting]   = new LootingState(),
            [BotState.Buffing]   = new BuffingState(),
            [BotState.Town]      = new TownState(),
            [BotState.Returning] = new ReturningState(),
            [BotState.Dead]      = new DeadState(),
            [BotState.Paused]    = new PausedState(),
        };

        _current = _states[BotState.Hunting];
    }

    // ── External control (called by BotEngine) ────────────────────────────────

    /// <summary>Force an immediate transition (e.g. Pause button or death event).</summary>
    public async Task ForceTransitionAsync(BotState target, CancellationToken ct)
    {
        if (!_states.TryGetValue(target, out var nextState)) return;
        await TransitionToAsync(nextState, ct);
    }

    // ── Main loop ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Run the state machine until <paramref name="ct"/> is cancelled.
    /// Call from a background Task (e.g. <c>Task.Run</c>).
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // Enter initial state
        await _current.OnEnterAsync(_ctx, ct);
        _ctx.Status.State = _current.StateId;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                BotState next;

                try
                {
                    next = await _current.TickAsync(_ctx, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _ctx.Emit($"[ERROR in {_current.StateId}] {ex.Message}");
                    next = BotState.Hunting; // safe fallback
                }

                if (next != _current.StateId)
                {
                    if (_states.TryGetValue(next, out var nextState))
                        await TransitionToAsync(nextState, ct);
                    else
                        _ctx.Emit($"[SM] Unknown target state: {next}");
                }

                await Task.Delay(180, ct); // ~5 ticks/s
            }
        }
        catch (OperationCanceledException) { /* clean exit */ }
        finally
        {
            // Always exit current state cleanly
            try { await _current.OnExitAsync(_ctx, ct); } catch { /* ignore on shutdown */ }
        }
    }

    // ── Transition ────────────────────────────────────────────────────────────

    private async Task TransitionToAsync(IBotState next, CancellationToken ct)
    {
        var from = _current.StateId;

        // Exit current
        try { await _current.OnExitAsync(_ctx, ct); }
        catch (Exception ex) { _ctx.Emit($"[SM] OnExit error ({from}): {ex.Message}"); }

        _current = next;
        _ctx.Status.State = next.StateId;

        // Enter next
        try { await _current.OnEnterAsync(_ctx, ct); }
        catch (Exception ex) { _ctx.Emit($"[SM] OnEnter error ({next.StateId}): {ex.Message}"); }

        _ctx.Emit($"[SM] {from} → {next.StateId}");
        Transitioned?.Invoke(from, next.StateId);
    }
}
