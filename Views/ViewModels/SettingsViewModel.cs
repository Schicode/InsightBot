using InsightBot.Core.Configuration;
using InsightBot.Core.Pk2;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace InsightBot.Views.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
/// All properties match the x:Bind paths in SettingsPage.xaml exactly.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ConnectionConfig _conn;
    private readonly Pk2Config _pk2cfg;
    private DispatcherQueue? _dispatcher;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsViewModel(ConnectionConfig conn, Pk2Config pk2cfg)
    {
        _conn = conn;
        _pk2cfg = pk2cfg;

        LoadPk2Command = new AsyncRelayCommand(LoadPk2Async, () => !IsLoading && Pk2Path.Length > 0);
        AutoFillFromPk2Command = new RelayCommand(AutoFillFromPk2, () => GameDataService.Instance.IsLoaded);
        ClearPinCommand = new RelayCommand(() => SecondaryPin = string.Empty);

        // Seed from loaded profile
        Pk2Path = _pk2cfg.Pk2Path;
        Pk2Key = _pk2cfg.Pk2Key;
        Language = _pk2cfg.Language;
        Username = _conn.Username;
        Password = _conn.Password;
        SecondaryPin = _conn.SecondaryPin;
        GatewayHost = _conn.GatewayHost;
        // GatewayPortText / ProxyPortText are string-backed
        _gatewayPortText = _conn.GatewayPort > 0 ? _conn.GatewayPort.ToString() : string.Empty;
        _proxyPortText = _conn.ProxyPort > 0 ? _conn.ProxyPort.ToString() : string.Empty;

        // Subscribe to GameDataService events
        GameDataService.Instance.Progress += msg => Dispatch(() => LoadProgress = msg);
        GameDataService.Instance.LoadError += err => Dispatch(() => { LoadProgress = $"ERROR: {err}"; IsLoading = false; });
        GameDataService.Instance.Loaded += () => Dispatch(OnPk2Loaded);
    }

    public void SetDispatcher(DispatcherQueue dq) => _dispatcher = dq;

    // ── PK2 ───────────────────────────────────────────────────────────────────

    private string _pk2Path = string.Empty;
    public string Pk2Path
    {
        get => _pk2Path;
        set { if (Set(ref _pk2Path, value)) { _pk2cfg.Pk2Path = value; LoadPk2Command.RaiseCanExecuteChanged(); } }
    }

    private string _pk2Key = "169841";
    public string Pk2Key
    {
        get => _pk2Key;
        set { if (Set(ref _pk2Key, value)) _pk2cfg.Pk2Key = value; }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { if (Set(ref _isLoading, value)) LoadPk2Command.RaiseCanExecuteChanged(); }
    }

    private string _loadProgress = string.Empty;
    public string LoadProgress
    {
        get => _loadProgress;
        private set => Set(ref _loadProgress, value);
    }

    // ── Language ──────────────────────────────────────────────────────────────

    // XAML binds: ItemsSource="{x:Bind VM.AvailableLanguages}"  SelectedItem="{x:Bind VM.Language}"
    public ObservableCollection<string> AvailableLanguages { get; } =
        new(new[] { "English", "German", "French", "Spanish", "Chinese", "Korean" });

    private string _language = "English";
    public string Language
    {
        get => _language;
        set { if (Set(ref _language, value)) _pk2cfg.Language = value ?? "English"; }
    }

    // ── Gateway options (populated after PK2 load) ────────────────────────────

    // XAML binds: ItemsSource="{x:Bind VM.GatewayOptions}"  SelectedIndex="{x:Bind VM.SelectedGatewayIndex}"
    public ObservableCollection<string> GatewayOptions { get; } = new();

    private int _selectedGatewayIndex = -1;
    public int SelectedGatewayIndex
    {
        get => _selectedGatewayIndex;
        set
        {
            if (Set(ref _selectedGatewayIndex, value) && value >= 0 && value < GatewayOptions.Count)
            {
                // Parse "host:port" and apply
                var entry = GatewayOptions[value];
                int colon = entry.LastIndexOf(':');
                if (colon > 0)
                {
                    GatewayHost = entry[..colon];
                    GatewayPortText = entry[(colon + 1)..];
                }
            }
        }
    }

    // ── Connection ────────────────────────────────────────────────────────────

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set { if (Set(ref _username, value)) _conn.Username = value; }
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set { if (Set(ref _password, value)) _conn.Password = value; }
    }

    private string _secondaryPin = string.Empty;
    public string SecondaryPin
    {
        get => _secondaryPin;
        set
        {
            if (Set(ref _secondaryPin, value))
            {
                _conn.SecondaryPin = value;
                OnPropertyChanged(nameof(HasSecondaryPin));
            }
        }
    }

    // XAML binds BoolToVisConverter / BoolToInvVisConverter to this
    public bool HasSecondaryPin => !string.IsNullOrEmpty(_secondaryPin);

    private string _gatewayHost = string.Empty;
    public string GatewayHost
    {
        get => _gatewayHost;
        set { if (Set(ref _gatewayHost, value)) _conn.GatewayHost = value; }
    }

    // XAML uses Text="{x:Bind VM.GatewayPortText}" (string, not double — TextBox binding)
    private string _gatewayPortText = string.Empty;
    public string GatewayPortText
    {
        get => _gatewayPortText;
        set
        {
            if (Set(ref _gatewayPortText, value))
                if (int.TryParse(value, out int p)) _conn.GatewayPort = p;
        }
    }

    private string _proxyPortText = string.Empty;
    public string ProxyPortText
    {
        get => _proxyPortText;
        set
        {
            if (Set(ref _proxyPortText, value))
                if (int.TryParse(value, out int p)) _conn.ProxyPort = p;
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public AsyncRelayCommand LoadPk2Command { get; }
    public RelayCommand AutoFillFromPk2Command { get; }   // XAML: VM.AutoFillFromPk2Command
    public RelayCommand ClearPinCommand { get; }

    // ── PK2 load ──────────────────────────────────────────────────────────────

    private async Task LoadPk2Async()
    {
        if (string.IsNullOrEmpty(Pk2Path)) return;
        IsLoading = true;
        LoadProgress = "Opening Media.pk2…";
        try
        {
            await GameDataService.Instance.LoadAsync(Pk2Path, Pk2Key);
        }
        catch (Exception ex)
        {
            LoadProgress = $"Error: {ex.Message}";
            IsLoading = false;
        }
    }

    private void OnPk2Loaded()
    {
        IsLoading = false;
        LoadProgress = $"Loaded — {GameDataService.Instance.CharacterCount:N0} chars, " +
                       $"{GameDataService.Instance.ItemCount:N0} items, " +
                       $"{GameDataService.Instance.SkillCount:N0} skills";

        GatewayOptions.Clear();
        var info = GameDataService.Instance.ConnectionInfo;
        if (info != null)
            foreach (var (host, port) in info.Gateways)
                GatewayOptions.Add($"{host}:{port}");

        AutoFillFromPk2Command.RaiseCanExecuteChanged();
        // Auto-select first gateway if none is set yet
        if (GatewayOptions.Count > 0 && string.IsNullOrEmpty(GatewayHost))
            SelectedGatewayIndex = 0;
    }

    private void AutoFillFromPk2()
    {
        var info = GameDataService.Instance.ConnectionInfo;
        if (info?.Gateways.Count > 0)
        {
            var (host, port) = info.Gateways[0];
            GatewayHost = host;
            GatewayPortText = port.ToString();
            LoadProgress = $"Auto-filled: {host}:{port}";
        }
    }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    private void Dispatch(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) { action(); return; }
        _dispatcher.TryEnqueue(new DispatcherQueueHandler(action));
    }
}