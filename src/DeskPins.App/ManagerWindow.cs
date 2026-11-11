using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed partial class ManagerWindow : Window
    {
        private readonly AppHost host;
        private readonly ListBox categories, notes;
        private readonly TreeView tree;
        private readonly ComboBox order;
        private readonly ContentControl preview;
        private readonly ScrollViewer homeView;
        private readonly TextBlock count, heading, outlineHeading;
        private readonly Border outlineHost, listHost, previewHost;
        private readonly GridSplitter outlineSplitter, listSplitter;
        private readonly ColumnDefinition outlineColumn, outlineSplitterColumn, listColumn, listSplitterColumn;
        private readonly Button outlineProperties, outlineDelete;
        private readonly System.Windows.Threading.DispatcherTimer saveTimer;
        private TextEditor editor;
        private Note editing;
        private Panel inlineTitleHost;
        private bool dirty, loading, buildingTree;
        private int lastCategory = -1;
        private Point dragStart;
        private bool dragReady;
        private DateTime displayedModifiedUtc;
        private string folderFilter = "", stackFilter = "", tagFilter = "", navigationSnapshot = "";
        private readonly string[] names = { Ui.Text("首页"), Ui.Text("桌面"), Ui.Text("堆叠"), Ui.Text("附着"), Ui.Text("休眠中"), Ui.Text("重复提醒"), Ui.Text("日历"), Ui.Text("已关闭"), Ui.Text("已收藏"), Ui.Text("标签"), Ui.Text("搜索结果") };
        private static readonly string[] railIcons = { "", "", "", "", "", "", "", "", "", "", "" };
        // Category ids stay stable for persisted pane widths / sort orders; only the rail order changes.
        private static readonly int[] CategoryOrder = { 0, 1, 2, 9, 8, 3, 4, 5, 6, 7, 10 };

        public ManagerWindow(AppHost host)
        {
            this.host = host; Title = Ui.Text("DeskPins · 管理便签"); Width = 1280; Height = 760; MinWidth = 900; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Ui.FitDialog(this, 900, 520);
            saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; saveTimer.Tick += (s, e) => SaveEditor();
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(body);

            categories = new ListBox { BorderThickness = new Thickness(0), Background = Ui.Brush("#1268B3"), Padding = new Thickness(0, 4, 0, 4), ItemContainerStyle = RailStyle(), HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true };
            foreach (var category in CategoryOrder)
            {
                var item = new ListBoxItem
                {
                    Content = new TextBlock { Text = railIcons[category], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 19, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                    ToolTip = names[category], Tag = category, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 40
                };
                System.Windows.Automation.AutomationProperties.SetName(item, names[category]);
                categories.Items.Add(item);
            }
            categories.Drop += (s, e) => { var item = FindItem(e.OriginalSource as DependencyObject); if (item == null) return; int category = (int)item.Tag; if (category == 1 || category == 7 || category == 8) DropTo(e, category == 1 ? NoteState.Desktop : category == 7 ? NoteState.Closed : NoteState.Stored, ""); };
            categories.SelectionChanged += (s, e) => { if (buildingTree) return; SaveEditor(); folderFilter = stackFilter = tagFilter = ""; ApplyLayout(); BuildTree(); Refresh(); };
            Grid.SetColumn(categories, 0); body.Children.Add(categories);

            var workspace = new Grid { Background = Brushes.White };
            Grid.SetColumn(workspace, 1); body.Children.Add(workspace);
            outlineColumn = new ColumnDefinition { Width = new GridLength(0) }; outlineSplitterColumn = new ColumnDefinition { Width = new GridLength(0) };
            listColumn = new ColumnDefinition { Width = new GridLength(0) }; listSplitterColumn = new ColumnDefinition { Width = new GridLength(0) };
            workspace.ColumnDefinitions.Add(outlineColumn); workspace.ColumnDefinitions.Add(outlineSplitterColumn); workspace.ColumnDefinitions.Add(listColumn); workspace.ColumnDefinitions.Add(listSplitterColumn);
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });

            outlineHeading = new TextBlock { FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#1F2937"), Margin = new Thickness(0, 0, 0, 8) };
            var outlineTools = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var outlineCreate = SmallButton("新建"); outlineCreate.Click += (s, e) => OpenCreateMenu(outlineCreate);
            outlineProperties = SmallButton("属性…"); outlineProperties.Click += (s, e) => EditSelectedCollection();
            outlineDelete = SmallButton("删除"); outlineDelete.Click += (s, e) => DeleteSelectedCollection();
            outlineTools.Children.Add(outlineCreate); outlineTools.Children.Add(outlineProperties); outlineTools.Children.Add(outlineDelete);
            tree = new TreeView { BorderThickness = new Thickness(0), Background = Brushes.White, Padding = new Thickness(0, 2, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true };
            tree.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            tree.SelectedItemChanged += TreeSelected;
            tree.PreviewMouseLeftButtonDown += TreeMouseDown;
            tree.PreviewMouseMove += TreeMouseMove;
            tree.PreviewMouseLeftButtonUp += (s, e) => treeDragReady = false;
            tree.DragOver += TreeDragOver;
            tree.Drop += TreeDrop;
            tree.ContextMenu = BlankTreeMenu();
            var outlinePanel = new DockPanel { Margin = new Thickness(14, 14, 12, 12) };
            DockPanel.SetDock(outlineHeading, Dock.Top); outlinePanel.Children.Add(outlineHeading);
            DockPanel.SetDock(outlineTools, Dock.Top); outlinePanel.Children.Add(outlineTools);
            outlinePanel.Children.Add(tree);
            outlineHost = new Border { Background = Brushes.White, BorderBrush = Ui.Brush("#E5E7EB"), BorderThickness = new Thickness(0, 0, 1, 0), Child = outlinePanel, Visibility = Visibility.Collapsed };
            Grid.SetColumn(outlineHost, 0); workspace.Children.Add(outlineHost);
            outlineSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Ui.Brush("#E5E7EB"), ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Visibility = Visibility.Collapsed };
            outlineSplitter.DragCompleted += (s, e) => SavePaneWidths();
            Grid.SetColumn(outlineSplitter, 1); workspace.Children.Add(outlineSplitter);

            var listPanel = new DockPanel { Margin = new Thickness(14, 14, 14, 12) };
            var listHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            listHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heading = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#1F2937"), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumnSpan(heading, 2); listHeader.Children.Add(heading);
            count = new TextBlock { FontSize = 12, Foreground = Ui.Brush("#6B7280"), Margin = new Thickness(2, 8, 8, 0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(count, 1); listHeader.Children.Add(count);
            order = new ComboBox { ItemsSource = new[] { Ui.Text("修改时间 ↓"), Ui.Text("创建时间 ↓"), Ui.Text("标题 A–Z"), Ui.Text("提醒时间 ↑") }, SelectedIndex = 0, Width = 150, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 4, 0, 0) };
            order.SelectionChanged += (s, e) => Refresh(); Grid.SetColumn(order, 1); Grid.SetRow(order, 1); listHeader.Children.Add(order);
            calendarHost = new Border { Visibility = Visibility.Collapsed };
            Grid.SetRow(calendarHost, 2); Grid.SetColumnSpan(calendarHost, 2); listHeader.Children.Add(calendarHost);
            listPanel.Children.Add(listHeader);
            DockPanel.SetDock(listHeader, Dock.Top);
            notes = new ListBox { SelectionMode = SelectionMode.Extended, BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var noteItemStyle = new Style(typeof(ListBoxItem));
            noteItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            noteItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            noteItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            noteItemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            noteItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var selectedCard = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedCard.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("#EAF3FB")));
            noteItemStyle.Triggers.Add(selectedCard);
            notes.ItemContainerStyle = noteItemStyle;
            notes.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            notes.ItemTemplate = NoteCardTemplate();
            notes.SelectionChanged += (s, e) => DisplaySelected();
            notes.MouseDoubleClick += (s, e) => OpenSelected();
            notes.PreviewMouseLeftButtonDown += (s, e) => { dragReady = FindItem(e.OriginalSource as DependencyObject) != null; dragStart = e.GetPosition(notes); };
            notes.PreviewMouseLeftButtonUp += (s, e) => dragReady = false;
            notes.PreviewMouseRightButtonDown += (s, e) => { var item = FindItem(e.OriginalSource as DependencyObject); var note = item == null ? null : item.DataContext as Note; if (note != null && !notes.SelectedItems.Contains(note)) { notes.SelectedItems.Clear(); notes.SelectedItem = note; } };
            notes.PreviewMouseMove += (s, e) => { var p = e.GetPosition(notes); if (dragReady && e.LeftButton == MouseButtonState.Pressed && (Math.Abs(p.X - dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance) && Selected().Length > 0) { dragReady = false; DragDrop.DoDragDrop(notes, new DataObject("DeskPins.Notes", Selected().Select(n => n.Id).ToArray()), DragDropEffects.Move); } };
            notes.ContextMenu = Context(); listPanel.Children.Add(notes);
            listHost = new Border { Background = Brushes.White, Child = listPanel, Visibility = Visibility.Collapsed };
            Grid.SetColumn(listHost, 2); workspace.Children.Add(listHost);
            listSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Ui.Brush("#E5E7EB"), ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Visibility = Visibility.Collapsed };
            listSplitter.DragCompleted += (s, e) => SavePaneWidths();
            Grid.SetColumn(listSplitter, 3); workspace.Children.Add(listSplitter);

            previewHost = new Border { Background = Brushes.White, BorderBrush = Ui.Brush("#E5E7EB"), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(22, 18, 22, 14), Visibility = Visibility.Collapsed };
            preview = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            previewHost.Child = preview;
            homeView = new ScrollViewer { Background = Brushes.White, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed };
            var contentLayer = new Grid(); contentLayer.Children.Add(previewHost); contentLayer.Children.Add(homeView);
            Grid.SetColumn(contentLayer, 4); workspace.Children.Add(contentLayer);
            findBar = BuildFindBar();
            Grid.SetRow(findBar, 1); root.Children.Add(findBar);
            Content = root;

            host.Library.Changed += Changed;
            Closing += (s, e) => { try { SaveEditor(); SaveManagerState(); host.Library.SaveCatalog(); } catch (Exception ex) { e.Cancel = true; Ui.Error(ex); } };
            Closed += (s, e) => { saveTimer.Stop(); searchTimer.Stop(); host.Library.Changed -= Changed; };
            Deactivated += (s, e) => SaveEditor();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F2)
                {
                    if (IsOutlineCategory(SelectedCategory) && tree.IsKeyboardFocusWithin) EditSelectedCollection();
                    else { var n = notes.SelectedItem as Note; EditNoteTitle(n); }
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete && IsOutlineCategory(SelectedCategory) && tree.IsKeyboardFocusWithin) { DeleteSelectedCollection(); e.Handled = true; }
                else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { NewNoteForSelection(); e.Handled = true; }
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SelectCategory(10); BuildTree(); Refresh(); search.Focus(); e.Handled = true; }
            };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            RestoreManagerState();
        }

        private static Button SmallButton(string text) { return new Button { Content = Ui.Text(text), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2, 0, 2, 2), MinHeight = 26, FontSize = 12 }; }
        private static bool IsOutlineCategory(int category) { return category == 2 || category == 8 || category == 9; }
        private int SelectedCategory { get { var item = categories == null ? null : categories.SelectedItem as ListBoxItem; return item == null || !(item.Tag is int) ? 0 : (int)item.Tag; } }
        private void SelectCategory(int category) { if (categories != null) categories.SelectedItem = categories.Items.Cast<ListBoxItem>().FirstOrDefault(i => object.Equals(i.Tag, category)); }
        private static Style RailStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("#0B4F8A")));
            style.Triggers.Add(selected);
            return style;
        }

        private void ApplyLayout()
        {
            int category = SelectedCategory;
            bool outline = IsOutlineCategory(category), list = category != 0;
            double outlineWidth = GetOutlineWidth(category), listWidth = GetListWidth(category);
            outlineColumn.Width = outline ? new GridLength(outlineWidth) : new GridLength(0);
            outlineSplitterColumn.Width = outline ? new GridLength(5) : new GridLength(0);
            listColumn.Width = list ? new GridLength(listWidth) : new GridLength(0);
            listSplitterColumn.Width = list ? new GridLength(5) : new GridLength(0);
            outlineHost.Visibility = outline ? Visibility.Visible : Visibility.Collapsed;
            listHost.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            outlineSplitter.Visibility = outline ? Visibility.Visible : Visibility.Collapsed;
            listSplitter.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            previewHost.Visibility = category == 0 ? Visibility.Collapsed : Visibility.Visible;
            homeView.Visibility = category == 0 ? Visibility.Visible : Visibility.Collapsed;
            calendarHost.Visibility = category == 6 ? Visibility.Visible : Visibility.Collapsed;
            outlineHeading.Text = category == 8 ? names[8] : category == 2 ? names[2] : category == 9 ? names[9] : "";
            if (tree != null) tree.ContextMenu = IsOutlineCategory(category) ? BlankTreeMenu() : null;
            if (outlineProperties != null) outlineProperties.IsEnabled = outlineDelete.IsEnabled = tree.SelectedItem is TreeViewItem;
            ApplyFindBarLayout();
        }
        private double GetOutlineWidth(int category) { double value; if (host.Library.Catalog.Settings.ManagerOutlineWidths.TryGetValue(category, out value) && value >= 150) return Math.Min(420, value); return 240; }
        private double GetListWidth(int category) { double value; if (host.Library.Catalog.Settings.ManagerListWidths.TryGetValue(category, out value) && value >= 180) return Math.Min(620, value); return 340; }
        private void SavePaneWidths()
        {
            int category = SelectedCategory; if (category == 0) return;
            if (IsOutlineCategory(category)) { var width = outlineHost.ActualWidth; if (width >= 150) host.Library.Catalog.Settings.ManagerOutlineWidths[category] = width; }
            var listWidth = listHost.ActualWidth; if (listWidth >= 180) host.Library.Catalog.Settings.ManagerListWidths[category] = listWidth;
            host.Library.SaveCatalog();
        }

        private void OpenSelected()
        {
            var n = notes.SelectedItem as Note; if (n == null) return;
            if (n.Attachment != null && !host.IsAttachmentVisible(n)) new AttachWindow(host, n).ShowDialog();
            else if (n.State == NoteState.Desktop) host.Focus(n);
            else if (n.State == NoteState.Sleeping || n.State == NoteState.Recurring) new ScheduleWindow(host, n, true, n.State == NoteState.Recurring).ShowDialog();
            else if (n.Attachment != null) new AttachWindow(host, n).ShowDialog();
            else { host.Library.Move(n, NoteState.Desktop); host.Focus(n); }
        }

        private void Changed() { Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Background); }

        public void Refresh()
        {
            if (notes == null || order == null || categories == null) return;
            ApplyLayout();
            BuildTree();
            int category = SelectedCategory;
            if (lastCategory != category) { lastCategory = category; int saved; host.Library.Catalog.Settings.ManagerOrders.TryGetValue(category, out saved); order.SelectedIndex = Math.Max(0, Math.Min(3, saved)); }
            else host.Library.Catalog.Settings.ManagerOrders[category] = order.SelectedIndex;
            if (category == 0)
            {
                SaveEditor(); editing = null; notes.ItemsSource = null; preview.Content = null; inlineTitleHost = null; homeView.Content = BuildHomeDashboard(); return;
            }
            foreach (var note in host.Library.Notes.Values)
            {
                note.DisplayModifiedDates = host.Library.Catalog.Settings.ManagerModifiedDates;
                note.CompactPreview = host.Library.Catalog.Settings.CompactList;
                note.ShowId = category == 7;
                note.DisplayTags = (note.Tags ?? new List<string>()).Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)).Where(t => t != null).ToList();
            }
            if (category == 6) BuildCalendar();
            IEnumerable<Note> list = host.Library.Notes.Values;
            switch (category)
            {
                case 1: list = list.Where(n => n.State == NoteState.Desktop && n.StackId == "" && n.Attachment == null); break;
                case 2: list = list.Where(n => n.StackId != "" && (stackFilter == "" || n.StackId == stackFilter)); break;
                case 3: list = list.Where(n => n.Attachment != null && !host.IsAttachmentVisible(n)); break;
                case 4: list = list.Where(n => n.State == NoteState.Sleeping); break;
                case 5: list = list.Where(n => n.State == NoteState.Recurring); break;
                case 6: list = list.Where(n => MatchesDate(n, calendarSelected)); break;
                case 7: list = list.Where(n => n.State == NoteState.Closed); break;
                case 8: list = list.Where(n => n.State == NoteState.Stored && (folderFilter == "" || n.FolderId == folderFilter)); break;
                case 9: list = list.Where(n => tagFilter == "" ? n.Tags.Count > 0 : n.Tags.Contains(tagFilter)); break;
                case 10: list = SearchResults(); break;
            }
            if (order.SelectedIndex == 2) list = list.OrderBy(n => n.DisplayTitle); else if (order.SelectedIndex == 1) list = list.OrderByDescending(n => n.CreatedUtc); else if (order.SelectedIndex == 3) list = list.OrderBy(n => n.WakeUtc ?? n.AlarmUtc ?? DateTime.MaxValue); else list = list.OrderByDescending(n => n.ModifiedUtc);
            var items = list.ToList(); string selected = (notes.SelectedItem as Note)?.Id;
            loading = true; notes.ItemsSource = items; notes.SelectedItem = items.FirstOrDefault(n => n.Id == selected); loading = false;
            heading.Text = HeadingFor(category); count.Text = Ui.Text("数量：") + " " + items.Count;
            DisplaySelected();
        }

        private string HeadingFor(int category)
        {
            if (category == 8) { var folder = host.Library.Catalog.Folders.FirstOrDefault(f => f.Id == folderFilter); return folder == null ? names[8] : folder.Name; }
            if (category == 2) { var stack = host.Library.Catalog.Stacks.FirstOrDefault(s => s.Id == stackFilter); return stack == null ? names[2] : stack.Name; }
            if (category == 9) { var tag = host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == tagFilter); return tag == null ? names[9] : Ui.Text("标签") + ": " + tag.Name; }
            if (category == 10) { var term = (search == null ? "" : (search.Text ?? "").Trim()); return Ui.Text("搜索结果") + (term.Length == 0 ? "" : ": " + term); }
            return names[Math.Max(0, Math.Min(names.Length - 1, category))];
        }

        private FrameworkElement BuildHomeDashboard()
        {
            var panel = new StackPanel { Margin = new Thickness(30, 24, 30, 24) };
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = Ui.Text("首页"), FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#1F2937") });
            var totals = new TextBlock { Text = Ui.Text("合计"), FontSize = 19, Foreground = Ui.Brush("#1F2937"), HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(totals, 1); header.Children.Add(totals);
            panel.Children.Add(header);
            var all = host.Library.Notes.Values;
            int[] counts =
            {
                all.Count(n => n.State == NoteState.Desktop && n.StackId == "" && n.Attachment == null),
                all.Count(n => n.StackId != ""), all.Count(n => n.Attachment != null),
                all.Count(n => n.State == NoteState.Sleeping), all.Count(n => n.State == NoteState.Recurring),
                all.Count(n => n.State == NoteState.Closed), all.Count(n => n.State == NoteState.Stored)
            };
            string[] glyphs = { railIcons[1], railIcons[2], railIcons[3], railIcons[4], railIcons[5], railIcons[7], railIcons[8] };
            string[] labels = { names[1], names[2], names[3], names[4], names[5], names[7], names[8] };
            int max = Math.Max(1, counts.Max());
            var metrics = new Grid { Margin = new Thickness(0, 24, 0, 18) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            for (int i = 0; i < counts.Length; i++)
            {
                var row = new Grid { Height = 42, Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                var icon = new TextBlock { Text = glyphs[i], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Foreground = Ui.Brush("#333333"), VerticalAlignment = VerticalAlignment.Center, ToolTip = labels[i] };
                row.Children.Add(icon);
                var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8) };
                var bar = new Border { Background = Ui.Brush("#333333"), Height = 20, HorizontalAlignment = HorizontalAlignment.Left, Width = counts[i] == 0 ? 0 : Math.Min(620, Math.Max(4, counts[i] * (520.0 / max))) };
                barHost.Children.Add(bar); Grid.SetColumn(barHost, 1); row.Children.Add(barHost);
                var value = new TextBlock { Text = counts[i].ToString(), FontSize = 15, Foreground = Ui.Brush("#333333"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(value, 2); row.Children.Add(value);
                var total = new TextBlock { Text = counts[i].ToString(), FontSize = 20, Foreground = Ui.Brush("#333333"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(total, 3); row.Children.Add(total);
                var line = new Border { BorderBrush = Ui.Brush("#333333"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
                var wrapper = new Grid(); wrapper.Children.Add(row); wrapper.Children.Add(line); metrics.Children.Add(wrapper);
                metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(wrapper, i); Grid.SetColumnSpan(wrapper, 4);
            }
            panel.Children.Add(metrics);
            int totalCount = counts.Sum(), textCount = all.Count(n => n.Kind == NoteKind.Text), imageCount = all.Count(n => n.Kind == NoteKind.Image), encrypted = all.Count(n => n.Encrypted);
            long dbKb = 0; try { dbKb = new FileInfo(host.Library.Repository.FilePath).Length / 1024; } catch (IOException) { }
            var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            var db = new TextBlock { Text = Ui.Text("数据库大小：") + dbKb + "kb", FontSize = 20, Foreground = Ui.Brush("#333333"), TextWrapping = TextWrapping.Wrap }; footer.Children.Add(db);
            var enc = new TextBlock { Text = Ui.Text("加密：") + encrypted, FontSize = 20, Foreground = Ui.Brush("#333333") }; Grid.SetColumn(enc, 1); footer.Children.Add(enc);
            var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var textLine = new StackPanel { Orientation = Orientation.Horizontal }; textLine.Children.Add(new Border { Width = 52, Height = 18, Background = Ui.Brush("#333333"), Margin = new Thickness(0, 0, 10, 0) }); textLine.Children.Add(new TextBlock { Text = Ui.Text("文本：") + textCount, FontSize = 18, Foreground = Ui.Brush("#333333") }); legend.Children.Add(textLine);
            var imageLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) }; imageLine.Children.Add(new Border { Width = 52, Height = 18, Background = Ui.Brush("#BDBDBD"), Margin = new Thickness(0, 0, 10, 0) }); imageLine.Children.Add(new TextBlock { Text = Ui.Text("图片：") + imageCount, FontSize = 18, Foreground = Ui.Brush("#333333") }); legend.Children.Add(imageLine);
            Grid.SetColumn(legend, 2); footer.Children.Add(legend);
            panel.Children.Add(footer);
            var totalBlock = new TextBlock { Text = Ui.Text("合计：") + totalCount, FontSize = 16, Foreground = Ui.Brush("#6B7280"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 8) };
            panel.Children.Insert(2, totalBlock);
            return panel;
        }

        private void DisplaySelected(bool force = false)
        {
            if (loading || SelectedCategory == 0) return; var n = notes.SelectedItem as Note; if (!force && ReferenceEquals(editing, n) && (n == null || dirty || n.ModifiedUtc == displayedModifiedUtc)) return; SaveEditor(); editing = n; editor = null; dirty = false; preview.Content = null; inlineTitleHost = null; if (n == null) return;
            displayedModifiedUtc = n.ModifiedUtc;
            if (n.Encrypted && host.Library.SessionPassword == null) { preview.Content = new StackPanel { Children = { new TextBlock { Text = Ui.Text("此便签已加密"), FontSize = 18, Margin = new Thickness(10) }, Ui.Button("解锁", () => { host.Unlock(); DisplaySelected(true); }) } }; return; }
            try
            {
                host.Library.Load(n); loading = true;
                var panel = new DockPanel();
                var titleHost = new Grid();
                var title = new TextBlock { Text = n.ManagerTitle, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), ToolTip = n.Id + "\n" + Ui.Text("双击修改标题"), Cursor = Cursors.IBeam };
                title.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { BeginInlineTitleEdit(n, titleHost); e.Handled = true; } };
                titleHost.Children.Add(title);
                inlineTitleHost = titleHost;
                DockPanel.SetDock(titleHost, Dock.Top); panel.Children.Add(titleHost);
                var folder = host.Library.Catalog.Folders.FirstOrDefault(f => f.Id == n.FolderId);
                var folderLine = folder == null ? "" : Ui.Text("文件夹") + ": " + host.FolderPath(folder) + "\n";
                var info = new TextBlock { Text = folderLine + n.State + " · " + n.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "\n" + string.Join(" / ", n.Tags.Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)?.Name ?? id)), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
                DockPanel.SetDock(info, Dock.Top); panel.Children.Add(info);
                if (n.Kind == NoteKind.Text) { editor = new TextEditor { LinksClickable = host.Library.Catalog.Settings.LinksClickable, CheckboxTrailingSpace = host.Library.Catalog.Settings.CheckboxTrailingSpace }; editor.Read(n); editor.Background = Ui.Brush(n.Color); editor.TextChanged += (s, e) => { if (!loading) { dirty = true; saveTimer.Stop(); saveTimer.Start(); } }; panel.Children.Add(editor); }
                else { var image = new ImageEditor { Locked = true, MaxUndo = host.Library.Catalog.Settings.ImageUndoLevels, SaveFormat = host.Library.Catalog.Settings.ImageSaveType }; image.Read(n.Body); panel.Children.Add(new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); }
                preview.Content = panel; loading = false;
            }
            catch (Exception ex) { loading = false; preview.Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap }; }
        }

        public void FlushPending(Note note = null) { if (note == null || editing?.Id == note.Id) SaveEditor(); }
        private void EditNoteTitle(Note note)
        {
            if (note == null) return;
            if (ReferenceEquals(note, editing) && inlineTitleHost != null && inlineTitleHost.IsLoaded) { BeginInlineTitleEdit(note, inlineTitleHost); return; }
            SaveEditor();
            var title = Ui.Ask("设置标题", "标题", note.Title);
            if (title == null || title == note.Title) return;
            note.Title = title;
            host.Library.Save(note);
            Refresh();
        }
        private void BeginInlineTitleEdit(Note note, Panel titleHost)
        {
            SaveEditor();
            var box = new TextBox { Text = note.Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Padding = new Thickness(4, 2, 4, 2), BorderBrush = Ui.Brush("#93C5FD"), BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 0, 0, 6) };
            var finished = false;
            Action<bool> finish = commit =>
            {
                if (finished) return;
                finished = true;
                if (commit)
                {
                    var text = box.Text ?? "";
                    if (text != note.Title)
                    {
                        note.Title = text;
                        host.Library.Save(note);
                    }
                }
                DisplaySelected(true);
            };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { finish(true); e.Handled = true; } else if (e.Key == Key.Escape) { finish(false); e.Handled = true; } };
            box.LostFocus += (s, e) => finish(true);
            box.Loaded += (s, e) => box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
            titleHost.Children.Clear(); titleHost.Children.Add(box);
        }
        private void SaveEditor() { saveTimer.Stop(); if (!dirty || editing == null || editor == null || !host.Library.Notes.ContainsKey(editing.Id)) return; editor.Write(editing); host.Library.Save(editing); dirty = false; displayedModifiedUtc = editing.ModifiedUtc; host.ReloadWindow(editing); }
        private Note[] Selected() { return notes.SelectedItems.Cast<Note>().ToArray(); }
        private void Each(Action<Note> action) { SaveEditor(); try { foreach (var n in Selected()) action(n); Refresh(); } catch (Exception ex) { Ui.Error(ex); } }
        private void CloseSelected() { if (host.Library.Catalog.Settings.ConfirmClose && !Ui.Confirm(Ui.Text("关闭便签？"))) return; Each(n => host.Library.Move(n, NoteState.Closed)); }
        private ContextMenu Context() { var m = new ContextMenu(); m.Items.Add(Ui.Item("新建便签", NewNoteForSelection)); m.Items.Add(Ui.Item("设置标题…", () => EditNoteTitle(notes.SelectedItem as Note))); m.Items.Add(Ui.Item("恢复 / 唤醒", () => Each(n => host.Library.Move(n, NoteState.Desktop)))); m.Items.Add(Ui.Item("关闭", CloseSelected)); m.Items.Add(Ui.Item("收藏", () => Each(n => host.Library.Move(n, NoteState.Stored)))); m.Items.Add(Ui.Item("复制", () => Each(n => { host.FlushNote(n); host.Library.Copy(n); }))); m.Items.Add(Ui.Item("标签…", () => host.AssignTags(Selected()))); m.Items.Add(Ui.Item("Ghost 开启", () => Each(n => { n.Ghost = true; host.Library.Save(n); }))); m.Items.Add(Ui.Item("Ghost 关闭", () => Each(n => { n.Ghost = false; host.Library.Save(n); }))); m.Items.Add(Ui.Item("分离附着", () => Each(n => { n.Attachment = null; host.Library.Save(n); }))); m.Items.Add(Ui.Item("导出…", () => Each(host.Export))); m.Items.Add(Ui.Item("属性…", () => { var n = notes.SelectedItem as Note; if (n != null) MessageBox.Show(Json.Text(n), Ui.Text("便签属性")); })); m.Items.Add(Ui.Item("永久删除…", () => { if (Ui.Confirm(Ui.Text("永久删除选中的便签？"))) Each(host.Library.Delete); })); return m; }

        private void SelectNoteInList(string id)
        {
            var item = notes.Items.Cast<Note>().FirstOrDefault(n => n.Id == id);
            if (item != null) { notes.SelectedItem = item; notes.ScrollIntoView(item); }
        }

        private void SaveManagerState()
        {
            var settings = host.Library.Catalog.Settings;
            settings.ManagerCategory = SelectedCategory;
            settings.ManagerFolderFilter = folderFilter ?? "";
            settings.ManagerStackFilter = stackFilter ?? "";
            settings.ManagerTagFilter = tagFilter ?? "";
            settings.ManagerSelectedNoteId = SelectedCategory == 0 ? "" : (notes.SelectedItem as Note)?.Id ?? "";
            settings.ManagerCalendarDate = calendarSelected;
        }

        private void RestoreManagerState()
        {
            var settings = host.Library.Catalog.Settings;
            int category = settings.ManagerCategory;
            if (category < 0 || category > 10) category = 0;
            calendarSelected = settings.ManagerCalendarDate == DateTime.MinValue ? DateTime.Today : settings.ManagerCalendarDate.Date;
            calendarMonth = new DateTime(calendarSelected.Year, calendarSelected.Month, 1);
            SelectCategory(category);
            // The category handler clears filters; restore them after it has run.
            folderFilter = settings.ManagerFolderFilter ?? "";
            stackFilter = settings.ManagerStackFilter ?? "";
            tagFilter = settings.ManagerTagFilter ?? "";
            ApplyLayout(); BuildTree(); Refresh();
            if (category != 0 && !string.IsNullOrEmpty(settings.ManagerSelectedNoteId)) SelectNoteInList(settings.ManagerSelectedNoteId);
        }

        private static DataTemplate NoteCardTemplate()
        {
            var card = new FrameworkElementFactory(typeof(Border));
            card.SetValue(Border.BackgroundProperty, Brushes.White); card.SetValue(Border.BorderBrushProperty, Ui.Brush("#E5E7EB")); card.SetValue(Border.BorderThicknessProperty, new Thickness(1)); card.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); card.SetValue(Border.PaddingProperty, new Thickness(8)); card.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
            card.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Identifier"));
            var panel = new FrameworkElementFactory(typeof(DockPanel));
            var thumb = new FrameworkElementFactory(typeof(Border));
            thumb.SetValue(FrameworkElement.WidthProperty, 64d); thumb.SetValue(FrameworkElement.HeightProperty, 48d); thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); thumb.SetValue(Border.BorderBrushProperty, Ui.Brush("#D1D5DB")); thumb.SetValue(Border.BorderThicknessProperty, new Thickness(1)); thumb.SetBinding(Border.BackgroundProperty, new Binding("ColorHex") { Converter = new ColorBrushConverter() }); thumb.SetValue(DockPanel.DockProperty, Dock.Left);
            var previewText = new FrameworkElementFactory(typeof(TextBlock)); previewText.SetBinding(TextBlock.TextProperty, new Binding("DisplayPreview")); previewText.SetValue(TextBlock.FontSizeProperty, 6d); previewText.SetValue(TextBlock.ForegroundProperty, Ui.Brush("#4B5563")); previewText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); previewText.SetValue(TextBlock.MarginProperty, new Thickness(3)); previewText.SetValue(FrameworkElement.MaxHeightProperty, 42d); previewText.SetValue(FrameworkElement.ClipToBoundsProperty, true);
            thumb.AppendChild(previewText); panel.AppendChild(thumb);
            var right = new FrameworkElementFactory(typeof(StackPanel)); right.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
            var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding("ManagerTitle")); title.SetValue(TextBlock.FontSizeProperty, 14d); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); right.AppendChild(title);
            var summary = new FrameworkElementFactory(typeof(TextBlock)); summary.SetBinding(TextBlock.TextProperty, new Binding("DisplayPreview")); summary.SetValue(TextBlock.FontSizeProperty, 12d); summary.SetValue(TextBlock.ForegroundProperty, Ui.Brush("#6B7280")); summary.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); summary.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 3)); right.AppendChild(summary);
            var tagsHost = new FrameworkElementFactory(typeof(ItemsControl)); tagsHost.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("DisplayTags")); tagsHost.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 2));
            var tagPanel = new FrameworkElementFactory(typeof(WrapPanel));
            tagsHost.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(tagPanel));
            var tagBorder = new FrameworkElementFactory(typeof(Border));
            tagBorder.SetBinding(Border.BackgroundProperty, new Binding("ColorHex") { Converter = new ColorBrushConverter() }); tagBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); tagBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)); tagBorder.SetValue(Border.PaddingProperty, new Thickness(5, 1, 5, 1));
            var tagName = new FrameworkElementFactory(typeof(TextBlock)); tagName.SetBinding(TextBlock.TextProperty, new Binding("DisplayName")); tagName.SetBinding(TextBlock.ForegroundProperty, new Binding("ColorHex") { Converter = new ContrastBrushConverter() }); tagName.SetValue(TextBlock.FontSizeProperty, 10d);
            tagBorder.AppendChild(tagName); tagsHost.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate(typeof(Tag)) { VisualTree = tagBorder });
            right.AppendChild(tagsHost);
            var date = new FrameworkElementFactory(typeof(TextBlock)); date.SetBinding(TextBlock.TextProperty, new Binding("DateLabel")); date.SetValue(TextBlock.FontSizeProperty, 11d); date.SetValue(TextBlock.ForegroundProperty, Ui.Brush("#9CA3AF")); right.AppendChild(date);
            panel.AppendChild(right);
            card.AppendChild(panel);
            return new DataTemplate(typeof(Note)) { VisualTree = card };
        }

        private sealed class ColorBrushConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { return Ui.Brush(value as string); }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotSupportedException(); }
        }

        private sealed class ContrastBrushConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { return Ui.TextOn(value as string); }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotSupportedException(); }
        }
    }
}
