using System.Collections.ObjectModel;

namespace InsightBot.Views.ViewModels;

public sealed class LogPageViewModel : ObservableObject
{
    public ObservableCollection<string> LogLines => App.BotVM.LogLines;

    public RelayCommand ClearLogCommand { get; }

    public LogPageViewModel() =>
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());
}
