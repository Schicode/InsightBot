using System.Collections.Generic;

namespace InsightBot.Core.Game.Entities;

// ── Base entity (anything with a UniqueID in the world) ───────────────────────

public abstract class WorldEntity
{
    public uint   UniqueId  { get; init; }
    public uint   RefId     { get; init; }    // Media/object reference ID
    public EntityType Type  { get; init; }
    public WorldPosition Position { get; set; }
    public MotionState   Motion   { get; set; }
    public byte Angle { get; set; }           // 0–255 mapped to 0°–360°
}

// ── Local player character ────────────────────────────────────────────────────

public sealed class Character : WorldEntity
{
    public string Name     { get; set; } = string.Empty;
    public Race   Race     { get; set; }
    public byte   Level    { get; set; }
    public byte   MaxLevel { get; set; }

    public uint HP    { get; set; }
    public uint MaxHP { get; set; }
    public uint MP    { get; set; }
    public uint MaxMP { get; set; }

    public ulong Exp        { get; set; }
    public ulong MaxExp     { get; set; }
    public ulong SkillExp   { get; set; }
    public ulong MaxSkillExp { get; set; }

    public uint Gold { get; set; }

    public float HpPercent => MaxHP == 0 ? 0f : HP / (float)MaxHP * 100f;
    public float MpPercent => MaxMP == 0 ? 0f : MP / (float)MaxMP * 100f;

    public List<ActiveBuff> Buffs { get; } = new();

    public bool IsDead  => HP == 0;
    public bool IsAlive => HP > 0;
}

// ── Other player in the world ─────────────────────────────────────────────────

public sealed class OtherPlayer : WorldEntity
{
    public string Name  { get; set; } = string.Empty;
    public byte   Level { get; set; }
    public Race   Race  { get; set; }

    public uint HP    { get; set; }
    public uint MaxHP { get; set; }
    public uint MP    { get; set; }
    public uint MaxMP { get; set; }

    public bool IsInParty  { get; set; }
    public bool IsInGuild  { get; set; }
    public string GuildName { get; set; } = string.Empty;
}

// ── Monster / NPC ─────────────────────────────────────────────────────────────

public sealed class Monster : WorldEntity
{
    public string Name     { get; set; } = string.Empty;
    public byte   Level    { get; set; }

    public uint HP    { get; set; }
    public uint MaxHP { get; set; }

    public bool IsHostile    { get; set; }
    public bool IsTargeted   { get; set; }  // Currently targeted by our character
    public bool IsElite      { get; set; }
    public bool IsUniqueMonster { get; set; }

    public float HpPercent => MaxHP == 0 ? 0f : HP / (float)MaxHP * 100f;
    public bool  IsDead    => HP == 0;

    /// <summary>True if the monster is aggroed onto us specifically.</summary>
    public bool IsAggroedOnUs { get; set; }
}

// ── NPC (shop keeper, quest giver, teleport gate, …) ─────────────────────────

public sealed class Npc : WorldEntity
{
    public string Name     { get; set; } = string.Empty;
    public bool   HasShop  { get; set; }
    public bool   HasQuest { get; set; }
}

// ── Dropped item on the ground ────────────────────────────────────────────────

public sealed class GroundItem : WorldEntity
{
    public uint   ItemRefId   { get; set; }
    public string Name        { get; set; } = string.Empty;
    public uint   OwnerId     { get; set; }  // UniqueId of the player who gets exclusive rights
    public bool   IsGold      { get; set; }
    public uint   GoldAmount  { get; set; }

    /// <summary>True if the local character can freely pick this up.</summary>
    public bool CanPickUp(uint localUniqueId) =>
        OwnerId == 0 || OwnerId == localUniqueId;
}

// ── Active buff / debuff ──────────────────────────────────────────────────────

public sealed class ActiveBuff
{
    public uint     SkillId    { get; init; }
    public string   Name       { get; set; } = string.Empty;
    public BuffType BuffType   { get; set; }
    public int      Duration   { get; set; }    // Remaining ticks (server units)
    public bool     IsStackable { get; set; }
}
