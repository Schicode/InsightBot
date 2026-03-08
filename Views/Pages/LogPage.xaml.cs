using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace InsightBot.Views.Pages;

public sealed partial class LogPage : Page
{
    public LogPageViewModel VM { get; } = new();
    public LogPage() => InitializeComponent();
}