using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InsightBot.Core.Bot.States;

/// <summary>
/// ATTACKING state — send attack / skill packets until the target dies or is lost.
///
/// Transitions:
///   → Looting   when target dies
///   → Hunting   when target is lost / out of range / un-targetable
///   → Buffing   when a critical heal is needed mid-fight
///   → Dead      when local character HP = 0
/// </summary>
public sealed class AttackingState : IBotState
{
    public BotState StateId => BotState.Attacking;

    private const int MaxRetries       = 5;
    private const int AttackCooldownMs = 600;
    private const int SkillCooldownMs  = 800;

    // Per-session skill rotation index (cycles through configured skills)
    private int      _skillIndex;
    private DateTime _lastAttack    = DateTime.MinValue;
    private DateTime _targetLostAt  = DateTime.MinValue;
    private bool     _targetLostFlag;

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        _skillIndex     = 0;
        _lastAttack     = DateTime.MinValue;
        _targetLostFlag = false;
        ctx.Status.Message = "Attacking…";
        return Task.CompletedTask;
    }

    public async Task<BotState> TickAsync(StateContext ctx, CancellationToken ct)
    {
        var local = ctx.Game.LocalCharacter;
        if (local == null || local.IsDead) return BotState.Dead;

        // ── Critical HP: heal immediately ────────────────────────────────────
        if (local.HpPercent < ctx.Profile.Hunt.MinHpPercent)
        {
            ctx.Emit($"HP critical ({local.HpPercent:F0}%) — retreating to buff.");
            return BotState.Buffing;
        }

        // ── Potions mid-fight ────────────────────────────────────────────────
        await TryUsePotionsAsync(ctx, ct);

        // ── Validate target ──────────────────────────────────────────────────
        var target = ctx.Game.CurrentTarget as Monster;

        if (target == null || target.IsDead)
        {
            ctx.Status.MonstersKilled++;
            ctx.Emit($"Monster killed. Session total: {ctx.Status.MonstersKilled}");
            ctx.Game.ClearTarget();
            return BotState.Looting;
        }

        // Target out of attack range?
        float dist = local.Position.DistanceTo(target.Position);
        if (dist > ctx.Profile.Hunt.MaxRange)
        {
            ctx.AttackRetries++;
            if (ctx.AttackRetries >= MaxRetries)
            {
                ctx.Emit($"Target '{target.Name}' out of range ({dist:F0} wu) — abandoning.");
                ctx.Game.ClearTarget();
                return BotState.Hunting;
            }
            // Chase
            await WalkToAsync(target.Position, ctx, ct);
            ctx.Status.Message = $"Chasing {target.Name} ({dist:F0} wu)…";
            return BotState.Attacking;
        }

        ctx.AttackRetries = 0;

        // ── Attack / skill rotation ───────────────────────────────────────────
        await ExecuteAttackAsync(target, ctx, ct);

        ctx.Status.Message =
            $"Attacking {target.Name} | HP {target.HpPercent:F0}% | dist {dist:F0}";

        return BotState.Attacking;
    }

    public Task OnExitAsync(StateContext ctx, CancellationToken ct) => Task.CompletedTask;

    // ── Attack execution ──────────────────────────────────────────────────────

    private async Task ExecuteAttackAsync(Monster target, StateContext ctx, CancellationToken ct)
    {
        var skillIds = ctx.Profile.Hunt.AttackSkillIds;

        if (skillIds.Count > 0)
        {
            // Skill rotation
            int cooldown = SkillCooldownMs;
            if ((DateTime.Now - ctx.LastSkillCast).TotalMilliseconds < cooldown)
                return; // Wait for cooldown

            uint skillId = skillIds[_skillIndex % skillIds.Count];
            _skillIndex++;

            var pkt = new PacketWriter()
                .WriteUInt32(skillId)
                .WriteByte(1)                    // target type: unique id
                .WriteUInt32(target.UniqueId)
                .Build(Opcodes.C_SKILL_USE);

            await ctx.SendAsync(pkt, ct);
            ctx.LastSkillCast = DateTime.Now;
        }
        else
        {
            // Basic auto-attack
            if ((DateTime.Now - _lastAttack).TotalMilliseconds < AttackCooldownMs)
                return;

            var pkt = new PacketWriter()
                .WriteByte(1)                    // type: target
                .WriteUInt32(target.UniqueId)
                .Build(Opcodes.C_ATTACK);

            await ctx.SendAsync(pkt, ct);
            _lastAttack = DateTime.Now;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task TryUsePotionsAsync(StateContext ctx, CancellationToken ct)
    {
        var local = ctx.Game.LocalCharacter;
        if (local == null) return;

        var p = ctx.Profile.Potions;

        if (p.AutoUseHpPotion && p.HpPotionRefId != 0 &&
            local.HpPercent < p.HpPotionThreshold &&
            (DateTime.Now - ctx.LastHpPotion).TotalMilliseconds >= p.CooldownMs)
        {
            await UseItemAsync(p.HpPotionRefId, ctx, ct);
            ctx.LastHpPotion = DateTime.Now;
            ctx.Status.PotionsUsed++;
        }

        if (p.AutoUseMpPotion && p.MpPotionRefId != 0 &&
            local.MpPercent < p.MpPotionThreshold &&
            (DateTime.Now - ctx.LastMpPotion).TotalMilliseconds >= p.CooldownMs)
        {
            await UseItemAsync(p.MpPotionRefId, ctx, ct);
            ctx.LastMpPotion = DateTime.Now;
            ctx.Status.PotionsUsed++;
        }
    }

    private static Task UseItemAsync(uint refId, StateContext ctx, CancellationToken ct)
    {
        var pkt = new PacketWriter().WriteByte(0).WriteUInt32(refId).Build(0x7030);
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
