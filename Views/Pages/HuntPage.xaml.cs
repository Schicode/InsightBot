using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace InsightBot.Views.Pages;

public sealed partial class HuntPage : Page
{
    public HuntViewModel VM { get; } = new(App.BotVM.Service.Profile.Hunt);
    public HuntPage() => InitializeComponent();
}