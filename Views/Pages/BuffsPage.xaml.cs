using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace InsightBot.Views.Pages;

public sealed partial class BuffsPage : Page
{
    public BuffsViewModel VM { get; } = new(App.BotVM.Service.Profile.Buffs);
    public BuffsPage() => InitializeComponent();
}