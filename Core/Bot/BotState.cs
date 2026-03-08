using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace InsightBot.Core.Bot;

// ── State machine states ──────────────────────────────────────────────────────

public enum BotState
{
    /// <summary>Bot is stopped / not started.</summary>
    Idle,

    /// <summary>Connecting to gateway or agent server.</summary>
    Connecting,

    /// <summary>Logged into gateway, selecting character.</summary>
    LoggingIn,

    /// <summary>Character is in the world, looking for a target.</summary>
    Hunting,

    /// <summary>Actively attacking a target.</summary>
    Attacking,

    /// <summary>Collecting dropped items on the ground.</summary>
    Looting,

    /// <summary>Casting buffs / heals before or between fights.</summary>
    Buffing,

    /// <summary>Using a potion (HP or MP).</summary>
    UsingPotion,

    /// <summary>Walking back to the designated hunt area.</summary>
    Returning,

    /// <summary>Traveling to or inside town (repair, restock, sell).</summary>
    Town,

    /// <summary>Character is dead — waiting for respawn or manual revive.</summary>
    Dead,

    /// <summary>Paused by the user (keeps connection alive).</summary>
    Paused,

    /// <summary>An unrecoverable error occurred.</summary>
    Error,
}

// ── Observable status model ───────────────────────────────────────────────────

/// <summary>
/// Snapshot of the bot's current operational status.
/// Updated by <see cref="BotEngine"/> and consumed by the UI.
/// </summary>
public sealed class BotStatus
{
    public BotState State       { get; set; } = BotState.Idle;
    public string   StateLabel  => State.ToString();

    public string   Message     { get; set; } = string.Empty;
    public DateTime StartedAt   { get; set; }
    public TimeSpan Uptime      => State == BotState.Idle ? TimeSpan.Zero : DateTime.Now - StartedAt;

    // ── Session counters ──────────────────────────────────────────────────────
    public int   MonstersKilled  { get; set; }
    public int   ItemsPickedUp   { get; set; }
    public ulong GoldCollected   { get; set; }
    public int   PotionsUsed     { get; set; }
    public int   TownTrips       { get; set; }
    public int   DeathCount      { get; set; }

    public void Reset()
    {
        State           = BotState.Idle;
        Message         = string.Empty;
        MonstersKilled  = 0;
        ItemsPickedUp   = 0;
        GoldCollected   = 0;
        PotionsUsed     = 0;
        TownTrips       = 0;
        DeathCount      = 0;
    }
}
