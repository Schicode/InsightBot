using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using System;

namespace InsightBot.Views.Pages;

public sealed partial class GameDataPage : Page
{
    public GameDataViewModel VM { get; } = new();

    public GameDataPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        VM.SetDispatcher(DispatcherQueue);
    }

    private async void BrowseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pk2");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        // Von einer Page aus: HWND über XamlRoot → ContentIslandEnvironment → AppWindowId
        var windowId = XamlRoot.ContentIslandEnvironment.AppWindowId;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(windowId);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            VM.Pk2Path = file.Path;
            VM.LoadCommand.RaiseCanExecuteChanged();
        }
    }
}