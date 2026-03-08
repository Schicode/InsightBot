using InsightBot.Core.Game;
using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;

namespace InsightBot.Core.Network.Handlers;

/// <summary>
/// Handles 0x3015 — entity spawn.
/// The server sends this whenever an entity enters the local visibility range.
///
/// Packet layout (simplified iSRO 1.188 format):
///   uint32  RefId
///   byte    EntityType (1=NPC 2=Player 3=Monster 4=Pet …)
///   uint32  UniqueId
///   int16   PosX * 10
///   int16   PosZ * 10
///   int16   PosY * 10
///   uint16  Region
///   byte    Angle (0–255)
///   byte    MotionState
///   … type-specific payload …
/// </summary>
public sealed class SpawnHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_ENTITY_SPAWN;

    protected override void Process(PacketReader r, Packet raw)
    {
        uint refId = r.ReadUInt32();
        byte typeId = r.ReadByte();

        var type = (EntityType)typeId;
        uint uid = r.ReadUInt32();
        var pos = r.ReadPosition();
        byte angle = r.ReadByte();
        byte motion = r.ReadByte();

        var worldPos = new WorldPosition(pos.X, pos.Y, pos.Z, pos.Region);

        WorldEntity entity = type switch
        {
            EntityType.Monster => ParseMonster(r, refId, uid, worldPos, angle, motion),
            EntityType.Player  => ParsePlayer(r, refId, uid, worldPos, angle, motion),
            EntityType.NPC     => ParseNpc(r, refId, uid, worldPos, angle),
            EntityType.GroundItem or EntityType.None => ParseGroundItem(r, refId, uid, worldPos),
            _ => new Npc { UniqueId = uid, RefId = refId, Type = type, Position = worldPos }
        };

        GameContext.Instance.AddOrUpdateEntity(entity);
        GameContext.Instance.RaiseEntitySpawned(entity);
    }

    // ── Monster ───────────────────────────────────────────────────────────────

    private static Monster ParseMonster(PacketReader r, uint refId, uint uid,
        WorldPosition pos, byte angle, byte motion)
    {
        // After base fields: HP bar, hostile flag, elite/unique flags
        uint hp    = r.ReadUInt32();
        uint maxHp = r.ReadUInt32();

        byte flags  = r.ReadByte();
        bool hostile      = (flags & 0x01) != 0;
        bool isElite      = (flags & 0x02) != 0;
        bool isUnique     = (flags & 0x04) != 0;

        return new Monster
        {
            UniqueId  = uid,
            RefId     = refId,
            Type      = EntityType.Monster,
            Position  = pos,
            Angle     = angle,
            Motion    = (MotionState)motion,
            HP        = hp,
            MaxHP     = maxHp,
            IsHostile = hostile,
            IsElite   = isElite,
            IsUniqueMonster = isUnique,
        };
    }

    // ── Player ────────────────────────────────────────────────────────────────

    private static OtherPlayer ParsePlayer(PacketReader r, uint refId, uint uid,
        WorldPosition pos, byte angle, byte motion)
    {
        // Name is sent as a length-prefixed ASCII string
        string name  = r.ReadAsciiString();
        byte   level = r.ReadByte();
        byte   race  = r.ReadByte();

        uint hp    = r.ReadUInt32();
        uint maxHp = r.ReadUInt32();
        uint mp    = r.ReadUInt32();
        uint maxMp = r.ReadUInt32();

        // Guild info
        bool   inGuild   = r.ReadBool();
        string guildName = inGuild ? r.ReadAsciiString() : string.Empty;

        return new OtherPlayer
        {
            UniqueId  = uid,
            RefId     = refId,
            Type      = EntityType.Player,
            Position  = pos,
            Angle     = angle,
            Motion    = (MotionState)motion,
            Name      = name,
            Level     = level,
            Race      = (Race)race,
            HP        = hp,
            MaxHP     = maxHp,
            MP        = mp,
            MaxMP     = maxMp,
            IsInGuild = inGuild,
            GuildName = guildName,
        };
    }

    // ── NPC ───────────────────────────────────────────────────────────────────

    private static Npc ParseNpc(PacketReader r, uint refId, uint uid,
        WorldPosition pos, byte angle)
    {
        // NPC payload is minimal — just optional name
        bool hasName = r.HasData && r.Remaining >= 2;
        string name = hasName ? r.ReadAsciiString() : string.Empty;

        return new Npc
        {
            UniqueId = uid,
            RefId    = refId,
            Type     = EntityType.NPC,
            Position = pos,
            Angle    = angle,
            Name     = name,
        };
    }

    // ── Ground item ───────────────────────────────────────────────────────────

    private static GroundItem ParseGroundItem(PacketReader r, uint refId, uint uid,
        WorldPosition pos)
    {
        uint  itemRefId  = r.ReadUInt32();
        bool  isGold     = r.ReadBool();
        uint  goldAmount = isGold ? r.ReadUInt32() : 0;
        uint  ownerId    = r.ReadUInt32(); // 0 = anyone can pick up

        return new GroundItem
        {
            UniqueId   = uid,
            RefId      = refId,
            Type       = EntityType.GroundItem,
            Position   = pos,
            ItemRefId  = itemRefId,
            IsGold     = isGold,
            GoldAmount = goldAmount,
            OwnerId    = ownerId,
        };
    }
}
