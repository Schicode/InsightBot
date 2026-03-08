using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InsightBot.Core.Pk2;

// ── Data records ──────────────────────────────────────────────────────────────

public enum CharacterKind { Unknown, Player, NPC, Monster }

public sealed record CharacterData(
    uint RefId,
    string InternalName,
    string NameKey,
    CharacterKind Kind,
    byte Level
);

public sealed record ItemData(
    uint RefId,
    string InternalName,
    string NameKey,
    byte TypeId1,
    byte TypeId2,
    byte TypeId3,
    byte TypeId4
);

public sealed record SkillData(
    uint RefId,
    string InternalName,
    string NameKey,
    byte Level
);

// ── Connection info loaded from PK2 ──────────────────────────────────────────

public sealed class Pk2ConnectionInfo
{
    public string Locale { get; set; } = string.Empty;
    public int ContentId { get; set; } = 0;

    /// <summary>List of gateway addresses: host + port pairs.</summary>
    public List<(string Host, int Port)> Gateways { get; set; } = new();

    /// <summary>Raw division names (server shards).</summary>
    public List<string> Divisions { get; set; } = new();
}

// ── GameDataLoader ────────────────────────────────────────────────────────────

/// <summary>
/// Parses SRO textdata files directly from an open Pk2Reader.
///
/// File locations (case-insensitive, all under server_dep\silkroad\textdata\):
///   textuisystem.txt           → string table: col[1]=key  col[2]=text
///   characterdata_*.txt        → monsters/NPCs/players
///   itemdata_*.txt             → items
///   skilldata.txt              → skills
///
/// Root-level files parsed for connection config:
///   DIVISIONINFO.TXT           → shard / division list
///   GATEPORT.TXT               → gateway port
///   type.txt                   → locale / content-id
///
/// Tab-separated row layout (0-indexed columns):
///   0  = row index (unused)
///   1  = RefId (ServerIndex, uint)
///   2  = InternalName  (ObjName, e.g. "MOB_CH_WOLF_01")
///   3  = ObjChar
///   4  = ObjChar2
///   5  = TypeID1
///   6  = TypeID2
///   7  = TypeID3
///   8  = TypeID4
///   …  = many optional columns …
///   NameKey = first column in 5–25 whose value starts with one of:
///             STR_, UIIT_, SKILL_, MOB_, ITEM_, NPC_
/// </summary>
public sealed class GameDataLoader
{
    // ── Public data stores ────────────────────────────────────────────────────

    public Dictionary<uint, CharacterData> Characters { get; } = new();
    public Dictionary<uint, ItemData> Items { get; } = new();
    public Dictionary<uint, SkillData> Skills { get; } = new();

    /// <summary>String table: key → localized display text.</summary>
    public Dictionary<string, string> Strings { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Connection information auto-loaded from the PK2.</summary>
    public Pk2ConnectionInfo ConnectionInfo { get; } = new();

    // ── Entry point ───────────────────────────────────────────────────────────

    public static GameDataLoader Load(Pk2Reader pk2, IProgress<string>? progress = null)
    {
        var loader = new GameDataLoader();
        loader.LoadConnectionInfo(pk2, progress);
        loader.LoadStringTable(pk2, progress);
        loader.LoadAllTextdata(pk2, progress);
        return loader;
    }

    // ── Connection info ───────────────────────────────────────────────────────

    private void LoadConnectionInfo(Pk2Reader pk2, IProgress<string>? progress)
    {
        progress?.Report("Reading connection files from PK2…");

        // type.txt → locale + content-id
        string? typeTxt = pk2.GetText("type.txt") ?? pk2.FindText("type.txt");
        if (typeTxt is not null)
        {
            var lines = typeTxt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("Locale", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = t.Split('=', ':');
                    if (parts.Length >= 2 && int.TryParse(parts[^1].Trim(), out int loc))
                        ConnectionInfo.ContentId = loc;
                }
            }
            // First non-comment line is often the locale string
            string? firstLine = lines.FirstOrDefault(l => !l.StartsWith("/"));
            if (firstLine is not null)
                ConnectionInfo.Locale = firstLine.Split('\t', ' ', '=')[0].Trim();
        }

        // GATEPORT.TXT → default port
        int gatePort = 15779;
        string? gateTxt = pk2.GetText("GATEPORT.TXT") ?? pk2.FindText("GATEPORT.TXT");
        if (gateTxt is not null && int.TryParse(gateTxt.Trim(), out int port))
            gatePort = port;

        // DIVISIONINFO.TXT → host/division list
        string? divTxt = pk2.GetText("DIVISIONINFO.TXT") ?? pk2.FindText("DIVISIONINFO.TXT");
        if (divTxt is not null)
        {
            var lines = divTxt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // Format: first line = number of divisions
            // Then per division: DivisionName\nIPCount\nIP1\nIP2…
            int i = 0;
            if (i < lines.Length && int.TryParse(lines[i].Trim(), out int divCount))
            {
                i++;
                for (int d = 0; d < divCount && i < lines.Length; d++)
                {
                    string divName = lines[i++].Trim();
                    ConnectionInfo.Divisions.Add(divName);
                    if (i < lines.Length && int.TryParse(lines[i].Trim(), out int ipCount))
                    {
                        i++;
                        for (int ip = 0; ip < ipCount && i < lines.Length; ip++)
                        {
                            string host = lines[i++].Trim();
                            if (host.Length > 0)
                                ConnectionInfo.Gateways.Add((host, gatePort));
                        }
                    }
                }
            }
        }

        progress?.Report($"  Gateways: {ConnectionInfo.Gateways.Count}  Divisions: {ConnectionInfo.Divisions.Count}");
    }

