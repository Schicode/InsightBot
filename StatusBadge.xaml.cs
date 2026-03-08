using InsightBot.Core.Bot;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InsightBot;

public sealed partial class StatusBadge : UserControl
{
    public StatusBadge()
    {
        InitializeComponent();
        Loaded   += (_, _) => { App.BotVM.StateChanged += OnStateChanged; Refresh(App.BotVM.State); };
        Unloaded += (_, _) => App.BotVM.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(BotState s) => DispatcherQueue.TryEnqueue(() => Refresh(s));

    private void Refresh(BotState state)
    {
        StateText.Text = state.ToString();

        BadgeBorder.Background = state switch
        {
            BotState.Hunting   or
            BotState.Attacking => new SolidColorBrush(Colors.DarkGreen),
            BotState.Looting   => new SolidColorBrush(Colors.DarkGoldenrod),
            BotState.Buffing   => new SolidColorBrush(Colors.SteelBlue),
            BotState.Town      or
            BotState.Returning => new SolidColorBrush(Colors.SlateBlue),
            BotState.Dead      => new SolidColorBrush(Colors.DarkRed),
            BotState.Paused    => new SolidColorBrush(Colors.DimGray),
            BotState.Error     => new SolidColorBrush(Colors.OrangeRed),
            _                  => new SolidColorBrush(Colors.Gray),
        };

        StateText.Foreground = new SolidColorBrush(Colors.White);
    }
}
