using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed partial class ManagerWindow
    {
        private readonly Border findBar;
        private TextBox search;
        private bool suppressSearch;
        private System.Windows.Threading.DispatcherTimer searchTimer;
        private Button findToggle, tagFilterButton;
        private StackPanel tagChips;
        private WrapPanel findScopes;
        private TextBlock searchSummary;

        private Border BuildFindBar()
        {
            var outer = new Border { Background = UiTheme.Background, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 8, 12, 8), Visibility = Visibility.Collapsed };
            var panel = new StackPanel();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            search = new TextBox { Height = 30, FontSize = 14, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1), ToolTip = Ui.Text("输入关键词搜索标题、标识符和正文") };
            System.Windows.Automation.AutomationProperties.SetName(search, Ui.Text("搜索关键词"));
            search.TextChanged += (s, e) => { if (!suppressSearch && searchTimer != null) { searchTimer.Stop(); searchTimer.Start(); } };
            search.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { searchTimer.Stop(); RefreshSearch(); e.Handled = true; }
                else if (e.Key == Key.Escape) { search.Clear(); e.Handled = true; }
            };
            row.Children.Add(search);
            var searchButton = Ui.PrimaryButton("搜索", () => { searchTimer.Stop(); RefreshSearch(); });
            Grid.SetColumn(searchButton, 1); row.Children.Add(searchButton);
            tagChips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var tagContent = new StackPanel { Orientation = Orientation.Horizontal };
            tagContent.Children.Add(Glyph(""));
            tagContent.Children.Add(tagChips);
            tagFilterButton = new Button { Content = tagContent, Height = 30, MinWidth = 100, MaxWidth = 250, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(7, 0, 7, 0), Background = UiTheme.Surface, BorderThickness = new Thickness(0), ToolTip = Ui.Text("标签过滤"), Focusable = true };
            tagFilterButton.Click += (s, e) => { try { OpenSearchTagFilter(); } catch (Exception ex) { Ui.Error(ex, this); } };
            Grid.SetColumn(tagFilterButton, 2); row.Children.Add(tagFilterButton);
            var reset = DarkButton(IconRetry, "重置搜索", ResetSearch);
            Grid.SetColumn(reset, 3); row.Children.Add(reset);
            findToggle = DarkButton("", "展开 / 收拢", ToggleFindBarExpanded);
            Grid.SetColumn(findToggle, 4); row.Children.Add(findToggle);
            panel.Children.Add(row);

            findScopes = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            var settings = host.Library.Catalog.Settings;
            findScopes.Children.Add(ScopeBox("桌面", () => settings.SearchDesktop, value => settings.SearchDesktop = value));
            findScopes.Children.Add(ScopeBox("附着", () => settings.SearchAttached, value => settings.SearchAttached = value));
            findScopes.Children.Add(ScopeBox("已收藏", () => settings.SearchStored, value => settings.SearchStored = value));
            findScopes.Children.Add(ScopeBox("休眠中", () => settings.SearchSleeping, value => settings.SearchSleeping = value));
            findScopes.Children.Add(ScopeBox("已关闭", () => settings.SearchClosed, value => settings.SearchClosed = value));
            findScopes.Children.Add(ScopeBox("重复提醒", () => settings.SearchRecurring, value => settings.SearchRecurring = value));
            findScopes.Children.Add(ScopeBox("搜索内容", () => settings.SearchContent, value => settings.SearchContent = value));
            var pin = new CheckBox { Content = Ui.Text("固定查找栏"), IsChecked = settings.SearchPinFindBar, Foreground = UiTheme.Text, Margin = new Thickness(0, 3, 18, 3), VerticalContentAlignment = VerticalAlignment.Center };
            pin.Checked += (s, e) => { settings.SearchPinFindBar = true; host.Library.SaveCatalog(); ApplyFindBarLayout(); };
            pin.Unchecked += (s, e) => { settings.SearchPinFindBar = false; host.Library.SaveCatalog(); ApplyFindBarLayout(); };
            findScopes.Children.Add(pin);
            panel.Children.Add(findScopes);
            searchSummary = new TextBlock { Foreground = UiTheme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 0, 0) };
            panel.Children.Add(searchSummary);
            outer.Child = panel;
            outer.SizeChanged += (s, e) => ScrollRailToSelection();

            searchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshSearch(); };
            UpdateTagChips();
            return outer;
        }

        private static TextBlock Glyph(string glyph) { return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = UiTheme.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }; }

        private Button DarkButton(string glyph, string tip, Action click)
        {
            var button = new Button { Content = Glyph(glyph), Width = 34, Height = 30, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0), Background = UiTheme.Surface, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1), Style = UiTheme.ButtonStyle(), ToolTip = Ui.Text(tip), Focusable = true };
            System.Windows.Automation.AutomationProperties.SetName(button, Ui.Text(tip));
            button.Click += (s, e) => { try { click(); } catch (Exception ex) { Ui.Error(ex, this); } };
            return button;
        }

        private CheckBox ScopeBox(string label, Func<bool> get, Action<bool> set)
        {
            var check = new CheckBox { Content = Ui.Text(label), IsChecked = get(), Foreground = UiTheme.Text, Margin = new Thickness(0, 3, 18, 3), VerticalContentAlignment = VerticalAlignment.Center };
            check.Checked += (s, e) => { if (suppressSearch) return; set(true); host.Library.SaveCatalog(); RefreshSearch(); };
            check.Unchecked += (s, e) => { if (suppressSearch) return; set(false); host.Library.SaveCatalog(); RefreshSearch(); };
            return check;
        }

        private void ApplyFindBarLayout()
        {
            if (findBar == null) return;
            var settings = host.Library.Catalog.Settings;
            findBar.Visibility = SelectedCategory == 10 || settings.SearchPinFindBar ? Visibility.Visible : Visibility.Collapsed;
            if (findScopes != null) findScopes.Visibility = settings.SearchBarExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (findToggle != null) findToggle.Content = Glyph(settings.SearchBarExpanded ? "" : "");
            UpdateTagChips();
            UpdateSearchSummary();
            if (categories != null) Dispatcher.BeginInvoke(new Action(ScrollRailToSelection), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void UpdateSearchSummary()
        {
            if (searchSummary == null) return;
            var settings = host.Library.Catalog.Settings;
            var scopes = new List<string>();
            if (settings.SearchDesktop) scopes.Add(Ui.Text("桌面"));
            if (settings.SearchAttached) scopes.Add(Ui.Text("附着"));
            if (settings.SearchStored) scopes.Add(Ui.Text("已收藏"));
            if (settings.SearchSleeping) scopes.Add(Ui.Text("休眠中"));
            if (settings.SearchClosed) scopes.Add(Ui.Text("已关闭"));
            if (settings.SearchRecurring) scopes.Add(Ui.Text("重复提醒"));
            var tags = (settings.SearchTagFilter ?? new List<string>()).Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)?.Name).Where(name => name != null).ToArray();
            searchSummary.Text = Ui.Text("有效范围：") + (scopes.Count == 0 ? Ui.Text("未选择范围（无结果）") : string.Join(" / ", scopes)) + " · " + Ui.Text(settings.SearchContent ? "包含正文" : "仅标题与标识符") + " · " + Ui.Text("标签：") + (tags.Length == 0 ? Ui.Text("不限") : Ui.Text("任一匹配") + " " + string.Join(" / ", tags));
        }

        private void ResetSearch()
        {
            searchTimer.Stop();
            var settings = host.Library.Catalog.Settings;
            settings.SearchDesktop = settings.SearchAttached = settings.SearchStored = settings.SearchSleeping = settings.SearchClosed = settings.SearchRecurring = settings.SearchContent = true;
            settings.SearchTagFilter = new List<string>();
            suppressSearch = true;
            try { search.Clear(); if (findScopes != null) foreach (var check in findScopes.Children.OfType<CheckBox>().Take(7)) check.IsChecked = true; }
            finally { suppressSearch = false; }
            host.Library.SaveCatalog();
            UpdateTagChips(); UpdateSearchSummary(); RefreshSearch();
        }

        private void UpdateTagChips()
        {
            if (tagChips == null) return;
            tagChips.Children.Clear();
            var settings = host.Library.Catalog.Settings;
            var tags = (settings.SearchTagFilter ?? new List<string>()).Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)).Where(t => t != null).ToList();
            if (tags.Count == 0)
            {
                tagChips.Children.Add(new TextBlock { Text = Ui.Text("标签过滤"), Foreground = UiTheme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
                return;
            }
            foreach (var tag in tags.Take(3))
            {
                tagChips.Children.Add(new Border
                {
                    Background = Ui.Brush(tag.Color), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(2, 0, 2, 0),
                    Child = new TextBlock { Text = tag.Name, FontSize = 11, Foreground = Ui.TextOn(tag.Color), VerticalAlignment = VerticalAlignment.Center }
                });
            }
            if (tags.Count > 3) tagChips.Children.Add(new TextBlock { Text = "+" + (tags.Count - 3), Foreground = UiTheme.Text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
        }

        private void OpenSearchTagFilter()
        {
            var settings = host.Library.Catalog.Settings;
            var dialog = new SearchTagWindow(host, settings.SearchTagFilter) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            settings.SearchTagFilter = dialog.SelectedTags;
            host.Library.SaveCatalog();
            UpdateTagChips();
            RefreshSearch();
        }

        private void ToggleFindBarExpanded()
        {
            var settings = host.Library.Catalog.Settings;
            settings.SearchBarExpanded = !settings.SearchBarExpanded;
            host.Library.SaveCatalog();
            ApplyFindBarLayout();
        }

        private void RefreshSearch()
        {
            UpdateSearchSummary();
            if (SelectedCategory != 10) SelectCategory(10);
            else Refresh();
        }

        private IEnumerable<Note> SearchResults()
        {
            SaveEditor();
            var settings = host.Library.Catalog.Settings;
            string term = search == null ? "" : (search.Text ?? "").Trim();
            var tags = (settings.SearchTagFilter ?? new List<string>()).Where(id => host.Library.Catalog.Tags.Any(t => t.Id == id)).ToList();
            IEnumerable<Note> results;
            if (settings.SearchContent) results = host.Library.Search(term, tags, false);
            else results = host.Library.Notes.Values.Where(n => MatchesMetadata(n, term) && MatchesTags(n, tags));
            return results.Where(n => InSearchScope(n, settings));
        }

        private static bool MatchesMetadata(Note note, string term)
        {
            if (term.Length == 0) return true;
            return (note.Title ?? "").IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0
                || (!string.IsNullOrEmpty(note.Id) && note.Id.StartsWith(term, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesTags(Note note, List<string> tags)
        {
            return tags.Count == 0 || tags.Any(id => note.Tags.Contains(id));
        }

        private static bool InSearchScope(Note note, Settings settings)
        {
            return (settings.SearchDesktop && note.State == NoteState.Desktop && note.Attachment == null)
                || (settings.SearchAttached && note.Attachment != null)
                || (settings.SearchStored && note.State == NoteState.Stored)
                || (settings.SearchSleeping && note.State == NoteState.Sleeping)
                || (settings.SearchClosed && note.State == NoteState.Closed)
                || (settings.SearchRecurring && note.State == NoteState.Recurring);
        }
    }
}
