using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace InsightBot.Views.Pages;

public sealed partial class TownPage : Page
{
    public TownViewModel VM { get; } = new(
        App.BotVM.Service.Profile.Town,
        App.BotVM.Service.Profile.Potions);
    public TownPage() => InitializeComponent();
}