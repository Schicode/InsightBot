using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot.States;

/// <summary>
/// LOOTING state — collect all eligible ground items near the kill spot.
///
/// Transitions:
///   → Hunting   when no more items remain in range
///   → Town      if inventory full after looting
/// </summary>
public sealed class LootingState : IBotState
{
    public BotState StateId => BotState.Looting;

    private const int PickupWaitMs  = 1200; // wait after sending pickup before next
    private const int MoveWaitMs    = 900;  // wait after movement packet
    private const int MaxLootPasses = 10;   // max items per loot session

    private int      _passCount;
    private DateTime _lastPickupAt = DateTime.MinValue;

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        _passCount    = 0;
        _lastPickupAt = DateTime.MinValue;
        ctx.Status.Message = "Looting…";
        return Task.CompletedTask;
    }

    public async Task<BotState> TickAsync(StateContext ctx, CancellationToken ct)
    {
        if (!ctx.Profile.Loot.Enabled)
            return BotState.Hunting;

        // Enforce pickup cooldown (server needs time to process)
        if ((DateTime.Now - _lastPickupAt).TotalMilliseconds < PickupWaitMs)
            return BotState.Looting;

        if (_passCount >= MaxLootPasses)
        {
            ctx.Emit($"Loot pass limit reached ({MaxLootPasses}), resuming hunt.");
            return BotState.Hunting;
        }

        var local = ctx.Game.LocalCharacter;
        if (local == null) return BotState.Hunting;

        // Find next eligible item
        var item = FindNextItem(ctx, local.UniqueId);

        if (item == null)
        {
            ctx.Status.Message = "No more items.";
            return BotState.Hunting;
        }

        float dist = local.Position.DistanceTo(item.Position);

        // Walk to item if needed
        if (dist > 55f)
        {
            await WalkToAsync(item.Position, ctx, ct);
            ctx.Status.Message = $"Walking to item ({dist:F0} wu)…";
            await Task.Delay(MoveWaitMs, ct);
            return BotState.Looting; // re-evaluate after move
        }

        // Send pickup
        var pickup = new PacketWriter()
            .WriteUInt32(item.UniqueId)
            .Build(Opcodes.C_ITEM_PICKUP);

        await ctx.SendAsync(pickup, ct);

        string label = item.IsGold
            ? $"{item.GoldAmount} gold"
            : $"item 0x{item.ItemRefId:X8}";

        ctx.Emit($"Picking up {label}");
        ctx.Status.ItemsPickedUp++;
        if (item.IsGold) ctx.Status.GoldCollected += item.GoldAmount;

        _lastPickupAt = DateTime.Now;
        _passCount++;

        return BotState.Looting;
    }

    public Task OnExitAsync(StateContext ctx, CancellationToken ct) => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GroundItem? FindNextItem(StateContext ctx, uint localUid)
    {
        var cfg   = ctx.Profile.Loot;
        var local = ctx.Game.LocalCharacter;
        if (local == null) return null;

        return ctx.Game.GroundItems
            .Where(i =>
                i.CanPickUp(localUid) &&
                local.Position.DistanceTo(i.Position) <= cfg.MaxLootRange &&
                IsEligible(i, cfg)
            )
            .OrderBy(i => local.Position.DistanceTo(i.Position))
            .FirstOrDefault();
    }

    private static bool IsEligible(GroundItem item, Configuration.LootConfig cfg)
    {
        if (item.IsGold) return cfg.PickUpGold;
        if (cfg.IgnoreRefIds.Contains(item.ItemRefId)) return false;
        if (cfg.AllowedRefIds.Count > 0)
            return cfg.AllowedRefIds.Contains(item.ItemRefId);
        return true;
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
