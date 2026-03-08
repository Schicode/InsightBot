using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot.States;

// ─────────────────────────────────────────────────────────────────────────────
// ReturningState — walk the configured waypoint path back to the hunt area
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// RETURNING state — walks each waypoint from <see cref="Configuration.TownConfig.ReturnPath"/>
/// sequentially until the character arrives at the hunt area.
///
/// Transitions:
///   → Buffing   when all waypoints are reached (re-apply buffs before hunting)
///   → Hunting   if no waypoints are configured
/// </summary>
public sealed class ReturningState : IBotState
{
    public BotState StateId => BotState.Returning;

    private int      _waypointIndex;
    private DateTime _moveTime = DateTime.MinValue;
    private const int WaypointToleranceWu = 60;
    private const int MoveSendCooldownMs  = 3_000; // Resend move every 3s if stuck
    private const int WaypointTimeoutMs   = 20_000;
    private DateTime _waypointStartedAt;

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        _waypointIndex    = 0;
        _moveTime         = DateTime.MinValue;
        _waypointStartedAt = DateTime.Now;
        ctx.Status.Message = "Returning to hunt area…";
        ctx.Emit("Returning: following waypoints.");
        return Task.CompletedTask;
    }

    public async Task<BotState> TickAsync(StateContext ctx, CancellationToken ct)
    {
        var waypoints = ctx.Profile.Town.ReturnPath;

        if (waypoints.Count == 0)
        {
            ctx.Emit("No return waypoints configured — resuming hunt.");
            return BotState.Buffing;
        }

        if (_waypointIndex >= waypoints.Count)
        {
            ctx.Emit("All waypoints reached, resuming.");
            return BotState.Buffing;
        }

        var local = ctx.Game.LocalCharacter;
        if (local == null) return BotState.Returning;

        var wp      = waypoints[_waypointIndex];
        var wpPos   = new WorldPosition(wp.X, wp.Y, wp.Z, wp.Region);
        float dist  = local.Position.DistanceTo(wpPos);

        // Arrived at waypoint?
        if (dist <= WaypointToleranceWu)
        {
            string label = string.IsNullOrEmpty(wp.Label) ? $"WP{_waypointIndex}" : wp.Label;
            ctx.Emit($"Reached {label} ({_waypointIndex + 1}/{waypoints.Count})");
            _waypointIndex++;
            _waypointStartedAt = DateTime.Now;
            return BotState.Returning;
        }

        // Timeout? (stuck)
        if ((DateTime.Now - _waypointStartedAt).TotalMilliseconds > WaypointTimeoutMs)
        {
            ctx.Emit($"Waypoint timeout — skipping WP{_waypointIndex}.");
            _waypointIndex++;
            _waypointStartedAt = DateTime.Now;
            return BotState.Returning;
        }

        // Resend movement packet periodically
        if ((DateTime.Now - _moveTime).TotalMilliseconds >= MoveSendCooldownMs)
        {
            await WalkToAsync(wpPos, ctx, ct);
            _moveTime = DateTime.Now;
            ctx.Status.Message =
                $"Returning [{_waypointIndex + 1}/{waypoints.Count}] dist={dist:F0}";
        }

        return BotState.Returning;
    }

    public Task OnExitAsync(StateContext ctx, CancellationToken ct) => Task.CompletedTask;

    private static Task WalkToAsync(WorldPosition dest, StateContext ctx, CancellationToken ct)
    {
        var pkt = new PacketWriter()
            .WriteInt16((short)(dest.X * 10))
            .WriteInt16((short)(dest.Z * 10))
            .WriteInt16((short)(dest.Y * 10))
            .WriteUInt16(dest.Region)
            .WriteByte(0)
            .Build(Opcodes.C_MOVEMENT);
        return ctx.SendAsync(pkt, ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DeadState — wait for revive, auto-resurrect if configured
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// DEAD state — monitor HP until the character is revived.
///
/// Transitions:
///   → Buffing   when the character is alive again (HP > 0)
/// </summary>
public sealed class DeadState : IBotState
{
    public BotState StateId => BotState.Dead;

    private DateTime _diedAt;
    private const int ReviveCheckIntervalMs = 2_000;
    private const int AutoReviveDelayMs     = 5_000; // wait before accepting revive prompt

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        _diedAt = DateTime.Now;
        ctx.Status.DeathCount++;
        ctx.Status.Message = "Dead — waiting for revive…";
        ctx.Emit($"Character died. Total deaths: {ctx.Status.DeathCount}");
        ctx.Game.ClearTarget();
        return Task.CompletedTask;
    }

    public async Task<BotState> TickAsync(StateContext ctx, CancellationToken ct)
    {
        var local = ctx.Game.LocalCharacter;

        // Character revived (HP > 0)?
        if (local != null && local.IsAlive)
        {
            ctx.Emit($"Revived after {(DateTime.Now - _diedAt).TotalSeconds:F1}s.");
            return BotState.Buffing;
        }

        // After a short wait, send the revive/respawn acceptance packet
        if ((DateTime.Now - _diedAt).TotalMilliseconds >= AutoReviveDelayMs)
        {
            await AcceptReviveAsync(ctx, ct);
        }

        int waited = (int)(DateTime.Now - _diedAt).TotalSeconds;
        ctx.Status.Message = $"Dead — waited {waited}s…";
        await Task.Delay(ReviveCheckIntervalMs, ct);
        return BotState.Dead;
    }

    public Task OnExitAsync(StateContext ctx, CancellationToken ct) => Task.CompletedTask;

    private static Task AcceptReviveAsync(StateContext ctx, CancellationToken ct)
    {
        // 0xB077 is the respawn packet — clicking "Resurrect at nearest town"
        var pkt = new PacketWriter()
            .WriteByte(1) // resurrect type: 1 = town
            .Build(Opcodes.S_RESPAWN);
        return ctx.SendAsync(pkt, ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PausedState — keep connection alive, do nothing
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PausedState : IBotState
{
    public BotState StateId => BotState.Paused;

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        ctx.Status.Message = "Paused.";
        ctx.Emit("Bot paused by user.");
        return Task.CompletedTask;
    }

    public Task<BotState> TickAsync(StateContext ctx, CancellationToken ct) =>
        Task.FromResult(BotState.Paused);

    public Task OnExitAsync(StateContext ctx, CancellationToken ct)
    {
        ctx.Emit("Bot resumed.");
        return Task.CompletedTask;
    }
}
