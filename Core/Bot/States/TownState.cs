using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace InsightBot.Core.Bot.States;

/// <summary>
/// TOWN state — handles the full town routine:
///   1. Use return scroll (or walk to nearest teleport)
///   2. Wait to arrive in town
///   3. Restock potions at NPC
///   4. Repair equipment (optional)
///   5. Transition to Returning
///
/// Transitions:
///   → Returning   when town routine is complete
///   → Hunting     if town config is disabled
/// </summary>
public sealed class TownState : IBotState
{
    public BotState StateId => BotState.Town;

    private TownPhase _phase = TownPhase.UseScroll;
    private DateTime  _phaseEnteredAt;
    private const int ScrollWaitMs  = 12_000; // time to load into town after scroll
    private const int NpcActionMs   = 2_000;  // delay between NPC interactions
    private bool _repairDone;
    private bool _restockDone;

    private enum TownPhase
    {
        UseScroll,
        WaitingForTown,
        Restock,
        Repair,
        Done,
    }

    public Task OnEnterAsync(StateContext ctx, CancellationToken ct)
    {
        _phase          = TownPhase.UseScroll;
        _phaseEnteredAt = DateTime.Now;
        _repairDone     = false;
        _restockDone    = false;
        ctx.Status.TownTrips++;
        ctx.Status.Message = "Heading to town…";
        ctx.Emit($"Town trip #{ctx.Status.TownTrips} started.");
        return Task.CompletedTask;
    }

    public async Task<BotState> TickAsync(StateContext ctx, CancellationToken ct)
    {
        if (!ctx.Profile.Town.Enabled)
            return BotState.Hunting;

        switch (_phase)
        {
            case TownPhase.UseScroll:
                await UseReturnScrollAsync(ctx, ct);
                SetPhase(TownPhase.WaitingForTown);
                return BotState.Town;

            case TownPhase.WaitingForTown:
                // Wait fixed duration for the teleport animation + load
                if (Elapsed() >= ScrollWaitMs)
                {
                    ctx.Emit("Arrived in town.");
                    SetPhase(TownPhase.Restock);
                }
                ctx.Status.Message = $"Traveling to town… ({ScrollWaitMs / 1000 - Elapsed() / 1000}s)";
                return BotState.Town;

            case TownPhase.Restock:
                if (!_restockDone)
                {
                    await RestockPotionsAsync(ctx, ct);
                    _restockDone = true;
                    await Task.Delay(NpcActionMs, ct);
                }
                SetPhase(TownPhase.Repair);
                return BotState.Town;

            case TownPhase.Repair:
                // Repair is optional; skip if no NPC configured
                _repairDone = true;
                SetPhase(TownPhase.Done);
                return BotState.Town;

            case TownPhase.Done:
                ctx.Emit("Town routine complete, returning to hunt area.");
                return BotState.Returning;

            default:
                return BotState.Returning;
        }
    }

    public Task OnExitAsync(StateContext ctx, CancellationToken ct) => Task.CompletedTask;

    // ── Town actions ──────────────────────────────────────────────────────────

    private static async Task UseReturnScrollAsync(StateContext ctx, CancellationToken ct)
    {
        uint scrollRefId = ctx.Profile.Town.ReturnScrollRefId;
        if (scrollRefId == 0)
        {
            ctx.Emit("No return scroll configured — assuming manual teleport.");
            return;
        }

        var pkt = new PacketWriter()
            .WriteUInt32(scrollRefId)
            .Build(Opcodes.C_RETURN_SCROLL);

        await ctx.SendAsync(pkt, ct);
        ctx.Emit($"Return scroll used (RefId=0x{scrollRefId:X8}).");
    }

    private static async Task RestockPotionsAsync(StateContext ctx, CancellationToken ct)
    {
        var tcfg = ctx.Profile.Town;
        var pcfg = ctx.Profile.Potions;

        // HP potions
        if (pcfg.HpPotionRefId != 0 && tcfg.MinHpPotionCount > 0)
        {
            ctx.Emit($"Buying HP potions (RefId=0x{pcfg.HpPotionRefId:X8})…");
            await BuyItemAsync(pcfg.HpPotionRefId, (uint)tcfg.MinHpPotionCount * 5, ctx, ct);
            await Task.Delay(500, ct);
        }

        // MP potions
        if (pcfg.MpPotionRefId != 0 && tcfg.MinMpPotionCount > 0)
        {
            ctx.Emit($"Buying MP potions (RefId=0x{pcfg.MpPotionRefId:X8})…");
            await BuyItemAsync(pcfg.MpPotionRefId, (uint)tcfg.MinMpPotionCount * 5, ctx, ct);
            await Task.Delay(500, ct);
        }
    }

    private static Task BuyItemAsync(uint itemRefId, uint quantity,
        StateContext ctx, CancellationToken ct)
    {
        // 0x7931 — NPC buy packet
        var pkt = new PacketWriter()
            .WriteUInt32(itemRefId)
            .WriteUInt32(quantity)
            .Build(Opcodes.C_NPC_BUY);
        return ctx.SendAsync(pkt, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetPhase(TownPhase phase)
    {
        _phase          = phase;
        _phaseEnteredAt = DateTime.Now;
    }

    private int Elapsed() => (int)(DateTime.Now - _phaseEnteredAt).TotalMilliseconds;
}
