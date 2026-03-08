using InsightBot.Core.Configuration;
using InsightBot.Core.Pk2;
using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace InsightBot.Views.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel VM { get; }

    public SettingsPage()
    {
        InitializeComponent();
        VM = new SettingsViewModel(
            App.BotVM.Service.Profile.Connection,
            App.BotVM.Service.Profile.Pk2);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        VM.SetDispatcher(DispatcherQueue);
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pk2");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var windowId = XamlRoot.ContentIslandEnvironment.AppWindowId;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(windowId);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            VM.Pk2Path = file.Path;
            VM.LoadPk2Command.RaiseCanExecuteChanged();
        }
    }

    private void PasswordBox_PasswordChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args)
        => VM.Password = sender.Password;

    private void PinBox_PasswordChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args)
        => VM.SecondaryPin = sender.Password;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        App.BotVM.Service.SaveProfile();
        var btn = (Button)sender;
        btn.Content = "Saved ✓";
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(2000);
            btn.Content = "Save Settings";
        });
    }
}