    // ── String table ──────────────────────────────────────────────────────────

    private void LoadStringTable(Pk2Reader pk2, IProgress<string>? progress)
    {
        progress?.Report("Loading textuisystem.txt…");

        // Try known path, then fallback to global search
        string? text =
            pk2.GetText(@"server_dep\silkroad\textdata\textuisystem.txt") ??
            pk2.FindText("textuisystem.txt");

        if (text is null)
        {
            progress?.Report("  WARNING: textuisystem.txt not found in PK2");
            return;
        }

        int loaded = 0;
        foreach (var row in ParseTsv(text))
        {
            if (row.Length < 3) continue;
            string k = row[1].Trim();
            string v = row[2].Trim();
            if (k.Length > 0 && v.Length > 0 && Strings.TryAdd(k, v))
                loaded++;
        }
        progress?.Report($"  Strings: {loaded:N0}");
    }

    // ── Textdata ──────────────────────────────────────────────────────────────

    private void LoadAllTextdata(Pk2Reader pk2, IProgress<string>? progress)
    {
        // Try the canonical path and common variants
        Pk2Folder? textdata =
            pk2.ResolveFolder(@"server_dep\silkroad\textdata") ??
            pk2.ResolveFolder(@"Server_Dep\Silkroad\textdata") ??
            pk2.ResolveFolder("textdata");

        if (textdata is null)
        {
            progress?.Report("  WARNING: textdata folder not found – check PK2 path/key");
            return;
        }

        foreach (var (name, file) in textdata.Files)
        {
            string lo = name.ToLowerInvariant();
            if (lo.StartsWith("characterdata") && lo.EndsWith(".txt"))
                ProcessCharacterData(pk2, file, progress);
            else if (lo.StartsWith("itemdata") && lo.EndsWith(".txt"))
                ProcessItemData(pk2, file, progress);
            else if (lo == "skilldata.txt")
                ProcessSkillData(pk2, file, progress);
        }

        progress?.Report($"  Totals → Characters: {Characters.Count:N0}  Items: {Items.Count:N0}  Skills: {Skills.Count:N0}");
    }

    // ── characterdata ─────────────────────────────────────────────────────────

