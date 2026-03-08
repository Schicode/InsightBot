using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InsightBot.Core.Configuration;

// ── Connection ────────────────────────────────────────────────────────────────

public sealed class ConnectionConfig
{
    public string GatewayHost { get; set; } = "gwgt1.joymax.com";
    public int GatewayPort { get; set; } = 15779;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Secondary security PIN (6-8 digits).
    /// Required by iSRO if the account has the "PassKey" / second password enabled.
    /// Sent via opcode 0x6117 after successful primary login.
    /// </summary>
    public string SecondaryPin { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;
    public int ServerIndex { get; set; } = 0;
    public byte ShardId { get; set; } = 0;

    /// <summary>Proxy mode: game client connects through InsightBot.</summary>
    public bool UseProxy { get; set; } = true;
    public int ProxyPort { get; set; } = 15779;
}

// ── PK2 ───────────────────────────────────────────────────────────────────────

public sealed class Pk2Config
{
    /// <summary>Full path to Media.pk2.</summary>
    public string Pk2Path { get; set; } = string.Empty;

    /// <summary>Blowfish key.  Default iSRO = "169841".</summary>
    public string Pk2Key { get; set; } = "169841";

    /// <summary>Language/locale to use for display names (e.g. "English", "German").</summary>
    public string Language { get; set; } = "English";
}

// ── Hunt ──────────────────────────────────────────────────────────────────────

public sealed class HuntConfig
{
    public bool Enabled { get; set; } = true;
    public float MaxRange { get; set; } = 300f;
    public bool AttackElite { get; set; } = true;
    public bool AttackUnique { get; set; } = false;
    public float MinHpPercent { get; set; } = 20f;

    public List<uint> TargetRefIds { get; set; } = new();
    public List<uint> IgnoreRefIds { get; set; } = new();
    public List<uint> AttackSkillIds { get; set; } = new();
}

// ── Buffs ─────────────────────────────────────────────────────────────────────

public sealed class BuffConfig
{
    public bool Enabled { get; set; } = true;
    public float HealThreshold { get; set; } = 50f;
    public uint HealSkillId { get; set; } = 0;
    public float MpThreshold { get; set; } = 30f;
    public uint MpSkillId { get; set; } = 0;
    public int SkillDelayMs { get; set; } = 500;

    public List<uint> SelfBuffSkillIds { get; set; } = new();
}

// ── Loot ──────────────────────────────────────────────────────────────────────

public sealed class LootConfig
{
    public bool Enabled { get; set; } = true;
    public bool PickUpGold { get; set; } = true;
    public float MaxLootRange { get; set; } = 150f;
    public int MinItemGrade { get; set; } = 0;

    public List<uint> AllowedRefIds { get; set; } = new();
    public List<uint> IgnoreRefIds { get; set; } = new();
}

// ── Town ──────────────────────────────────────────────────────────────────────

public sealed class TownConfig
{
    public bool Enabled { get; set; } = true;
    public float InventoryFullPercent { get; set; } = 90f;
    public int MinHpPotionCount { get; set; } = 5;
    public int MinMpPotionCount { get; set; } = 5;
    public uint HpPotionRefId { get; set; } = 0;
    public uint MpPotionRefId { get; set; } = 0;
    public uint ReturnScrollRefId { get; set; } = 0;

    public List<WaypointConfig> ReturnPath { get; set; } = new();
}

// ── Waypoint ──────────────────────────────────────────────────────────────────

public sealed class WaypointConfig
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public ushort Region { get; set; }
    public string Label { get; set; } = string.Empty;
}

// ── Potions ───────────────────────────────────────────────────────────────────

public sealed class PotionConfig
{
    public bool AutoUseHpPotion { get; set; } = true;
    public float HpPotionThreshold { get; set; } = 50f;
    public uint HpPotionRefId { get; set; } = 0;
    public bool AutoUseMpPotion { get; set; } = true;
    public float MpPotionThreshold { get; set; } = 40f;
    public uint MpPotionRefId { get; set; } = 0;
    public int CooldownMs { get; set; } = 1000;
}

// ── Root profile ──────────────────────────────────────────────────────────────

public sealed class BotProfile
{
    public string ProfileName { get; set; } = "Default";
    public string Version { get; set; } = "2.0";

    public ConnectionConfig Connection { get; set; } = new();
    public Pk2Config Pk2 { get; set; } = new();
    public HuntConfig Hunt { get; set; } = new();
    public BuffConfig Buffs { get; set; } = new();
    public LootConfig Loot { get; set; } = new();
    public TownConfig Town { get; set; } = new();
    public PotionConfig Potions { get; set; } = new();

    // ── Serialization ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public void SaveTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    public static BotProfile LoadFrom(string path)
        => JsonSerializer.Deserialize<BotProfile>(File.ReadAllText(path), JsonOpts) ?? new();

    public static BotProfile LoadOrCreate(string path)
    {
        if (File.Exists(path)) return LoadFrom(path);
        var p = new BotProfile();
        p.SaveTo(path);
        return p;
    }

    public static string DefaultDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");

    public static string ProfilePath(string name) =>
        Path.Combine(DefaultDirectory, $"{name}.json");

    public static IEnumerable<string> ListProfiles()
    {
        Directory.CreateDirectory(DefaultDirectory);
        return Directory.GetFiles(DefaultDirectory, "*.json")
                        .Select(p => Path.GetFileNameWithoutExtension(p)!);
    }
}