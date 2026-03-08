using InsightBot.Core.Game;
using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;
using System.Collections.Generic;

namespace InsightBot.Core.Network.Handlers;

// ── 0xB070 — HP/MP bar update ─────────────────────────────────────────────────

/// <summary>
/// Handles 0xB070 — HP or MP update for any entity.
///
/// Packet layout:
///   uint32  UniqueId
///   byte    Type  (0=HP, 1=MP)
///   uint32  NewValue
///   uint32  MaxValue  (sometimes 0 — keep existing max)
/// </summary>
public sealed class HpMpUpdateHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_HP_UPDATE;

    protected override void Process(PacketReader r, Packet raw)
    {
        uint uid      = r.ReadUInt32();
        byte type     = r.ReadByte();
        uint newValue = r.ReadUInt32();
        uint maxValue = r.HasData ? r.ReadUInt32() : 0;

        var local = GameContext.Instance.LocalCharacter;

        if (local != null && uid == local.UniqueId)
        {
            if (type == 0) // HP
            {
                local.HP = newValue;
                if (maxValue > 0) local.MaxHP = maxValue;
            }
            else // MP
            {
                local.MP = newValue;
                if (maxValue > 0) local.MaxMP = maxValue;
            }
            GameContext.Instance.RaiseCharacterUpdated();
        }
        else if (GameContext.Instance.TryGetEntity(uid, out var entity))
        {
            switch (entity)
            {
                case Monster m:
                    if (type == 0) { m.HP = newValue; if (maxValue > 0) m.MaxHP = maxValue; }
                    break;
                case OtherPlayer p:
                    if (type == 0) { p.HP = newValue; if (maxValue > 0) p.MaxHP = maxValue; }
                    else           { p.MP = newValue; if (maxValue > 0) p.MaxMP = maxValue; }
                    break;
            }
        }

        GameContext.Instance.RaiseHpMpUpdated(uid, newValue, maxValue);
    }
}

// ── 0x3013 — Full character data on world enter ───────────────────────────────

/// <summary>
/// Handles 0x3013 — full character data packet sent after successful login.
/// This is the main "you have entered the world" packet.
///
/// Packet layout (iSRO 1.188):
///   uint32  UniqueId
///   uint32  RefId       (character model reference)
///   byte    Scale
///   byte    Level
///   byte    MaxLevel
///   uint64  Exp
///   uint64  MaxExp
///   uint32  Gold
///   uint32  SkillPoints
///   uint32  HP
///   uint32  MaxHP
///   uint32  MP
///   uint32  MaxMP
///   byte    Race        (0=Chinese, 1=European)
///   string  Name        (ASCII, length-prefixed)
///   int16   PosX * 10
///   int16   PosZ * 10
///   int16   PosY * 10
///   uint16  Region
/// </summary>
public sealed class CharacterDataHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_CHAR_DATA;

    protected override void Process(PacketReader r, Packet raw)
    {
        uint   uid      = r.ReadUInt32();
        uint   refId    = r.ReadUInt32();
        byte   scale    = r.ReadByte();
        byte   level    = r.ReadByte();
        byte   maxLevel = r.ReadByte();
        ulong  exp      = r.ReadUInt64();
        ulong  maxExp   = r.ReadUInt64();
        uint   gold     = r.ReadUInt32();
        uint   skillPts = r.ReadUInt32();
        uint   hp       = r.ReadUInt32();
        uint   maxHp    = r.ReadUInt32();
        uint   mp       = r.ReadUInt32();
        uint   maxMp    = r.ReadUInt32();
        byte   race     = r.ReadByte();
        string name     = r.ReadAsciiString();
        var    pos      = r.ReadPosition();

        var character = new Character
        {
            UniqueId = uid,
            RefId    = refId,
            Type     = EntityType.Player,
            Name     = name,
            Level    = level,
            MaxLevel = maxLevel,
            Race     = (Race)race,
            Exp      = exp,
            MaxExp   = maxExp,
            Gold     = gold,
            HP       = hp,
            MaxHP    = maxHp,
            MP       = mp,
            MaxMP    = maxMp,
            Position = new WorldPosition(pos.X, pos.Y, pos.Z, pos.Region),
        };

        GameContext.Instance.SetLocalCharacter(character);
        GameContext.Instance.RaiseCharacterUpdated();

        Console.WriteLine($"[CharData] Logged in as '{name}' Lv{level} HP={hp}/{maxHp} MP={mp}/{maxMp} @ {character.Position}");
    }
}

// ── 0x30A2 — Buff added ───────────────────────────────────────────────────────

/// <summary>
/// Handles 0x30A2 — buff/debuff applied to an entity.
///
/// Packet layout:
///   uint32  UniqueId (target)
///   uint32  SkillId
///   uint32  Duration (ticks)
///   byte    BuffType (1=positive, 2=negative)
/// </summary>
public sealed class BuffHandler : IPacketHandler
{
    public IReadOnlyList<ushort> HandledOpcodes => [Opcodes.S_BUFF_ADD, Opcodes.S_BUFF_REMOVE];

    public void Handle(Packet packet)
    {
        try
        {
            var r = new PacketReader(packet);
            if (packet.Opcode == Opcodes.S_BUFF_ADD)
                HandleAdd(r);
            else
                HandleRemove(r);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BuffHandler] Error: {ex.Message}");
        }
    }

    private static void HandleAdd(PacketReader r)
    {
        uint uid      = r.ReadUInt32();
        uint skillId  = r.ReadUInt32();
        uint duration = r.ReadUInt32();
        byte buffType = r.ReadByte();

        var buff = new ActiveBuff
        {
            SkillId  = skillId,
            BuffType = (BuffType)buffType,
            Duration = (int)duration,
        };

        var local = GameContext.Instance.LocalCharacter;
        if (local != null && uid == local.UniqueId)
        {
            // Remove existing same-skill buff first (no duplicates)
            local.Buffs.RemoveAll(b => b.SkillId == skillId);
            local.Buffs.Add(buff);
            GameContext.Instance.RaiseCharacterUpdated();
        }
    }

    private static void HandleRemove(PacketReader r)
    {
        uint uid     = r.ReadUInt32();
        uint skillId = r.ReadUInt32();

        var local = GameContext.Instance.LocalCharacter;
        if (local != null && uid == local.UniqueId)
        {
            local.Buffs.RemoveAll(b => b.SkillId == skillId);
            GameContext.Instance.RaiseCharacterUpdated();
        }
    }
}