    private void ProcessCharacterData(Pk2Reader pk2, Pk2File file, IProgress<string>? progress)
    {
        // *** THIS was broken in the previous version — ReadFileText was a placeholder ***
        string? text = pk2.ReadFileText(file);
        if (text is null) return;

        int count = 0;
        foreach (var row in ParseTsv(text))
        {
            if (row.Length < 9) continue;
            if (!uint.TryParse(row[1].Trim(), out uint refId) || refId == 0) continue;

            string internalName = row[2].Trim();
            string nameKey = ScanForStrKey(row, 5, 25);
            byte t1 = ParseU8(row, 5);
            byte t2 = ParseU8(row, 6);

            // TypeID1 / TypeID2 encoding for iSRO:
            //   t1=1  t2=1  → NPC
            //   t1=1  t2=2  → Player (character creation)
            //   t1=1  t2=4  → Monster (attackable)
            //   t1=1  t2=5  → Unique boss
            CharacterKind kind = (t1, t2) switch
            {
                (1, 1) => CharacterKind.NPC,
                (1, 2) => CharacterKind.Player,
                (1, 4) => CharacterKind.Monster,
                (1, 5) => CharacterKind.Monster,
                (1, _) when t2 >= 3 => CharacterKind.Monster,
                _ => CharacterKind.Unknown
            };

            // Level is usually in column 57 for characterdata (varies by version)
            byte level = ParseU8(row, Math.Min(57, row.Length - 1));

            Characters.TryAdd(refId, new CharacterData(refId, internalName, nameKey, kind, level));
            count++;
        }
        progress?.Report($"    {file.Name}: {count}");
    }

    // ── itemdata ──────────────────────────────────────────────────────────────

    private void ProcessItemData(Pk2Reader pk2, Pk2File file, IProgress<string>? progress)
    {
        string? text = pk2.ReadFileText(file);
        if (text is null) return;

        int count = 0;
        foreach (var row in ParseTsv(text))
        {
            if (row.Length < 9) continue;
            if (!uint.TryParse(row[1].Trim(), out uint refId) || refId == 0) continue;

            Items.TryAdd(refId, new ItemData(
                refId,
                row[2].Trim(),
                ScanForStrKey(row, 5, 25),
                ParseU8(row, 5),
                ParseU8(row, 6),
                ParseU8(row, 7),
                ParseU8(row, 8)
            ));
            count++;
        }
        progress?.Report($"    {file.Name}: {count}");
    }

    // ── skilldata ─────────────────────────────────────────────────────────────

    private void ProcessSkillData(Pk2Reader pk2, Pk2File file, IProgress<string>? progress)
    {
        string? text = pk2.ReadFileText(file);
        if (text is null) return;

        int count = 0;
        foreach (var row in ParseTsv(text))
        {
            if (row.Length < 5) continue;
            if (!uint.TryParse(row[1].Trim(), out uint refId) || refId == 0) continue;

            byte level = ParseU8(row, Math.Min(10, row.Length - 1));
            Skills.TryAdd(refId, new SkillData(refId, row[2].Trim(),
                                               ScanForStrKey(row, 5, 25), level));
            count++;
        }
        progress?.Report($"    {file.Name}: {count}");
    }

    // ── Name resolution ───────────────────────────────────────────────────────

    public string GetCharacterName(uint refId)
    {
        if (!Characters.TryGetValue(refId, out var c)) return $"0x{refId:X}";
        return Lookup(c.NameKey) ?? c.InternalName;
    }

    public string GetItemName(uint refId)
    {
        if (!Items.TryGetValue(refId, out var item)) return $"0x{refId:X}";
        return Lookup(item.NameKey) ?? item.InternalName;
    }

    public string GetSkillName(uint refId)
    {
        if (!Skills.TryGetValue(refId, out var s)) return $"0x{refId:X}";
        return Lookup(s.NameKey) ?? s.InternalName;
    }

    private string? Lookup(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return Strings.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    private static IEnumerable<string[]> ParseTsv(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            string t = line.TrimEnd('\r', ' ');
            if (t.Length == 0 || t[0] == '/' || t[0] == '#') continue;
            yield return t.Split('\t');
        }
    }

    /// <summary>
    /// Scan columns [start, end) for a value that looks like a string-table key
    /// (starts with STR_, UIIT_, SKILL_, MOB_, ITEM_, or NPC_).
    /// Version-independent — the column index shifts between iSRO builds.
    /// </summary>
    private static string ScanForStrKey(string[] row, int start, int end)
    {
        int limit = Math.Min(end, row.Length);
        for (int i = start; i < limit; i++)
        {
            string v = row[i].Trim();
            if (v.StartsWith("STR_", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("UIIT_", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("SKILL_", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("MOB_", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("NPC_", StringComparison.OrdinalIgnoreCase))
                return v;
        }
        return string.Empty;
    }

    private static byte ParseU8(string[] row, int idx)
    {
        if ((uint)idx >= (uint)row.Length) return 0;
        return byte.TryParse(row[idx].Trim(), out byte v) ? v : (byte)0;
    }
}