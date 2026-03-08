using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InsightBot.Core.Pk2;

/// <summary>
/// Application-level singleton that owns the loaded game data.
/// Call <see cref="LoadAsync"/> once at startup (e.g. from Settings page).
/// All other subsystems access data through the read-only properties.
/// </summary>
public sealed class GameDataService
{
    public static GameDataService Instance { get; } = new();

    private GameDataLoader? _data;

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsLoaded => _data is not null;
    public int CharacterCount => _data?.Characters.Count ?? 0;
    public int ItemCount => _data?.Items.Count ?? 0;
    public int SkillCount => _data?.Skills.Count ?? 0;
    public int StringCount => _data?.Strings.Count ?? 0;

    /// <summary>Connection info auto-extracted from PK2 (DIVISIONINFO, GATEPORT, type.txt).</summary>
    public Pk2ConnectionInfo? ConnectionInfo => _data?.ConnectionInfo;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<string>? Progress;
    public event Action? Loaded;
    public event Action<string>? LoadError;

    // ── Load ──────────────────────────────────────────────────────────────────

    public async Task LoadAsync(string pk2Path, string key = "169841")
    {
        _data = null;

        try
        {
            await Task.Run(() =>
            {
                var reporter = new Progress<string>(msg => Progress?.Invoke(msg));

                Progress?.Invoke($"Opening {System.IO.Path.GetFileName(pk2Path)}…");
                Progress?.Invoke($"Key: {key}  (derived: {BitConverter.ToString(Pk2Reader.DeriveKey(key)[..6])}…)");

                using var pk2 = new Pk2Reader(pk2Path, key);

                // Diagnostics: show root folders so the user can verify the PK2 opened
                string rootFolders = string.Join(", ", pk2.Root.Folders.Keys.Take(8));
                Progress?.Invoke($"Root folders: [{rootFolders}]");

                if (pk2.Root.Folders.Count == 0 && pk2.Root.Files.Count == 0)
                {
                    Progress?.Invoke("ERROR: PK2 appears empty — wrong key or corrupted file.");
                    LoadError?.Invoke("PK2 decryption produced no entries. Check the Blowfish key.");
                    return;
                }

                _data = GameDataLoader.Load(pk2, reporter);
            });
        }
        catch (Exception ex)
        {
            string msg = $"Failed to load PK2: {ex.Message}";
            Progress?.Invoke(msg);
            LoadError?.Invoke(msg);
            return;
        }

        Loaded?.Invoke();
        Progress?.Invoke($"Load complete — Characters:{CharacterCount:N0}  Items:{ItemCount:N0}  Skills:{SkillCount:N0}  Strings:{StringCount:N0}");
    }

    // ── Name resolution ───────────────────────────────────────────────────────

    public string GetMonsterName(uint refId) => _data?.GetCharacterName(refId) ?? $"#0x{refId:X}";
    public string GetItemName(uint refId) => _data?.GetItemName(refId) ?? $"#0x{refId:X}";
    public string GetSkillName(uint refId) => _data?.GetSkillName(refId) ?? $"#0x{refId:X}";
    public string GetString(string key) => _data?.Strings.GetValueOrDefault(key) ?? key;

    // ── Search ────────────────────────────────────────────────────────────────

    public IEnumerable<CharacterData> SearchMonsters(string query) =>
        _data?.Characters.Values
              .Where(c => c.Kind == CharacterKind.Monster &&
                          MatchesQuery(c.InternalName, GetMonsterName(c.RefId), query))
        ?? Enumerable.Empty<CharacterData>();

    public IEnumerable<ItemData> SearchItems(string query) =>
        _data?.Items.Values
              .Where(i => MatchesQuery(i.InternalName, GetItemName(i.RefId), query))
        ?? Enumerable.Empty<ItemData>();

    public IEnumerable<SkillData> SearchSkills(string query) =>
        _data?.Skills.Values
              .Where(s => MatchesQuery(s.InternalName, GetSkillName(s.RefId), query))
        ?? Enumerable.Empty<SkillData>();

    private static bool MatchesQuery(string internalName, string displayName, string query) =>
        internalName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        displayName.Contains(query, StringComparison.OrdinalIgnoreCase);
}