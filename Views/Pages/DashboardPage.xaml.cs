using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace InsightBot.Views.Pages;

public sealed partial class DashboardPage : Page
{
    public BotServiceViewModel VM => App.BotVM;
    public DashboardPage() => InitializeComponent();
}