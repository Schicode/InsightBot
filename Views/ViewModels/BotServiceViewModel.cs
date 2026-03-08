using InsightBot.Core.Bot;
using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InsightBot.Views.ViewModels;

public sealed class BotServiceViewModel : ObservableObject
{
    public Core.Bot.BotService Service { get; } = new();

    public DispatcherQueue? Dispatcher { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private BotState _state = BotState.Idle;
    public BotState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsPaused));
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                PauseCommand.RaiseCanExecuteChanged();
                ResumeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StateLabel => State.ToString();
    public bool IsRunning => State != BotState.Idle && State != BotState.Error;
    public bool IsPaused => State == BotState.Paused;

    public event System.Action<BotState>? StateChanged;

    // ── Session counters ──────────────────────────────────────────────────────

    private int _monstersKilled;
    private int _itemsPickedUp;
    private ulong _goldCollected;
    private int _potionsUsed;
    private int _deathCount;
    private string _statusMessage = string.Empty;

    public int MonstersKilled { get => _monstersKilled; private set => Set(ref _monstersKilled, value); }
    public int ItemsPickedUp { get => _itemsPickedUp; private set => Set(ref _itemsPickedUp, value); }
    public ulong GoldCollected { get => _goldCollected; private set => Set(ref _goldCollected, value); }
    public int PotionsUsed { get => _potionsUsed; private set => Set(ref _potionsUsed, value); }
    public int DeathCount { get => _deathCount; private set => Set(ref _deathCount, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    // ── Log ───────────────────────────────────────────────────────────────────

    public ObservableCollection<string> LogLines { get; } = new();

    // ── Profile ───────────────────────────────────────────────────────────────

    private string _profileName = "Default";
    public string ProfileName { get => _profileName; set => Set(ref _profileName, value); }

    private List<string> _availableProfiles = new();
    public List<string> AvailableProfiles
    {
        get => _availableProfiles;
        private set => Set(ref _availableProfiles, value);
    }

    public void RefreshProfiles() =>
        AvailableProfiles = Service.ListProfiles().ToList();

    // ── Commands ──────────────────────────────────────────────────────────────

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand LoadProfileCommand { get; }
    public RelayCommand SaveProfileCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public BotServiceViewModel()
    {
        StartCommand = new AsyncRelayCommand(
            execute: () => StartAsync(),
            canExecute: () => !IsRunning);
        StopCommand = new AsyncRelayCommand(
            execute: () => Service.StopAsync(),
            canExecute: () => IsRunning);
        PauseCommand = new RelayCommand(
            execute: () => Service.Pause(),
            canExecute: () => IsRunning && !IsPaused);
        ResumeCommand = new RelayCommand(
            execute: () => Service.Resume(),
            canExecute: () => IsPaused);
        LoadProfileCommand = new RelayCommand(execute: () => LoadProfile());
        SaveProfileCommand = new RelayCommand(execute: () => SaveProfile());

        Service.StateChanged += s => Dispatch(() => OnStateChanged(s));
        Service.LogMessage += m => Dispatch(() => AddLog(m));
    }

    // ── Commands impl ─────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task StartAsync()
    {
        Service.LoadProfile(ProfileName);
        await Service.StartAsync();
    }

    private void LoadProfile()
    {
        Service.LoadProfile(ProfileName);
        AddLog($"Profile '{ProfileName}' loaded.");
    }

    private void SaveProfile()
    {
        Service.SaveProfile();
        AddLog("Profile saved.");
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnStateChanged(BotState newState)
    {
        State = newState;
        SyncStatus();
        StateChanged?.Invoke(newState);
    }

    private void SyncStatus()
    {
        var s = Service.Status;
        MonstersKilled = s.MonstersKilled;
        ItemsPickedUp = s.ItemsPickedUp;
        GoldCollected = s.GoldCollected;
        PotionsUsed = s.PotionsUsed;
        DeathCount = s.DeathCount;
        StatusMessage = s.Message;
    }

    private void AddLog(string line)
    {
        LogLines.Add(line);
        if (LogLines.Count > 200) LogLines.RemoveAt(0);
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private void Dispatch(System.Action action)
    {
        if (Dispatcher is null) { action(); return; }
        if (Dispatcher.HasThreadAccess)
            action();
        else
            Dispatcher.TryEnqueue(new DispatcherQueueHandler(action));
    }
}