namespace InsightBot.Core.Network;

/// <summary>
/// Silkroad Online packet opcodes (iSRO protocol).
/// C_ = Client → Server   S_ = Server → Client
/// </summary>
public static class Opcodes
{
    // ── Handshake / Security ──────────────────────────────────────────────────

    public const ushort C_HANDSHAKE = 0x5000;
    public const ushort S_HANDSHAKE = 0x5000;
    public const ushort S_HANDSHAKE_ACK = 0x9000;

    /// <summary>Aliases used by older code.</summary>
    public const ushort HANDSHAKE = 0x5000;
    public const ushort HANDSHAKE_RESPONSE = 0x9000;
    public const ushort PING = 0x2002;

    // ── Patch / version check ─────────────────────────────────────────────────

    public const ushort C_PATCH_REQUEST = 0x6100;
    public const ushort S_PATCH_RESPONSE = 0xA100;

    // ── Gateway → Login ───────────────────────────────────────────────────────

    public const ushort C_LOGIN_REQUEST = 0x6102;
    public const ushort S_LOGIN_RESPONSE = 0xA102;
    public const ushort S_SERVER_LIST = 0xA101;
    public const ushort C_SELECT_SERVER = 0x6101;

    // ── Secondary PIN (PassKey) ───────────────────────────────────────────────

    public const ushort S_PASSKEY_REQUEST = 0xA120;
    public const ushort C_PASSKEY_RESPONSE = 0x6117;
    public const ushort S_PASSKEY_RESULT = 0xA121;

    // ── Agent handoff ─────────────────────────────────────────────────────────

    public const ushort S_AGENT_AUTH = 0xA103;
    public const ushort S_AGENT_HANDOFF = 0xA103;   // alias
    public const ushort C_AGENT_AUTH = 0x6103;

    // ── Character select / world enter ────────────────────────────────────────

    public const ushort S_CHAR_LIST = 0xB007;
    public const ushort C_CHAR_SELECT = 0x7007;
    public const ushort S_CHAR_DATA = 0x3013;
    public const ushort S_WORLD_ENTER = 0x34A5;
    public const ushort S_RESPAWN = 0x34A5;   // alias (world re-enter after death)

    // ── HP / MP updates ───────────────────────────────────────────────────────

    public const ushort S_HP_UPDATE = 0xB070;
    public const ushort S_STAT_UPDATE = 0xB072;

    // ── Movement ──────────────────────────────────────────────────────────────

    public const ushort C_MOVE = 0x7021;
    public const ushort C_MOVEMENT = 0x7021;   // alias
    public const ushort S_MOVE_RESPONSE = 0x3020;
    public const ushort S_MOVEMENT = 0x3021;
    public const ushort S_ENTITY_MOVE = 0x3021;   // alias
    public const ushort S_MOVEMENT_STOP = 0x3022;
    public const ushort S_ENTITY_STOP = 0x3022;   // alias

    // ── Spawn / despawn ───────────────────────────────────────────────────────

    public const ushort S_ENTITY_SPAWN = 0x3015;
    public const ushort S_ENTITY_DESPAWN = 0x3016;
    public const ushort S_GROUP_SPAWN = 0x3017;
    public const ushort S_GROUP_DESPAWN = 0x3018;

    // ── Skills / combat ───────────────────────────────────────────────────────

    public const ushort C_SKILL_USE = 0x7074;
    public const ushort C_ATTACK = 0x7074;   // alias
    public const ushort S_SKILL_RESPONSE = 0xB074;
    public const ushort S_SKILL_HIT = 0x30D0;
    public const ushort S_ENTITY_DEAD = 0x30FB;
    public const ushort S_ENTITY_DEATH = 0x30FB;   // alias
    public const ushort S_SKILL_BUFF_ADD = 0x30C0;
    public const ushort S_BUFF_ADD = 0x30C0;   // alias
    public const ushort S_SKILL_BUFF_END = 0x30C2;
    public const ushort S_BUFF_REMOVE = 0x30C2;   // alias

    // ── Items / loot ──────────────────────────────────────────────────────────

    public const ushort S_ITEM_SPAWN = 0x3038;
    public const ushort C_PICK_ITEM = 0x7034;
    public const ushort C_ITEM_PICKUP = 0x7034;   // alias
    public const ushort S_INVENTORY_DATA = 0xB034;
    public const ushort S_ITEM_PICK_RESULT = 0xB035;
    public const ushort S_ITEM_PICKUP = 0xB035;   // alias
    public const ushort S_INVENTORY_UPDATE = 0xB036;

    // ── NPC / shop ────────────────────────────────────────────────────────────

    public const ushort C_NPC_BUY = 0x7035;
    public const ushort C_RETURN_SCROLL = 0x704C;

    // ── Chat ──────────────────────────────────────────────────────────────────

    public const ushort C_CHAT = 0x7025;
    public const ushort S_CHAT = 0x3026;

    // ── Misc ──────────────────────────────────────────────────────────────────

    public const ushort C_HEARTBEAT = 0x2002;
    public const ushort S_DISCONNECT = 0x2001;
}