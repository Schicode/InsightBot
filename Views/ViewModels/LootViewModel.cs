using InsightBot.Core.Configuration;
using System.Linq;

namespace InsightBot.Views.ViewModels;

public sealed class LootViewModel : ObservableObject
{
    public LootConfig Loot { get; }
    public LootViewModel(LootConfig cfg) => Loot = cfg;

    public string AllowedRefIdsText
    {
        get => string.Join(",", Loot.AllowedRefIds.Select(id => $"0x{id:X}"));
        set { Loot.AllowedRefIds = HuntViewModel.ParseList(value); OnPropertyChanged(); }
    }

    public string IgnoreRefIdsText
    {
        get => string.Join(",", Loot.IgnoreRefIds.Select(id => $"0x{id:X}"));
        set { Loot.IgnoreRefIds = HuntViewModel.ParseList(value); OnPropertyChanged(); }
    }
}
