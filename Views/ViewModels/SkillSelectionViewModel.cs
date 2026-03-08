using InsightBot.Core.Pk2;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace InsightBot.Views.ViewModels;

// ── SkillIconItem ─────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single skill or buff entry in the icon-based selection UI.
/// Loaded from PK2 game data.
/// </summary>
public sealed class SkillIconItem : ObservableObject
{
    public uint   RefId        { get; init; }
    public string InternalName { get; init; } = string.Empty;
    public string DisplayName  { get; init; } = string.Empty;
    public string Description  { get; init; } = string.Empty;
    public byte   Level        { get; init; }

    /// <summary>Icon image loaded from PK2. Null = show default placeholder.</summary>
    private BitmapImage? _icon;
    public  BitmapImage? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string Tooltip =>
        $"[{RefId}] {DisplayName}\n{InternalName}" +
        (Level > 0 ? $"\nLevel {Level}" : "") +
        (Description.Length > 0 ? $"\n{Description}" : "");
}

// ── SkillSelectionViewModel ───────────────────────────────────────────────────

/// <summary>
/// ViewModel for the icon-based skill / buff selection page.
///
/// Two observable collections drive the UI:
///   AvailableSkills  — left panel (all skills from PK2, filtered by search)
///   SelectedSkills   — right panel (currently active selection)
///
/// Clicking an item moves it between panels (Add / Remove).
/// </summary>
public sealed class SkillSelectionViewModel : ObservableObject
{
    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<SkillIconItem> AvailableSkills { get; } = new();
    public ObservableCollection<SkillIconItem> SelectedSkills  { get; } = new();

    // ── Search / filter ───────────────────────────────────────────────────────

    private string _searchText = string.Empty;
    public  string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
                ApplyFilter();
        }
    }

    private bool _showActiveOnly;
    public bool ShowActiveOnly
    {
        get => _showActiveOnly;
        set { if (Set(ref _showActiveOnly, value)) ApplyFilter(); }
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _isLoading;
    public  bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    private string _statusText = "Load Media.pk2 to view skills";
    public  string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public RelayCommand<SkillIconItem> AddSkillCommand    { get; }
    public RelayCommand<SkillIconItem> RemoveSkillCommand { get; }
    public AsyncRelayCommand           LoadSkillsCommand  { get; }

    // ── Internal ──────────────────────────────────────────────────────────────

    private readonly List<SkillIconItem> _allSkills = new();
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SkillSelectionViewModel()
    {
        AddSkillCommand    = new RelayCommand<SkillIconItem>(AddSkill,    s => s is not null && !s.IsSelected);
        RemoveSkillCommand = new RelayCommand<SkillIconItem>(RemoveSkill, s => s is not null && s.IsSelected);
        LoadSkillsCommand  = new AsyncRelayCommand(LoadSkillsAsync, () => !IsLoading);
    }

    public void SetDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dq) => _dispatcher = dq;

    // ── Load from GameDataService ─────────────────────────────────────────────

    public async Task LoadSkillsAsync()
    {
        var svc = GameDataService.Instance;
        if (!svc.IsLoaded)
        {
            StatusText = "PK2 not loaded — go to Settings first.";
            return;
        }

        IsLoading  = true;
        StatusText = "Loading skills…";

        await Task.Run(() =>
        {
            var items = svc.SearchSkills("")
                           .Select(s => new SkillIconItem
                           {
                               RefId        = s.RefId,
                               InternalName = s.InternalName,
                               DisplayName  = svc.GetSkillName(s.RefId),
                               Level        = s.Level
                           })
                           .OrderBy(s => s.DisplayName)
                           .ToList();

            Dispatch(() =>
            {
                _allSkills.Clear();
                _allSkills.AddRange(items);
                ApplyFilter();
                StatusText = $"{items.Count:N0} skills loaded";
                IsLoading  = false;
            });
        });
    }

    // ── Add / Remove ─────────────────────────────────────────────────────────

    private void AddSkill(SkillIconItem? item)
    {
        if (item is null || item.IsSelected) return;
        item.IsSelected = true;
        SelectedSkills.Add(item);
        AddSkillCommand.RaiseCanExecuteChanged();
        RemoveSkillCommand.RaiseCanExecuteChanged();
    }

    private void RemoveSkill(SkillIconItem? item)
    {
        if (item is null || !item.IsSelected) return;
        item.IsSelected = false;
        SelectedSkills.Remove(item);
        AddSkillCommand.RaiseCanExecuteChanged();
        RemoveSkillCommand.RaiseCanExecuteChanged();
    }

    // ── Get selected RefIds for BotProfile ────────────────────────────────────

    public List<uint> GetSelectedRefIds() => SelectedSkills.Select(s => s.RefId).ToList();

    public void SetSelectedRefIds(IEnumerable<uint> refIds)
    {
        // Clear existing
        foreach (var s in SelectedSkills.ToList()) RemoveSkill(s);

        // Re-select
        var set = new HashSet<uint>(refIds);
        foreach (var s in _allSkills.Where(s => set.Contains(s.RefId)))
            AddSkill(s);
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var filtered = _allSkills.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string q = _searchText.Trim();
            filtered = filtered.Where(s =>
                s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.InternalName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (_showActiveOnly)
            filtered = filtered.Where(s => s.IsSelected);

        Dispatch(() =>
        {
            AvailableSkills.Clear();
            foreach (var s in filtered.Take(500)) // limit for UI perf
                AvailableSkills.Add(s);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Dispatch(Action action)
    {
        if (_dispatcher is null) { action(); return; }
        _dispatcher.TryEnqueue(() => action());
    }
}
