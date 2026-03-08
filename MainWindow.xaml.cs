using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace InsightBot;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["dashboard"] = typeof(Views.Pages.DashboardPage),
        ["hunt"]      = typeof(Views.Pages.HuntPage),
        ["loot"]      = typeof(Views.Pages.LootPage),
        ["buffs"]     = typeof(Views.Pages.BuffsPage),
        ["skills"]    = typeof(Views.Pages.SkillsPage),
        ["town"]      = typeof(Views.Pages.TownPage),
        ["log"]       = typeof(Views.Pages.LogPage),
        ["gamedata"]  = typeof(Views.Pages.GameDataPage),
        ["settings"]  = typeof(Views.Pages.SettingsPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        NavView.SelectedItem = NavView.MenuItems[0];
        Navigate("dashboard");
    }

    private void NavView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        string tag = args.IsSettingsSelected
            ? "settings"
            : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "dashboard";
        Navigate(tag);
    }

    private void Navigate(string tag)
    {
        if (Pages.TryGetValue(tag, out var pageType))
            ContentFrame.Navigate(pageType);
    }
}
