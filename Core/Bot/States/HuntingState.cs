using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot.States;

/// <summary>
/// HUNTING state — scan for the nearest valid monster and initiate attack.
///
/// Transitions:
///   → Attacking   when a valid target is in range
///   → Buffing     when a buff is missing or heal is needed
///   → Town        when a town-trip condition is met
///   → Looting     if unlooted items are nearby (cleanup pass)
/// </summary>
public sealed class HuntingState : IBotState
{
    public BotState StateId => BotState.Hunting;

    private int _noTargetTicks;
    private const int MaxNoTargetTicks = 20; // ~4 s before logging warning

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        _noTargetTicks = 0;
        ctx.CurrentTargetUid = 0;
        ctx.Game.ClearTarget();
        ctx.Status.Message = "Hunting…";
        return Task.CompletedTask;
    }

    public async Task<BotState> TickAsync(StateContext ctx, CancellationToken ct)
    {
        // ── Priority 1: buffs / heal ─────────────────────────────────────────
        if (NeedsBuffing(ctx)) return BotState.Buffing;

        // ── Priority 2: potions ──────────────────────────────────────────────
        if (await TryUsePotionsAsync(ctx, ct)) return BotState.Hunting;

        // ── Priority 3: town trip ────────────────────────────────────────────
        if (NeedsTownTrip(ctx)) return BotState.Town;

        // ── Priority 4: loot cleanup ─────────────────────────────────────────
        var nearItem = ctx.Game.NearestPickableItem(ctx.Profile.Loot.MaxLootRange);
        if (nearItem != null && ctx.Profile.Loot.Enabled) return BotState.Looting;

        // ── Priority 5: find target ──────────────────────────────────────────
        var target = FindBestTarget(ctx);
        if (target == null)
        {
            _noTargetTicks++;
            if (_noTargetTicks == MaxNoTargetTicks)
                ctx.Emit("No monsters in range — waiting…");
            ctx.Status.Message = "Searching for monsters…";
            return BotState.Hunting;
        }

        _noTargetTicks = 0;

        // Walk toward target if outside attack range
        var local = ctx.Game.LocalCharacter;
        if (local != null)
        {
            float dist = local.Position.DistanceTo(target.Position);
            float attackRange = ctx.Profile.Hunt.MaxRange;

            if (dist > attackRange * 0.85f) // start walking at 85 % of max range
            {
                await WalkToAsync(target.Position, ctx, ct);
                ctx.Status.Message = $"Walking to {target.Name} ({dist:F0} wu)";
                return BotState.Hunting; // re-evaluate next tick
            }
        }

        // Commit to this target
        ctx.CurrentTargetUid = target.UniqueId;
        ctx.Game.SetTarget(target.UniqueId);
        ctx.AttackRetries = 0;
        ctx.Emit($"Target acquired: {target.Name} HP={target.HpPercent:F0}%");
        return BotState.Attacking;
    }

    public Task OnExitAsync(StateContext ctx, CancellationToken ct) => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Monster? FindBestTarget(StateContext ctx)
    {
        var local = ctx.Game.LocalCharacter;
        if (local == null) return null;

        var cfg = ctx.Profile.Hunt;

        return ctx.Game.Monsters
            .Where(m =>
                !m.IsDead &&
                m.IsHostile &&
                (cfg.AttackElite  || !m.IsElite) &&
                (cfg.AttackUnique || !m.IsUniqueMonster) &&
                !cfg.IgnoreRefIds.Contains(m.RefId) &&
                (cfg.TargetRefIds.Count == 0 || cfg.TargetRefIds.Contains(m.RefId)) &&
                local.Position.DistanceTo(m.Position) <= cfg.MaxRange
            )
            .OrderBy(m => local.Position.DistanceTo(m.Position))
            .FirstOrDefault();
    }

    private static bool NeedsBuffing(StateContext ctx)
    {
        if (!ctx.Profile.Buffs.Enabled) return false;
        var local = ctx.Game.LocalCharacter;
        if (local == null) return false;

        if (ctx.Profile.Buffs.HealSkillId != 0 &&
            local.HpPercent < ctx.Profile.Buffs.HealThreshold) return true;

        foreach (var id in ctx.Profile.Buffs.SelfBuffSkillIds)
            if (!local.Buffs.Any(b => b.SkillId == id)) return true;

        return false;
    }

    private static bool NeedsTownTrip(StateContext ctx)
    {
        if (!ctx.Profile.Town.Enabled) return false;
        // Extend here: inventory fullness, potion count, durability…
        return false;
    }

    private static async Task<bool> TryUsePotionsAsync(StateContext ctx, CancellationToken ct)
    {
        var local = ctx.Game.LocalCharacter;
        if (local == null) return false;

        bool used = false;
        var pcfg  = ctx.Profile.Potions;

        if (pcfg.AutoUseHpPotion && pcfg.HpPotionRefId != 0 &&
            local.HpPercent < pcfg.HpPotionThreshold &&
            (DateTime.Now - ctx.LastHpPotion).TotalMilliseconds >= pcfg.CooldownMs)
        {
            await UseItemAsync(pcfg.HpPotionRefId, ctx, ct);
            ctx.LastHpPotion = DateTime.Now;
            used = true;
        }

        if (pcfg.AutoUseMpPotion && pcfg.MpPotionRefId != 0 &&
            local.MpPercent < pcfg.MpPotionThreshold &&
            (DateTime.Now - ctx.LastMpPotion).TotalMilliseconds >= pcfg.CooldownMs)
        {
            await UseItemAsync(pcfg.MpPotionRefId, ctx, ct);
            ctx.LastMpPotion = DateTime.Now;
            used = true;
        }

        return used;
    }

    private static Task UseItemAsync(uint refId, StateContext ctx, CancellationToken ct)
    {
        var pkt = new PacketWriter()
            .WriteByte(0)
            .WriteUInt32(refId)
            .Build(0x7030);
        return ctx.SendAsync(pkt, ct);
    }

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
