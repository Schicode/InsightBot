using InsightBot.Core.Game;
using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;

namespace InsightBot.Core.Network.Handlers;

// ── 0x3016 — Entity despawn ───────────────────────────────────────────────────

/// <summary>
/// Handles 0x3016 — entity leaves visibility range.
///
/// Packet layout:
///   uint32  UniqueId
/// </summary>
public sealed class DespawnHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_ENTITY_DESPAWN;

    protected override void Process(PacketReader r, Packet raw)
    {
        uint uid = r.ReadUInt32();
        GameContext.Instance.RemoveEntity(uid);
        GameContext.Instance.RaiseEntityDespawned(uid);
    }
}

// ── 0x3018 — Entity death ─────────────────────────────────────────────────────

/// <summary>
/// Handles 0x3018 — entity killed/died.
///
/// Packet layout:
///   uint32  UniqueId of the dead entity
///   uint32  UniqueId of the killer (0 = environment / self)
/// </summary>
public sealed class EntityDeathHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_ENTITY_DEATH;

    protected override void Process(PacketReader r, Packet raw)
    {
        uint deadUid   = r.ReadUInt32();
        uint killerUid = r.ReadUInt32();

        // Zero out HP on the entity in context
        if (GameContext.Instance.TryGetEntity(deadUid, out var entity))
        {
            if (entity is Monster m) m.HP = 0;
            if (entity is OtherPlayer p) p.HP = 0;
        }

        // If local character died
        var local = GameContext.Instance.LocalCharacter;
        if (local != null && deadUid == local.UniqueId)
            local.HP = 0;

        // Clear target if we just killed it
        if (GameContext.Instance.CurrentTargetUid == deadUid)
            GameContext.Instance.ClearTarget();

        GameContext.Instance.RaiseEntityDied(deadUid);
    }
}

// ── 0xB021 / 0x7021 — Movement ───────────────────────────────────────────────

/// <summary>
/// Handles 0xB021 — entity movement update from server.
///
/// Packet layout:
///   uint32  UniqueId
///   byte    MovementType (0=to cell, 1=to entity)
///   if type==0:
///     int16   DestX * 10
///     int16   DestZ * 10
///     int16   DestY * 10
///     uint16  DestRegion
///   if type==1:
///     uint32  TargetUniqueId
///   byte    MotionState
/// </summary>
public sealed class MovementHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_MOVEMENT;

    protected override void Process(PacketReader r, Packet raw)
    {
        uint uid          = r.ReadUInt32();
        byte movementType = r.ReadByte();

        WorldPosition dest = WorldPosition.Zero;
        if (movementType == 0)
        {
            dest = new WorldPosition(
                X:      r.ReadInt16() / 10f,
                Y:      r.ReadInt16() / 10f,
                Z:      r.ReadInt16() / 10f,
                Region: r.ReadUInt16()
            );
        }
        else
        {
            // Moving toward another entity — skip target uid
            r.Skip(4);
        }

        byte motionByte = r.ReadByte();
        var motion = (MotionState)motionByte;

        // Update local character position
        var local = GameContext.Instance.LocalCharacter;
        if (local != null && uid == local.UniqueId)
        {
            if (movementType == 0) local.Position = dest;
            local.Motion = motion;
            GameContext.Instance.RaiseCharacterUpdated();
            return;
        }

        // Update other entities
        if (GameContext.Instance.TryGetEntity(uid, out var entity) && entity != null)
        {
            if (movementType == 0) entity.Position = dest;
            entity.Motion = motion;
        }
    }
}

// ── 0xB022 — Movement stop ────────────────────────────────────────────────────

/// <summary>
/// Handles 0xB022 — entity stopped moving.
///
/// Packet layout:
///   uint32  UniqueId
///   int16   StopX * 10
///   int16   StopZ * 10
///   int16   StopY * 10
///   uint16  Region
///   byte    Angle
/// </summary>
public sealed class MovementStopHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_MOVEMENT_STOP;

    protected override void Process(PacketReader r, Packet raw)
    {
        uint uid = r.ReadUInt32();
        var  pos = r.ReadPosition();
        byte angle = r.ReadByte();

        var stopPos = new WorldPosition(pos.X, pos.Y, pos.Z, pos.Region);

        var local = GameContext.Instance.LocalCharacter;
        if (local != null && uid == local.UniqueId)
        {
            local.Position = stopPos;
            local.Motion   = MotionState.None;
            local.Angle    = angle;
            GameContext.Instance.RaiseCharacterUpdated();
            return;
        }

        if (GameContext.Instance.TryGetEntity(uid, out var entity) && entity != null)
        {
            entity.Position = stopPos;
            entity.Motion   = MotionState.None;
            entity.Angle    = angle;
        }
    }
}
