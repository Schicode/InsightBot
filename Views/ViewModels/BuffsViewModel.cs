using InsightBot.Core.Configuration;
using System.Linq;

namespace InsightBot.Views.ViewModels;

public sealed class BuffsViewModel : ObservableObject
{
    public BuffConfig Buffs { get; }
    public BuffsViewModel(BuffConfig cfg) => Buffs = cfg;

    public string SelfBuffSkillIdsText
    {
        get => string.Join(",", Buffs.SelfBuffSkillIds.Select(id => $"0x{id:X}"));
        set { Buffs.SelfBuffSkillIds = HuntViewModel.ParseList(value); OnPropertyChanged(); }
    }

    public string HealSkillIdText
    {
        get => $"0x{Buffs.HealSkillId:X}";
        set { if (TryHex(value, out uint v)) { Buffs.HealSkillId = v; OnPropertyChanged(); } }
    }

    public string MpSkillIdText
    {
        get => $"0x{Buffs.MpSkillId:X}";
        set { if (TryHex(value, out uint v)) { Buffs.MpSkillId = v; OnPropertyChanged(); } }
    }

    internal static bool TryHex(string s, out uint v)
    {
        s = s.Trim();
        if (s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out v);
        return uint.TryParse(s, out v);
    }
}
