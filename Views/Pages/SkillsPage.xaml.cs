using InsightBot.Core.Pk2;
using InsightBot.Views.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;

namespace InsightBot.Views.Pages;

public sealed partial class SkillsPage : Page
{
    public SkillSelectionViewModel VM { get; } = new();

    private SkillIconItem? _selectedAvailable;
    private SkillIconItem? _selectedFromRight;

    public SkillsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        VM.SetDispatcher(DispatcherQueue);

        // Auto-load if PK2 is already loaded
        if (GameDataService.Instance.IsLoaded && VM.AvailableSkills.Count == 0)
            _ = VM.LoadSkillsAsync();
    }

    private void AvailableItem_Click(object sender, ItemClickEventArgs e)
    {
        _selectedAvailable = e.ClickedItem as SkillIconItem;
    }

    private void SelectedItem_Click(object sender, ItemClickEventArgs e)
    {
        _selectedFromRight = e.ClickedItem as SkillIconItem;
    }

    private void AddButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_selectedAvailable is not null)
            VM.AddSkillCommand.Execute(_selectedAvailable);
    }

    private void RemoveButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_selectedFromRight is not null)
            VM.RemoveSkillCommand.Execute(_selectedFromRight);
    }

    private void ClearAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        foreach (var s in VM.SelectedSkills.ToList())
            VM.RemoveSkillCommand.Execute(s);
    }
}
