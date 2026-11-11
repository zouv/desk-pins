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

        private Border BuildFindBar()
        {
            var outer = new Border { Background = Ui.Brush("#111111"), Padding = new Thickness(8, 6, 8, 6), Visibility = Visibility.Collapsed };
            var panel = new StackPanel();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            search = new TextBox { Height = 30, FontSize = 15, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0), ToolTip = Ui.Text("搜索") };
            search.TextChanged += (s, e) => { if (!suppressSearch && searchTimer != null) { searchTimer.Stop(); searchTimer.Start(); } };
            search.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { searchTimer.Stop(); RefreshSearch(); e.Handled = true; }
                else if (e.Key == Key.Escape) { search.Clear(); e.Handled = true; }
            };
            row.Children.Add(search);
            var searchButton = DarkButton("", "搜索", () => { searchTimer.Stop(); RefreshSearch(); });
            Grid.SetColumn(searchButton, 1); row.Children.Add(searchButton);
            tagChips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var tagContent = new StackPanel { Orientation = Orientation.Horizontal };
            tagContent.Children.Add(Glyph(""));
            tagContent.Children.Add(tagChips);
            tagFilterButton = new Button { Content = tagContent, Height = 30, MinWidth = 100, MaxWidth = 250, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(7, 0, 7, 0), Background = Ui.Brush("#374151"), BorderThickness = new Thickness(0), ToolTip = Ui.Text("标签过滤"), Focusable = false };
            tagFilterButton.Click += (s, e) => { try { OpenSearchTagFilter(); } catch (Exception ex) { Ui.Error(ex); } };
            Grid.SetColumn(tagFilterButton, 2); row.Children.Add(tagFilterButton);
            var close = DarkButton("", "关闭", CloseFindBar);
            Grid.SetColumn(close, 3); row.Children.Add(close);
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
            var pin = new CheckBox { Content = Ui.Text("固定查找栏"), IsChecked = settings.SearchPinFindBar, Foreground = Brushes.White, Margin = new Thickness(0, 3, 18, 3), VerticalContentAlignment = VerticalAlignment.Center };
            pin.Checked += (s, e) => { settings.SearchPinFindBar = true; host.Library.SaveCatalog(); ApplyFindBarLayout(); };
            pin.Unchecked += (s, e) => { settings.SearchPinFindBar = false; host.Library.SaveCatalog(); ApplyFindBarLayout(); };
            findScopes.Children.Add(pin);
            panel.Children.Add(findScopes);
            outer.Child = panel;

            searchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshSearch(); };
            UpdateTagChips();
            return outer;
        }

        private static TextBlock Glyph(string glyph) { return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }; }

        private static Button DarkButton(string glyph, string tip, Action click)
        {
            var button = new Button { Content = Glyph(glyph), Width = 34, Height = 30, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0), Background = Ui.Brush("#374151"), BorderThickness = new Thickness(0), ToolTip = Ui.Text(tip), Focusable = false };
            button.Click += (s, e) => { try { click(); } catch (Exception ex) { Ui.Error(ex); } };
            return button;
        }

        private CheckBox ScopeBox(string label, Func<bool> get, Action<bool> set)
        {
            var check = new CheckBox { Content = Ui.Text(label), IsChecked = get(), Foreground = Brushes.White, Margin = new Thickness(0, 3, 18, 3), VerticalContentAlignment = VerticalAlignment.Center };
            check.Checked += (s, e) => { set(true); host.Library.SaveCatalog(); RefreshSearch(); };
            check.Unchecked += (s, e) => { set(false); host.Library.SaveCatalog(); RefreshSearch(); };
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
        }

        private void UpdateTagChips()
        {
            if (tagChips == null) return;
            tagChips.Children.Clear();
            var settings = host.Library.Catalog.Settings;
            var tags = (settings.SearchTagFilter ?? new List<string>()).Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)).Where(t => t != null).ToList();
            if (tags.Count == 0)
            {
                tagChips.Children.Add(new TextBlock { Text = Ui.Text("标签过滤"), Foreground = Ui.Brush("#D1D5DB"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
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
            if (tags.Count > 3) tagChips.Children.Add(new TextBlock { Text = "+" + (tags.Count - 3), Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
        }

        private void OpenSearchTagFilter()
        {
            var settings = host.Library.Catalog.Settings;
            var dialog = new SearchTagWindow(host, settings.SearchTagFilter);
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

        private void CloseFindBar()
        {
            var settings = host.Library.Catalog.Settings;
            settings.SearchPinFindBar = false;
            searchTimer.Stop();
            suppressSearch = true; if (search != null) search.Clear(); suppressSearch = false;
            host.Library.SaveCatalog();
            if (SelectedCategory == 10) SelectCategory(0);
            ApplyFindBarLayout();
        }

        private void RefreshSearch()
        {
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
