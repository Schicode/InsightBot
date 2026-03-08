using System;

namespace InsightBot.Core.Game.Entities;

// ── World Position ────────────────────────────────────────────────────────────

public readonly record struct WorldPosition(float X, float Y, float Z, ushort Region)
{
    public static readonly WorldPosition Zero = new(0, 0, 0, 0);

    /// <summary>Approximate distance in world units (ignores Y axis).</summary>
    public float DistanceTo(WorldPosition other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public override string ToString() => $"({X:F1}, {Z:F1}) R={Region}";
}

// ── Entity type byte as sent by the server in 0x3015 ─────────────────────────

public enum EntityType : byte
{
    None    = 0,
    NPC     = 1,
    Player  = 2,
    Monster = 3,
    Pet     = 4,
    Cos     = 5,  // Character of Skills (summoned)
    GroundItem = 6,
    Portal  = 7,
    Structure = 8,
}

// ── Motion / movement state ───────────────────────────────────────────────────

public enum MotionState : byte
{
    None    = 0,
    Walking = 1,
    Running = 2,
    Sitting = 4,
}

// ── Character race ────────────────────────────────────────────────────────────

public enum Race : byte
{
    Chinese = 0,
    European = 1,
}

// ── Item/Equipment slot indices ───────────────────────────────────────────────

public enum EquipSlot : byte
{
    Head        = 0,
    Shoulder    = 1,
    Body        = 2,
    Hands       = 3,
    Legs        = 4,
    Feet        = 5,
    Weapon      = 6,
    Shield      = 7,
    Earring     = 8,
    Necklace    = 9,
    Ring1       = 10,
    Ring2       = 11,
    Ammo        = 12,
    Cos         = 13,
}

// ── Skill / buff state ────────────────────────────────────────────────────────

public enum BuffType : byte
{
    None     = 0,
    Positive = 1,
    Negative = 2,
}

// ── Chat channel ─────────────────────────────────────────────────────────────

public enum ChatChannel : byte
{
    All       = 0,
    Private   = 1,
    Party     = 2,
    Guild     = 3,
    Global    = 4,
    Notice    = 6,
    Stall     = 7,
    Academy   = 8,
}

// ── HP/MP update target ───────────────────────────────────────────────────────

public enum StatUpdateType : byte
{
    HP = 0,
    MP = 1,
}
