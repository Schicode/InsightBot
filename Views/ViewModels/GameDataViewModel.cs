using InsightBot.Core.Pk2;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InsightBot.Views.ViewModels;

public sealed class GameDataViewModel : ObservableObject
{
    private readonly GameDataService _svc = GameDataService.Instance;
    private DispatcherQueue? _dispatcher;

    // ── State ─────────────────────────────────────────────────────────────────

    private bool   _isLoading;
    private string _statusText  = "No data loaded. Pick Media.pk2 to begin.";
    private string _pk2Path     = string.Empty;
    private string _searchQuery = string.Empty;
    private int    _tabIndex;   // 0=Monsters, 1=Items, 2=Skills

    public bool   IsLoading   { get => _isLoading;   private set => Set(ref _isLoading,   value); }
    public string StatusText  { get => _statusText;  private set => Set(ref _statusText,  value); }
    public string Pk2Path     { get => _pk2Path;     set => Set(ref _pk2Path, value); }
    public int    TabIndex    { get => _tabIndex;     set { Set(ref _tabIndex, value); RunSearch(); } }

    public string SearchQuery
    {
        get => _searchQuery;
        set { Set(ref _searchQuery, value); RunSearch(); }
    }

    // ── Results ───────────────────────────────────────────────────────────────

    public ObservableCollection<GameDataRow> Results { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────

    public AsyncRelayCommand LoadCommand  { get; }
    public RelayCommand      ClearCommand { get; }

    public GameDataViewModel()
    {
        LoadCommand  = new AsyncRelayCommand(LoadPk2Async, () => !IsLoading && !string.IsNullOrEmpty(Pk2Path));
        ClearCommand = new RelayCommand(ClearSearch);

        _svc.Progress += msg => Dispatch(() => StatusText = msg);
        _svc.Loaded   += ()  => Dispatch(() =>
        {
            IsLoading  = false;
            StatusText = $"Loaded — {_svc.CharacterCount:N0} characters, " +
                         $"{_svc.ItemCount:N0} items, {_svc.SkillCount:N0} skills";
            LoadCommand.RaiseCanExecuteChanged();
            RunSearch();
        });
    }

    public void SetDispatcher(DispatcherQueue dq) => _dispatcher = dq;

    // ── Impl ──────────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task LoadPk2Async()
    {
        if (string.IsNullOrEmpty(Pk2Path)) return;
        IsLoading  = true;
        StatusText = "Opening Media.pk2…";
        LoadCommand.RaiseCanExecuteChanged();
        try
        {
            await _svc.LoadAsync(Pk2Path);
        }
        catch (Exception ex)
        {
            IsLoading  = false;
            StatusText = $"Error: {ex.Message}";
            LoadCommand.RaiseCanExecuteChanged();
        }
    }

    private void RunSearch()
    {
        if (!_svc.IsLoaded) return;
        Results.Clear();
        string q = _searchQuery.Trim();

        IEnumerable<GameDataRow> rows = TabIndex switch
        {
            1 => _svc.SearchItems(q)
                      .Take(200)
                      .Select(i => new GameDataRow($"0x{i.RefId:X}", _svc.GetItemName(i.RefId), i.InternalName,
                                                   $"TID {i.TypeId1}.{i.TypeId2}.{i.TypeId3}.{i.TypeId4}")),
            2 => _svc.SearchSkills(q)
                      .Take(200)
                      .Select(s => new GameDataRow($"0x{s.RefId:X}", _svc.GetSkillName(s.RefId), s.InternalName,
                                                   $"Lv {s.Level}")),
            _ => _svc.SearchMonsters(q)
                      .Take(200)
                      .Select(m => new GameDataRow($"0x{m.RefId:X}", _svc.GetMonsterName(m.RefId), m.InternalName,
                                                   $"Lv {m.Level}"))
        };

        foreach (var row in rows)
            Results.Add(row);
    }

    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        Results.Clear();
    }

    private void Dispatch(System.Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) { action(); return; }
        _dispatcher.TryEnqueue(new DispatcherQueueHandler(action));
    }
}

public sealed record GameDataRow(string RefId, string DisplayName, string InternalName, string Info);
