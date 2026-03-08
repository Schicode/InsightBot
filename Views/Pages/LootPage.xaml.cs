using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace InsightBot.Views.Pages;

public sealed partial class LootPage : Page
{
    public LootViewModel VM { get; } = new(App.BotVM.Service.Profile.Loot);
    public LootPage() => InitializeComponent();
}