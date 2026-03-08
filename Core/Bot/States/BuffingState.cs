using InsightBot.Core.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot.States;

/// <summary>
/// BUFFING state — cast all missing self-buffs and heals before resuming combat.
///
/// Transitions:
///   → Hunting   when all buffs are active and HP/MP are acceptable
/// </summary>
public sealed class BuffingState : IBotState
{
    public BotState StateId => BotState.Buffing;

    // Per-skill cooldown tracking (skillId → last cast time)
    private readonly Dictionary<uint, DateTime> _lastCast = new();
    private const int SkillIntervalMs = 600;

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        ctx.Status.Message = "Buffing…";
        return Task.CompletedTask;
    }

    public async Task<BotState> TickAsync(StateContext ctx, CancellationToken ct)
    {
        var local = ctx.Game.LocalCharacter;
        if (local == null) return BotState.Hunting;

        var cfg = ctx.Profile.Buffs;
        if (!cfg.Enabled) return BotState.Hunting;

        bool didCast = false;

        // ── Step 1: Heal if HP low ───────────────────────────────────────────
        if (cfg.HealSkillId != 0 && local.HpPercent < cfg.HealThreshold)
        {
            if (await TryCastAsync(cfg.HealSkillId, local.UniqueId, ctx, ct))
            {
                ctx.Emit($"Heal cast (HP={local.HpPercent:F0}%)");
                didCast = true;
            }
        }

        // ── Step 2: MP restore ───────────────────────────────────────────────
        if (cfg.MpSkillId != 0 && local.MpPercent < cfg.MpThreshold)
        {
            if (await TryCastAsync(cfg.MpSkillId, local.UniqueId, ctx, ct))
            {
                ctx.Emit($"MP skill cast (MP={local.MpPercent:F0}%)");
                didCast = true;
            }
        }

        // ── Step 3: Missing buffs ────────────────────────────────────────────
        foreach (uint skillId in cfg.SelfBuffSkillIds)
        {
            bool isActive = local.Buffs.Any(b => b.SkillId == skillId);
            if (!isActive)
            {
                if (await TryCastAsync(skillId, local.UniqueId, ctx, ct))
                {
                    ctx.Emit($"Buff cast: 0x{skillId:X8}");
                    didCast = true;
                    break; // One skill per tick — let server process before next
                }
            }
        }

        // ── Done? ────────────────────────────────────────────────────────────
        if (!didCast && !NeedsMoreBuffing(ctx))
        {
            ctx.Emit("All buffs applied, resuming hunt.");
            return BotState.Hunting;
        }

        return BotState.Buffing;
    }

    public Task OnExitAsync(StateContext ctx, CancellationToken ct) => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> TryCastAsync(uint skillId, uint targetUid,
        StateContext ctx, CancellationToken ct)
    {
        // Respect per-skill cooldown
        if (_lastCast.TryGetValue(skillId, out var last) &&
            (DateTime.Now - last).TotalMilliseconds < SkillIntervalMs)
            return false;

        var pkt = new PacketWriter()
            .WriteUInt32(skillId)
            .WriteByte(1)              // target: unique id
            .WriteUInt32(targetUid)
            .Build(Opcodes.C_SKILL_USE);

        await ctx.SendAsync(pkt, ct);

        _lastCast[skillId] = DateTime.Now;
        await Task.Delay(ctx.Profile.Buffs.SkillDelayMs, ct);
        return true;
    }

    private static bool NeedsMoreBuffing(StateContext ctx)
    {
        var local = ctx.Game.LocalCharacter;
        if (local == null) return false;

        var cfg = ctx.Profile.Buffs;

        if (cfg.HealSkillId != 0 && local.HpPercent < cfg.HealThreshold) return true;
        if (cfg.MpSkillId   != 0 && local.MpPercent < cfg.MpThreshold)   return true;

        foreach (uint id in cfg.SelfBuffSkillIds)
            if (!local.Buffs.Any(b => b.SkillId == id)) return true;

        return false;
    }
}
