using InsightBot.Core.Configuration;

namespace InsightBot.Views.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    public ConnectionConfig Conn { get; }
    public SettingsViewModel(ConnectionConfig cfg) => Conn = cfg;

    public double GatewayPort
    {
        get => Conn.GatewayPort;
        set { Conn.GatewayPort = (int)value; OnPropertyChanged(); }
    }

    public double ProxyPort
    {
        get => Conn.ProxyPort;
        set { Conn.ProxyPort = (int)value; OnPropertyChanged(); }
    }
}
