using InsightBot.Core.Configuration;
using InsightBot.Core.Game;
using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot;

// ─────────────────────────────────────────────────────────────────────────────
// HuntEngine — attack logic
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HuntEngine
{
    private readonly HuntConfig      _cfg;
    private readonly SroProxy        _proxy;
    private readonly GameContext     _ctx;

    private DateTime _lastAttackAt = DateTime.MinValue;
    private const int AttackCooldownMs = 800;

    public HuntEngine(HuntConfig cfg, SroProxy proxy, GameContext ctx)
    {
        _cfg   = cfg;
        _proxy = proxy;
        _ctx   = ctx;
    }

    /// <summary>Send one attack or skill-use packet toward <paramref name="target"/>.</summary>
    public async Task AttackTargetAsync(Monster target, CancellationToken ct)
    {
        // Throttle attack rate
        var elapsed = DateTime.Now - _lastAttackAt;
        if (elapsed.TotalMilliseconds < AttackCooldownMs)
        {
            await Task.Delay(AttackCooldownMs - (int)elapsed.TotalMilliseconds, ct);
        }

        Packet pkt;

        if (_cfg.AttackSkillIds.Count > 0)
        {
            // 0x7070 — skill use (first skill in rotation)
            pkt = new PacketWriter()
                .WriteUInt32(_cfg.AttackSkillIds[0])
                .WriteByte(1)                     // target type: unique id
                .WriteUInt32(target.UniqueId)
                .Build(Opcodes.C_SKILL_USE);
        }
        else
        {
            // basic melee attack
            pkt = new PacketWriter()
                .WriteByte(1)                     // attack type: target
                .WriteUInt32(target.UniqueId)
                .Build(Opcodes.C_ATTACK);
        }

        await _proxy.InjectToServerAsync(pkt, ct);
        _lastAttackAt = DateTime.Now;
    }

    /// <summary>
    /// Determines whether a monster qualifies as an attack target
    /// based on configured RefId lists and flags.
    /// </summary>
    public bool IsValidTarget(Monster m)
    {
        if (m.IsDead) return false;
        if (!m.IsHostile) return false;
        if (!_cfg.AttackElite  && m.IsElite)        return false;
        if (!_cfg.AttackUnique && m.IsUniqueMonster) return false;
        if (_cfg.IgnoreRefIds.Contains(m.RefId))    return false;
        if (_cfg.TargetRefIds.Count > 0 && !_cfg.TargetRefIds.Contains(m.RefId))
            return false;
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// LootEngine — pick up ground items
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LootEngine
{
    private readonly LootConfig  _cfg;
    private readonly SroProxy    _proxy;
    private readonly GameContext _ctx;

    public LootEngine(LootConfig cfg, SroProxy proxy, GameContext ctx)
    {
        _cfg   = cfg;
        _proxy = proxy;
        _ctx   = ctx;
    }

    /// <summary>
    /// Finds the nearest eligible item, moves to it, and sends a pickup packet.
    /// Returns true if a pickup was attempted.
    /// </summary>
    public async Task<bool> LootNearbyAsync(CancellationToken ct)
    {
        if (_ctx.LocalCharacter == null) return false;

        uint localUid = _ctx.LocalCharacter.UniqueId;
        var item = _ctx.GroundItems
            .Where(i => IsEligible(i, localUid))
            .OrderBy(i => _ctx.LocalCharacter.Position.DistanceTo(i.Position))
            .FirstOrDefault();

        if (item == null) return false;

        // Move to item if needed
        float dist = _ctx.LocalCharacter.Position.DistanceTo(item.Position);
        if (dist > 60f)
        {
            await MoveToAsync(item.Position, ct);
            await Task.Delay(800, ct);
        }

        // Send pick-up packet: 0x7034
        var pkt = new PacketWriter()
            .WriteUInt32(item.UniqueId)
            .Build(Opcodes.C_ITEM_PICKUP);

        await _proxy.InjectToServerAsync(pkt, ct);
        return true;
    }

    private bool IsEligible(GroundItem item, uint localUid)
    {
        if (!item.CanPickUp(localUid)) return false;

        if (item.IsGold && _cfg.PickUpGold) return true;
        if (item.IsGold && !_cfg.PickUpGold) return false;

        if (_cfg.IgnoreRefIds.Contains(item.ItemRefId)) return false;
        if (_cfg.AllowedRefIds.Count > 0)
            return _cfg.AllowedRefIds.Contains(item.ItemRefId);

        return true;
    }

    private async Task MoveToAsync(WorldPosition dest, CancellationToken ct)
    {
        var pkt = new PacketWriter()
            .WriteInt16((short)(dest.X * 10))
            .WriteInt16((short)(dest.Z * 10))
            .WriteInt16((short)(dest.Y * 10))
            .WriteUInt16(dest.Region)
            .WriteByte(0)
            .Build(Opcodes.C_MOVEMENT);

        await _proxy.InjectToServerAsync(pkt, ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BuffEngine — maintain self-buffs
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BuffEngine
{
    private readonly BuffConfig  _cfg;
    private readonly SroProxy    _proxy;
    private readonly GameContext _ctx;

    private DateTime _lastCastAt = DateTime.MinValue;

    public BuffEngine(BuffConfig cfg, SroProxy proxy, GameContext ctx)
    {
        _cfg   = cfg;
        _proxy = proxy;
        _ctx   = ctx;
    }

    /// <summary>True if any configured buff is missing from the character's active buffs.</summary>
    public bool NeedsBuffing()
    {
        if (!_cfg.Enabled) return false;
        var local = _ctx.LocalCharacter;
        if (local == null) return false;

        // Check buff list
        foreach (var skillId in _cfg.SelfBuffSkillIds)
            if (!local.Buffs.Any(b => b.SkillId == skillId))
                return true;

        // Check heal threshold
        if (_cfg.HealSkillId != 0 && local.HpPercent < _cfg.HealThreshold)
            return true;

        return false;
    }

    /// <summary>Cast all missing buffs and heals, respecting the skill delay.</summary>
    public async Task ApplyBuffsAsync(CancellationToken ct)
    {
        if (!_cfg.Enabled) return;
        var local = _ctx.LocalCharacter;
        if (local == null) return;

        // Heal if needed
        if (_cfg.HealSkillId != 0 && local.HpPercent < _cfg.HealThreshold)
            await CastSelfAsync(_cfg.HealSkillId, ct);

        // Apply missing buffs
        foreach (var skillId in _cfg.SelfBuffSkillIds)
        {
            if (local.Buffs.Any(b => b.SkillId == skillId)) continue;
            await CastSelfAsync(skillId, ct);
        }
    }

    private async Task CastSelfAsync(uint skillId, CancellationToken ct)
    {
        // Enforce skill delay
        var elapsed = DateTime.Now - _lastCastAt;
        int remaining = _cfg.SkillDelayMs - (int)elapsed.TotalMilliseconds;
        if (remaining > 0) await Task.Delay(remaining, ct);

        // 0x7070 with self as target
        var local = _ctx.LocalCharacter;
        if (local == null) return;

        var pkt = new PacketWriter()
            .WriteUInt32(skillId)
            .WriteByte(1)                 // target type: unique id
            .WriteUInt32(local.UniqueId)
            .Build(Opcodes.C_SKILL_USE);

        await _proxy.InjectToServerAsync(pkt, ct);
        _lastCastAt = DateTime.Now;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PotionEngine — auto HP/MP potions
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PotionEngine
{
    private readonly PotionConfig _cfg;
    private readonly SroProxy     _proxy;
    private readonly GameContext  _ctx;

    private DateTime _lastHpPotionAt = DateTime.MinValue;
    private DateTime _lastMpPotionAt = DateTime.MinValue;

    public PotionEngine(PotionConfig cfg, SroProxy proxy, GameContext ctx)
    {
        _cfg   = cfg;
        _proxy = proxy;
        _ctx   = ctx;
    }

    /// <summary>
    /// Check HP/MP thresholds and use potions if needed.
    /// Returns true if a potion was used (caller should yield to let it take effect).
    /// </summary>
    public async Task<bool> TickAsync(CancellationToken ct)
    {
        var local = _ctx.LocalCharacter;
        if (local == null) return false;

        bool used = false;

        if (_cfg.AutoUseHpPotion
            && _cfg.HpPotionRefId != 0
            && local.HpPercent < _cfg.HpPotionThreshold
            && (DateTime.Now - _lastHpPotionAt).TotalMilliseconds >= _cfg.CooldownMs)
        {
            await UseItemAsync(_cfg.HpPotionRefId, ct);
            _lastHpPotionAt = DateTime.Now;
            used = true;
        }

        if (_cfg.AutoUseMpPotion
            && _cfg.MpPotionRefId != 0
            && local.MpPercent < _cfg.MpPotionThreshold
            && (DateTime.Now - _lastMpPotionAt).TotalMilliseconds >= _cfg.CooldownMs)
        {
            await UseItemAsync(_cfg.MpPotionRefId, ct);
            _lastMpPotionAt = DateTime.Now;
            used = true;
        }

        return used;
    }

    private async Task UseItemAsync(uint itemRefId, CancellationToken ct)
    {
        // 0x7034 is pickup; item use in SRO is typically a right-click inventory action.
        // The exact opcode for using an item from inventory by slot is 0x7030.
        // We send RefId; the server maps it to the right slot.
        var pkt = new PacketWriter()
            .WriteByte(0)             // action: use item
            .WriteUInt32(itemRefId)
            .Build(0x7030);

        await _proxy.InjectToServerAsync(pkt, ct);
    }
}
