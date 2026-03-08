using InsightBot.Core.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace InsightBot.Views.ViewModels;

public sealed class HuntViewModel : ObservableObject
{
    private readonly HuntConfig _cfg;
    public HuntConfig Hunt => _cfg;

    public HuntViewModel(HuntConfig cfg) => _cfg = cfg;

    public string AttackSkillIdsText
    {
        get => string.Join(",", _cfg.AttackSkillIds.Select(id => $"0x{id:X}"));
        set { _cfg.AttackSkillIds = ParseList(value); OnPropertyChanged(); }
    }

    public string TargetRefIdsText
    {
        get => string.Join(",", _cfg.TargetRefIds.Select(id => $"0x{id:X}"));
        set { _cfg.TargetRefIds = ParseList(value); OnPropertyChanged(); }
    }

    public string IgnoreRefIdsText
    {
        get => string.Join(",", _cfg.IgnoreRefIds.Select(id => $"0x{id:X}"));
        set { _cfg.IgnoreRefIds = ParseList(value); OnPropertyChanged(); }
    }

    internal static List<uint> ParseList(string text) =>
        text.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Select(s =>
            {
                s = s.Trim();
                bool ok = s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
                    ? uint.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out uint v)
                    : uint.TryParse(s, out v);
                return ok ? (uint?)v : null;
            })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
}
