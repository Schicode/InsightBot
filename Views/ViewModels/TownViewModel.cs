using InsightBot.Core.Configuration;

namespace InsightBot.Views.ViewModels;

public sealed class TownViewModel : ObservableObject
{
    public TownConfig   Town    { get; }
    public PotionConfig Potions { get; }

    public TownViewModel(TownConfig town, PotionConfig potions)
    {
        Town    = town;
        Potions = potions;
    }

    public string ReturnScrollRefIdText
    {
        get => $"0x{Town.ReturnScrollRefId:X}";
        set { if (BuffsViewModel.TryHex(value, out uint v)) { Town.ReturnScrollRefId = v; OnPropertyChanged(); } }
    }

    public string HpPotionRefIdText
    {
        get => $"0x{Potions.HpPotionRefId:X}";
        set { if (BuffsViewModel.TryHex(value, out uint v)) { Potions.HpPotionRefId = v; OnPropertyChanged(); } }
    }

    public string MpPotionRefIdText
    {
        get => $"0x{Potions.MpPotionRefId:X}";
        set { if (BuffsViewModel.TryHex(value, out uint v)) { Potions.MpPotionRefId = v; OnPropertyChanged(); } }
    }
}
