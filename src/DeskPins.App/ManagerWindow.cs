using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly Grid workspace;
        private readonly TextBlock selectionSummary;
        private readonly Button viewButton, restoreButton, closeButton;
        private readonly Border listEmpty;
        private TextBlock saveFeedback;
        private bool refreshing;
        private string refreshError;
        private readonly Border outlineHost, listHost, previewHost;
        private readonly Thumb outlineSplitter;
        private readonly GridSplitter listSplitter;
        private readonly ColumnDefinition outlineColumn, outlineSplitterColumn, listColumn, listSplitterColumn, previewColumn;
        private readonly Button outlineProperties, outlineDelete;
        private readonly System.Windows.Threading.DispatcherTimer saveTimer;
        private TextEditor editor;
        private Note editing;
        private Panel inlineTitleHost;
        private bool dirty, loading, buildingTree, outlineFitPending, preservePaneWidths, applyingThinScrollBars;
        private int paneLayoutCategory = -1;
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
            UiTheme.Apply(this);
            Resources[typeof(ScrollBar)] = UiTheme.ThinScrollBarStyle();
            Ui.FitDialog(this, 900, 520);
            saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; saveTimer.Tick += (s, e) => { try { SaveEditor(); } catch (Exception ex) { Ui.Error(ex, this); } };
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(body);

            categories = new ListBox { BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = UiTheme.Border, Background = UiTheme.Background, Padding = new Thickness(1, 8, 1, 4), ItemContainerStyle = RailStyle(), HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true };
            categories.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            foreach (var category in CategoryOrder)
            {
                var icon = new TextBlock
                {
                    Text = railIcons[category], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30,
                    Foreground = UiTheme.Accent, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
                };
                var caption = new TextBlock
                {
                    Text = category == 10 ? "Search" : names[category], FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 40, Margin = new Thickness(0, 2, 0, 0)
                };
                caption.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
                content.Children.Add(icon); content.Children.Add(caption);
                var item = new ListBoxItem
                {
                    Content = content, ToolTip = names[category], Tag = category,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 56
                };
                System.Windows.Automation.AutomationProperties.SetName(item, names[category]);
                categories.Items.Add(item);
            }
            categories.Drop += (s, e) => { var item = FindItem(e.OriginalSource as DependencyObject); if (item == null) return; int category = (int)item.Tag; if (category == 1 || category == 7 || category == 8) DropTo(e, category == 1 ? NoteState.Desktop : category == 7 ? NoteState.Closed : NoteState.Stored, ""); };
            categories.SelectionChanged += (s, e) =>
            {
                if (buildingTree) return;
                int previous = lastCategory;
                SaveEditor();
                if (previous >= 0 && previous != SelectedCategory) SaveCategoryState(previous);
                RestoreCategoryState(SelectedCategory);
                ApplyLayout(); BuildTree(); Refresh();
            };
            categories.SizeChanged += (s, e) => ScrollRailToSelection();
            Grid.SetColumn(categories, 0); body.Children.Add(categories);

            workspace = new Grid { Background = UiTheme.Surface };
            workspace.SizeChanged += (s, e) => ConstrainPaneWidths();
            Grid.SetColumn(workspace, 1); body.Children.Add(workspace);
            outlineColumn = new ColumnDefinition { Width = new GridLength(0) }; outlineSplitterColumn = new ColumnDefinition { Width = new GridLength(0) };
            listColumn = new ColumnDefinition { Width = new GridLength(0) }; listSplitterColumn = new ColumnDefinition { Width = new GridLength(0) };
            workspace.ColumnDefinitions.Add(outlineColumn); workspace.ColumnDefinitions.Add(outlineSplitterColumn); workspace.ColumnDefinitions.Add(listColumn); workspace.ColumnDefinitions.Add(listSplitterColumn);
            previewColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 };
            workspace.ColumnDefinitions.Add(previewColumn);

            outlineHeading = new TextBlock { FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8) };
            var outlineTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var outlineCreate = IconButton(IconNew, "新建…", () => { });
            outlineCreate.Click += (s, e) => OpenCreateMenu(outlineCreate);
            outlineProperties = IconButton(IconSettings, "属性…", EditSelectedCollection);
            outlineDelete = IconButton(IconDelete, "删除", DeleteSelectedCollection, danger: true);
            outlineTools.Children.Add(outlineCreate); outlineTools.Children.Add(outlineProperties); outlineTools.Children.Add(outlineDelete);
            tree = new TreeView { BorderThickness = new Thickness(0), Background = UiTheme.Surface, Padding = new Thickness(0, 2, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true, ItemContainerStyle = OutlineTreeStyle() };
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
            outlineHost = new Border { Background = UiTheme.Surface, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 1, 0), Child = outlinePanel, Visibility = Visibility.Collapsed };
            Grid.SetColumn(outlineHost, 0); workspace.Children.Add(outlineHost);
            outlineSplitter = PaneSplitter();
            outlineSplitter.DragStarted += (s, e) => preservePaneWidths = true;
            outlineSplitter.DragDelta += (s, e) => ResizeOutlinePane(e.HorizontalChange);
            outlineSplitter.DragCompleted += (s, e) => SavePaneWidths();
            Grid.SetColumn(outlineSplitter, 1); workspace.Children.Add(outlineSplitter);

            var listPanel = new DockPanel { Margin = new Thickness(14, 14, 14, 12) };
            var listHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            listHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listHeader.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            listHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var headingLine = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headingLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headingLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
            count = new TextBlock { FontSize = 12, Foreground = UiTheme.Muted, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
            headingLine.Children.Add(heading); Grid.SetColumn(count, 1); headingLine.Children.Add(count);
            Grid.SetColumnSpan(headingLine, 2); listHeader.Children.Add(headingLine);
            var orderOptions = new[] { Ui.Text("修改时间 ↓"), Ui.Text("创建时间 ↓"), Ui.Text("标题 A–Z"), Ui.Text("提醒时间 ↑") };
            double orderWidth = 80;
            foreach (var option in orderOptions)
            {
                var measured = new FormattedText(option, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                orderWidth = Math.Max(orderWidth, Math.Ceiling(measured.WidthIncludingTrailingWhitespace) + 30);
            }
            orderWidth = Math.Min(96, orderWidth);
            order = new ComboBox
            {
                ItemsSource = orderOptions, SelectedIndex = 0,
                Width = orderWidth, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0),
                Background = UiTheme.Surface, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1), Foreground = UiTheme.Text,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12, Style = UiTheme.ComboBoxStyle()
            };
            System.Windows.Automation.AutomationProperties.SetName(order, Ui.Text("排序"));
            order.SelectionChanged += (s, e) => Refresh(); Grid.SetColumn(order, 1); Grid.SetRow(order, 1); listHeader.Children.Add(order);
            // The compact calendar is fully visible; do not add a nested scrollbar.
            calendarHost = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
            Grid.SetRow(calendarHost, 2); Grid.SetColumnSpan(calendarHost, 2); listHeader.Children.Add(calendarHost);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            actions.Children.Add(IconButton(IconNew, "新建便签", NewNoteForSelection, primary: true));
            viewButton = IconButton(IconEdit, "查看 / 编辑", OpenSelected);
            restoreButton = IconButton(IconRestore, "恢复到桌面", RestoreSelected);
            closeButton = IconButton(IconClose, "关闭便签（可恢复）", CloseSelected);
            actions.Children.Add(viewButton); actions.Children.Add(restoreButton); actions.Children.Add(closeButton);
            Grid.SetRow(actions, 1); Grid.SetColumn(actions, 0); listHeader.Children.Add(actions);
            listPanel.Children.Add(listHeader);
            DockPanel.SetDock(listHeader, Dock.Top);
            selectionSummary = new TextBlock { Foreground = UiTheme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 0, 8) };
            DockPanel.SetDock(selectionSummary, Dock.Top); listPanel.Children.Add(selectionSummary);
            notes = new ListBox { SelectionMode = SelectionMode.Extended, BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var noteItemStyle = new Style(typeof(ListBoxItem));
            noteItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            noteItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            noteItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            noteItemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            noteItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var selectedCard = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedCard.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Selected));
            noteItemStyle.Triggers.Add(selectedCard);
            notes.ItemContainerStyle = noteItemStyle;
            notes.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            notes.ItemTemplate = NoteCardTemplate();
            notes.SelectionChanged += (s, e) => { UpdateSelectionSummary(); DisplaySelected(); RememberSelection(); };
            notes.MouseDoubleClick += (s, e) => OpenSelected();
            notes.PreviewMouseLeftButtonDown += (s, e) => { dragReady = FindItem(e.OriginalSource as DependencyObject) != null; dragStart = e.GetPosition(notes); };
            notes.PreviewMouseLeftButtonUp += (s, e) => dragReady = false;
            notes.PreviewMouseRightButtonDown += (s, e) => { var item = FindItem(e.OriginalSource as DependencyObject); var note = item == null ? null : item.DataContext as Note; if (note != null && !notes.SelectedItems.Contains(note)) { notes.SelectedItems.Clear(); notes.SelectedItem = note; } };
            notes.PreviewMouseMove += (s, e) => { var p = e.GetPosition(notes); if (dragReady && e.LeftButton == MouseButtonState.Pressed && (Math.Abs(p.X - dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance) && Selected().Length > 0) { dragReady = false; DragDrop.DoDragDrop(notes, new DataObject("DeskPins.Notes", Selected().Select(n => n.Id).ToArray()), DragDropEffects.Move); } };
            notes.ContextMenu = Context();
            notes.ContextMenuOpening += (s, e) => notes.ContextMenu = Context();
            var noteLayer = new Grid(); noteLayer.Children.Add(notes);
            listEmpty = new Border { Background = UiTheme.Surface, Padding = new Thickness(0), Visibility = Visibility.Collapsed };
            noteLayer.Children.Add(listEmpty); listPanel.Children.Add(noteLayer);
            listHost = new Border { Background = UiTheme.Surface, Child = listPanel, Visibility = Visibility.Collapsed };
            Grid.SetColumn(listHost, 2); workspace.Children.Add(listHost);
            listSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = UiTheme.Border, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Visibility = Visibility.Collapsed };
            listSplitter.DragStarted += (s, e) => preservePaneWidths = true;
            listSplitter.DragCompleted += (s, e) => SavePaneWidths();
            Grid.SetColumn(listSplitter, 3); workspace.Children.Add(listSplitter);

            previewHost = new Border { Background = UiTheme.Surface, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(22, 18, 22, 14), Visibility = Visibility.Collapsed };
            preview = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            previewHost.Child = preview;
            homeView = new ScrollViewer { Background = UiTheme.Surface, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed };
            var contentLayer = new Grid(); contentLayer.Children.Add(previewHost); contentLayer.Children.Add(homeView);
            Grid.SetColumn(contentLayer, 4); workspace.Children.Add(contentLayer);
            findBar = BuildFindBar();
            Grid.SetRow(findBar, 1); root.Children.Add(findBar);
            Content = root;

            host.Library.Changed += Changed;
            LayoutUpdated += (s, e) => ApplyThinScrollBars();
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(ApplyThinScrollBars), System.Windows.Threading.DispatcherPriority.Loaded);
            Closing += (s, e) => { if (host.StorageFailed) return; try { SaveEditor(); SaveManagerState(); host.Library.SaveCatalog(); } catch (Exception ex) { e.Cancel = true; Ui.Error(ex, this); } };
            Closed += (s, e) => { saveTimer.Stop(); searchTimer.Stop(); host.Library.Changed -= Changed; };
            Deactivated += (s, e) => { try { SaveEditor(); } catch (Exception ex) { Ui.Error(ex, this); } };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F2)
                {
                    if (IsOutlineCategory(SelectedCategory) && tree.IsKeyboardFocusWithin) BeginInlineCollectionRename();
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

        private const string IconNew = "", IconEdit = "", IconClose = "", IconSettings = "", IconDelete = "";
        private const string IconRestore = "", IconRetry = "", IconRename = "", IconInfo = "", IconClock = "", IconAttach = "";
        private static Button IconButton(string glyph, string label, Action click, bool primary = false, bool danger = false)
        {
            var button = primary ? Ui.PrimaryButton("", click) : danger ? Ui.DangerButton("", click) : Ui.Button("", click);
            button.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            button.Width = 32; button.Height = 30; button.MinWidth = 32; button.MinHeight = 30;
            button.Padding = new Thickness(0); button.Margin = new Thickness(0, 0, 4, 0);
            button.ToolTip = Ui.Text(label);
            System.Windows.Automation.AutomationProperties.SetName(button, Ui.Text(label));
            System.Windows.Controls.ToolTipService.SetShowOnDisabled(button, true);
            return button;
        }
        private static bool IsOutlineCategory(int category) { return category == 2 || category == 8 || category == 9; }
        private int SelectedCategory { get { var item = categories == null ? null : categories.SelectedItem as ListBoxItem; return item == null || !(item.Tag is int) ? 0 : (int)item.Tag; } }
        private void SaveCategoryState(int category)
        {
            if (category <= 0) return;
            var settings = host.Library.Catalog.Settings;
            if (IsOutlineCategory(category) && outlineHost != null && outlineHost.ActualWidth >= 100) settings.ManagerOutlineWidths[category] = outlineHost.ActualWidth;
            if (listHost != null && listHost.ActualWidth >= 180) settings.ManagerListWidths[category] = listHost.ActualWidth;
            var ids = notes == null ? new List<string>() : Selected().Select(n => n.Id).ToList();
            if (ids.Count == 0) settings.ManagerSelectedNoteIds.Remove(category); else settings.ManagerSelectedNoteIds[category] = ids;
            string filter = category == 8 ? folderFilter : category == 2 ? stackFilter : category == 9 ? tagFilter : "";
            if (string.IsNullOrEmpty(filter)) settings.ManagerFilters.Remove(category); else settings.ManagerFilters[category] = filter;
        }

        private void RestoreCategoryState(int category)
        {
            folderFilter = stackFilter = tagFilter = "";
            string filter;
            if (!host.Library.Catalog.Settings.ManagerFilters.TryGetValue(category, out filter) || string.IsNullOrEmpty(filter)) return;
            if (category == 8) folderFilter = filter; else if (category == 2) stackFilter = filter; else if (category == 9) tagFilter = filter;
        }

        private void RememberSelection()
        {
            if (loading || notes == null) return;
            int category = SelectedCategory; if (category <= 0) return;
            var ids = Selected().Select(n => n.Id).ToList(); var settings = host.Library.Catalog.Settings;
            if (ids.Count == 0) settings.ManagerSelectedNoteIds.Remove(category); else settings.ManagerSelectedNoteIds[category] = ids;
        }

        private void SelectCategory(int category)
        {
            if (categories == null) return;
            var item = categories.Items.Cast<ListBoxItem>().FirstOrDefault(i => object.Equals(i.Tag, category));
            categories.SelectedItem = item;
            ScrollRailToSelection();
        }
        private void ScrollRailToSelection() { if (categories != null && categories.SelectedItem != null) categories.ScrollIntoView(categories.SelectedItem); }
        private static Style RailStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.ForegroundProperty, UiTheme.Muted));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Surface));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, UiTheme.Accent));
            selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(selected);
            return style;
        }

        private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }

        private void ApplyThinScrollBars()
        {
            if (applyingThinScrollBars) return;
            applyingThinScrollBars = true;
            try
            {
                foreach (var bar in Visuals(this).OfType<ScrollBar>())
                {
                    if (bar.Orientation == Orientation.Vertical) { if (bar.Width != 6) bar.Width = 6; }
                    else if (bar.Height != 6) bar.Height = 6;
                }
            }
            finally { applyingThinScrollBars = false; }
        }

        private static Style OutlineTreeStyle()
        {
            var style = new Style(typeof(TreeViewItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, UiTheme.Text));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 2, 2, 2)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            var toggleTemplate = new ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton));
            var toggleRoot = new FrameworkElementFactory(typeof(TextBlock)); toggleRoot.Name = "Arrow";
            toggleRoot.SetValue(TextBlock.TextProperty, new Binding("IsChecked") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Converter = new ExpandGlyphConverter() });
            toggleRoot.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            toggleRoot.SetValue(TextBlock.FontSizeProperty, 9d);
            toggleRoot.SetValue(TextBlock.ForegroundProperty, UiTheme.Muted);
            toggleRoot.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            toggleRoot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            toggleTemplate.VisualTree = toggleRoot;

            var template = new ControlTemplate(typeof(TreeViewItem));
            var root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(FrameworkElement.WidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var header = new FrameworkElementFactory(typeof(System.Windows.Controls.Border)); header.Name = "Bd";
            header.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            header.SetValue(System.Windows.Controls.Border.BorderBrushProperty, Brushes.Transparent);
            header.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            header.SetValue(System.Windows.Controls.Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var headerContent = new FrameworkElementFactory(typeof(DockPanel));
            var expander = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.ToggleButton)); expander.Name = "Expander";
            expander.SetValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("IsExpanded") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
            expander.SetValue(Control.TemplateProperty, toggleTemplate);
            expander.SetValue(FrameworkElement.WidthProperty, 18d);
            expander.SetValue(FrameworkElement.HeightProperty, 18d);
            expander.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            expander.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            expander.SetValue(UIElement.FocusableProperty, false);
            expander.SetValue(DockPanel.DockProperty, Dock.Left); headerContent.AppendChild(expander);
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.Name = "PART_Header";
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            headerContent.AppendChild(content); header.AppendChild(headerContent); root.AppendChild(header);

            var items = new FrameworkElementFactory(typeof(ItemsPresenter)); items.Name = "ItemsHost";
            items.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            root.AppendChild(items); template.VisualTree = root;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, UiTheme.Background) { TargetName = "Bd" }); template.Triggers.Add(hover);
            var selected = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, UiTheme.Selected) { TargetName = "Bd" });
            selected.Setters.Add(new Setter(Control.ForegroundProperty, UiTheme.Text)); template.Triggers.Add(selected);
            var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focused.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, UiTheme.Accent) { TargetName = "Bd" }); template.Triggers.Add(focused);
            var collapsed = new Trigger { Property = TreeViewItem.IsExpandedProperty, Value = false };
            collapsed.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed) { TargetName = "ItemsHost" }); template.Triggers.Add(collapsed);
            var leaf = new Trigger { Property = TreeViewItem.HasItemsProperty, Value = false };
            leaf.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Hidden) { TargetName = "Expander" }); template.Triggers.Add(leaf);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Thumb PaneSplitter()
        {
            var thumb = new Thumb { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = UiTheme.Border, Cursor = Cursors.SizeWE, Focusable = true, Visibility = Visibility.Collapsed };
            var template = new ControlTemplate(typeof(Thumb));
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Accent)); template.Triggers.Add(hover);
            thumb.Template = template;
            return thumb;
        }

        private void ResizeOutlinePane(double delta)
        {
            if (workspace == null || !IsOutlineCategory(SelectedCategory)) return;
            double total = outlineColumn.ActualWidth + listColumn.ActualWidth;
            if (total <= 0) return;
            double maxList = Math.Max(260, workspace.ActualWidth - 10 - 220 - 100);
            double minOutline = Math.Max(100, total - maxList);
            double maxOutline = Math.Max(minOutline, Math.Min(420, total - 260));
            double target = Math.Max(minOutline, Math.Min(maxOutline, outlineColumn.ActualWidth + delta));
            outlineColumn.MinWidth = 100; outlineColumn.MaxWidth = 420;
            outlineColumn.Width = new GridLength(target);
            listColumn.MinWidth = 260; listColumn.MaxWidth = maxList;
            listColumn.Width = new GridLength(Math.Max(260, total - target));
            outlineFitPending = false;
            preservePaneWidths = true;
        }

        private void ApplyLayout()
        {
            int category = SelectedCategory;
            bool outline = IsOutlineCategory(category), list = category != 0;
            bool preserve = preservePaneWidths && paneLayoutCategory == category;
            paneLayoutCategory = category;
            if (!preserve)
            {
                double outlineWidth = GetOutlineWidth(category), listWidth = GetListWidth(category);
                bool hasSavedOutline = host.Library.Catalog.Settings.ManagerOutlineWidths.ContainsKey(category);
                outlineColumn.MinWidth = listColumn.MinWidth = 0;
                outlineColumn.MaxWidth = listColumn.MaxWidth = double.PositiveInfinity;
                outlineColumn.MinWidth = outline ? 100 : 0;
                outlineColumn.MaxWidth = outline ? 420 : 0;
                if (outline && hasSavedOutline)
                {
                    outlineColumn.Width = new GridLength(Math.Min(420, Math.Max(100, outlineWidth)));
                    outlineFitPending = false;
                }
                else
                {
                    outlineColumn.Width = outline ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                    outlineFitPending = outline;
                }
                listColumn.Width = list ? new GridLength(listWidth) : new GridLength(0);
                preservePaneWidths = false;
            }
            else
            {
                outlineColumn.MinWidth = outline ? 100 : 0;
                outlineColumn.MaxWidth = outline ? 420 : 0;
                outlineFitPending = false;
                if (list) listColumn.MinWidth = 260;
            }
            outlineSplitterColumn.Width = outline ? new GridLength(5) : new GridLength(0);
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
            if (outlineProperties != null) outlineProperties.IsEnabled = outlineDelete.IsEnabled = tree.SelectedItem is TreeViewItem node && !(node.Tag is string);
            ConstrainPaneWidths();
            ApplyFindBarLayout();
        }
        private double GetOutlineWidth(int category) { double value; if (host.Library.Catalog.Settings.ManagerOutlineWidths.TryGetValue(category, out value) && value >= 100) return Math.Min(420, value); return 240; }
        private double GetListWidth(int category) { double value; if (host.Library.Catalog.Settings.ManagerListWidths.TryGetValue(category, out value) && value >= 180) return Math.Min(620, value); return 340; }
        private void ConstrainPaneWidths()
        {
            if (workspace == null || workspace.ActualWidth <= 0 || SelectedCategory == 0) return;
            bool outline = IsOutlineCategory(SelectedCategory);
            double splitters = outline ? 10 : 5;
            double maxList = Math.Max(260, workspace.ActualWidth - splitters - 220 - 100);
            listColumn.MinWidth = 260;
            listColumn.MaxWidth = maxList;
            if (listColumn.Width.Value > maxList) listColumn.Width = new GridLength(maxList);
            if (outline && !outlineFitPending && outlineColumn.Width.IsAbsolute)
            {
                double maxOutline = Math.Max(100, workspace.ActualWidth - splitters - listColumn.Width.Value - 220);
                if (outlineColumn.Width.Value > maxOutline) outlineColumn.Width = new GridLength(maxOutline);
            }
            if (!outline || !outlineFitPending) return;
            double available = Math.Max(370, workspace.ActualWidth - splitters - listColumn.Width.Value);
            double target = Math.Max(GetOutlineWidth(SelectedCategory), available * .5);
            target = Math.Max(100, Math.Min(420, Math.Min(target, available - 220)));
            outlineColumn.Width = new GridLength(Math.Max(1, target), GridUnitType.Star);
            previewColumn.Width = new GridLength(Math.Max(1, available - target), GridUnitType.Star);
            outlineFitPending = false;
        }
        private void SavePaneWidths()
        {
            ConstrainPaneWidths();
            int category = SelectedCategory; if (category == 0) return;
            if (IsOutlineCategory(category)) { var width = outlineHost.ActualWidth; if (width >= 100) host.Library.Catalog.Settings.ManagerOutlineWidths[category] = width; }
            var listWidth = listHost.ActualWidth; if (listWidth >= 180) host.Library.Catalog.Settings.ManagerListWidths[category] = listWidth;
            host.Library.SaveCatalog();
        }

        private void OpenSelected()
        {
            var n = notes.SelectedItem as Note; if (n == null) return;
            DisplaySelected(true);
            if (n.State == NoteState.Desktop && n.Attachment == null) host.Focus(n);
            else if (editor != null) editor.Focus();
        }

        private void RestoreSelected()
        {
            Each(n => { host.FlushNote(n); host.Library.Move(n, NoteState.Desktop); });
        }

        private void Changed() { Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Background); }

        public void Refresh()
        {
            if (refreshing || notes == null || order == null || categories == null) return;
            refreshing = true;
            try { refreshError = null; RefreshCore(); }
            catch (Exception ex)
            {
                refreshError = ex.Message;
                heading.Text = HeadingFor(SelectedCategory); heading.ToolTip = heading.Text;
                count.Text = Ui.Text("加载失败");
                listEmpty.Child = EmptyState("无法加载便签列表", refreshError, "重试", Refresh);
                listEmpty.Visibility = Visibility.Visible;
            }
            finally { loading = false; refreshing = false; }
        }

        private void RefreshCore()
        {
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
            var items = list.ToList();
            List<string> savedSelection;
            bool hasSavedSelection = host.Library.Catalog.Settings.ManagerSelectedNoteIds.TryGetValue(category, out savedSelection) && savedSelection != null;
            string selected = hasSavedSelection && savedSelection.Count > 0 ? savedSelection[0] : (notes.SelectedItem as Note)?.Id;
            var selectedIds = hasSavedSelection ? new HashSet<string>(savedSelection) : new HashSet<string>(Selected().Select(n => n.Id));
            loading = true;
            try
            {
                notes.ItemsSource = items;
                notes.SelectedItem = items.FirstOrDefault(n => n.Id == selected) ?? items.FirstOrDefault(n => selectedIds.Contains(n.Id));
                foreach (var item in items.Where(n => selectedIds.Contains(n.Id))) if (!notes.SelectedItems.Contains(item)) notes.SelectedItems.Add(item);
            }
            finally { loading = false; }
            heading.Text = HeadingFor(category); heading.FontSize = category == 6 ? 20 : 24; heading.ToolTip = heading.Text; count.Text = Ui.Text("数量：") + " " + items.Count;
            UpdateSelectionSummary();
            listEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0) listEmpty.Child = EmptyState(category == 10 ? "没有匹配的便签" : "此分类暂无便签", category == 10 ? "试试其他关键词，或重置搜索范围与标签。" : "新建便签，或从其他分类移动便签到这里。", category == 10 ? "重置搜索" : "新建便签", category == 10 ? (Action)ResetSearch : NewNoteForSelection);
            DisplaySelected();
            RememberSelection();
        }

        private string HeadingFor(int category)
        {
            if (category == 8) { var folder = host.Library.Catalog.Folders.FirstOrDefault(f => f.Id == folderFilter); return folder == null ? names[8] : folder.Name; }
            if (category == 2) { var stack = host.Library.Catalog.Stacks.FirstOrDefault(s => s.Id == stackFilter); return stack == null ? names[2] : stack.Name; }
            if (category == 9) { var tag = host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == tagFilter); return tag == null ? names[9] : Ui.Text("标签") + ": " + tag.Name; }
            if (category == 6) return calendarSelected.ToString(Ui.CurrentLanguage == Localization.English ? "dddd, MMMM d, yyyy" : "yyyy年M月d日 dddd", new System.Globalization.CultureInfo(Ui.CurrentLanguage == Localization.English ? "en-US" : "zh-CN"));
            if (category == 10) { var term = (search == null ? "" : (search.Text ?? "").Trim()); return Ui.Text("搜索结果") + (term.Length == 0 ? "" : ": " + term); }
            return names[Math.Max(0, Math.Min(names.Length - 1, category))];
        }

        private FrameworkElement BuildHomeDashboard()
        {
            var panel = new StackPanel { Margin = new Thickness(30, 24, 30, 24) };
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = Ui.Text("首页"), FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text });
            var totals = new TextBlock { Text = Ui.Text("数量"), FontSize = 19, Foreground = UiTheme.Text, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(totals, 1); header.Children.Add(totals);
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
            for (int i = 0; i < counts.Length; i++)
            {
                var row = new Grid { Height = 42, Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                var icon = new TextBlock { Text = glyphs[i], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Foreground = UiTheme.Accent, VerticalAlignment = VerticalAlignment.Center, ToolTip = labels[i] };
                row.Children.Add(icon);
                var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8) };
                var bar = new Border { Background = UiTheme.Accent, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, Width = counts[i] == 0 ? 0 : Math.Min(620, Math.Max(4, counts[i] * (520.0 / max))) };
                barHost.Children.Add(bar); Grid.SetColumn(barHost, 1); row.Children.Add(barHost);
                var value = new TextBlock { Text = counts[i].ToString(), FontSize = 18, Foreground = UiTheme.Text, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(value, 2); row.Children.Add(value);
                var line = new Border { BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
                var wrapper = new Grid(); wrapper.Children.Add(row); wrapper.Children.Add(line); metrics.Children.Add(wrapper);
                metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(wrapper, i); Grid.SetColumnSpan(wrapper, 3);
            }
            panel.Children.Add(metrics);
            int totalCount = counts.Sum(), textCount = all.Count(n => n.Kind == NoteKind.Text), imageCount = all.Count(n => n.Kind == NoteKind.Image), encrypted = all.Count(n => n.Encrypted);
            long dbKb = 0; try { dbKb = new FileInfo(host.Library.Repository.FilePath).Length / 1024; } catch (IOException) { }
            var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            var db = new TextBlock { Text = Ui.Text("数据库大小：") + dbKb + "kb", FontSize = 18, Foreground = UiTheme.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; footer.Children.Add(db);
            var enc = new TextBlock { Text = Ui.Text("加密：") + encrypted, FontSize = 18, Foreground = UiTheme.Text, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(enc, 1); footer.Children.Add(enc);
            var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var textLine = new StackPanel { Orientation = Orientation.Horizontal }; textLine.Children.Add(new Border { Width = 52, Height = 18, Background = UiTheme.Accent, Margin = new Thickness(0, 0, 10, 0) }); textLine.Children.Add(new TextBlock { Text = Ui.Text("文本：") + textCount, FontSize = 18, Foreground = UiTheme.Text }); legend.Children.Add(textLine);
            var imageLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) }; imageLine.Children.Add(new Border { Width = 52, Height = 18, Background = UiTheme.Border, Margin = new Thickness(0, 0, 10, 0) }); imageLine.Children.Add(new TextBlock { Text = Ui.Text("图片：") + imageCount, FontSize = 18, Foreground = UiTheme.Text }); legend.Children.Add(imageLine);
            Grid.SetColumn(legend, 2); footer.Children.Add(legend);
            panel.Children.Add(footer);
            var totalBlock = new TextBlock { Text = Ui.Text("合计：") + totalCount, FontSize = 16, Foreground = UiTheme.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 8) };
            panel.Children.Insert(2, totalBlock);
            return panel;
        }

        private void UpdateSelectionSummary()
        {
            if (loading || selectionSummary == null || notes == null) return;
            var selected = Selected();
            viewButton.IsEnabled = selected.Length == 1;
            restoreButton.IsEnabled = selected.Any(n => n.State != NoteState.Desktop);
            closeButton.IsEnabled = selected.Any(n => n.State != NoteState.Closed);
            var restoreLabel = Ui.Text(selected.Length > 1 ? "批量恢复到桌面" : "恢复到桌面");
            var closeLabel = Ui.Text(selected.Length > 1 ? "批量关闭（可恢复）" : "关闭便签（可恢复）");
            restoreButton.ToolTip = restoreLabel; closeButton.ToolTip = closeLabel;
            System.Windows.Automation.AutomationProperties.SetName(restoreButton, restoreLabel);
            System.Windows.Automation.AutomationProperties.SetName(closeButton, closeLabel);
            selectionSummary.Text = selected.Length == 0 ? Ui.Text("未选中便签 · Ctrl / Shift 可多选") : string.Format(Ui.Text("已选中 {0} 张"), selected.Length) + " · " + string.Join(" / ", selected.GroupBy(n => n.State).Select(g => StateText(g.Key) + " " + g.Count()));
        }

        private static FrameworkElement EmptyState(string title, string description, string action = null, Action click = null)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 20, 4, 20) };
            panel.Children.Add(new TextBlock { Text = Ui.Text(title), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = Ui.Text(description), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
            if (click != null) { var button = Ui.Button(action, click); button.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(button); }
            return panel;
        }

        private static string StateText(NoteState state)
        {
            switch (state)
            {
                case NoteState.Desktop: return Ui.Text("桌面");
                case NoteState.Stored: return Ui.Text("已收藏");
                case NoteState.Closed: return Ui.Text("已关闭");
                case NoteState.Sleeping: return Ui.Text("休眠中");
                case NoteState.Recurring: return Ui.Text("重复提醒");
                default: return Ui.Text("未知状态");
            }
        }

        private void DisplaySelected(bool force = false)
        {
            if (loading || SelectedCategory == 0) return;
            var n = notes.SelectedItem as Note;
            if (!force && ReferenceEquals(editing, n) && n != null && (dirty || n.ModifiedUtc == displayedModifiedUtc)) return;
            SaveEditor(); editing = n; editor = null; saveFeedback = null; dirty = false; preview.Content = null; inlineTitleHost = null;
            if (n == null) { preview.Content = EmptyState("选择便签查看内容", "正文可在这里编辑；历史便签只有执行“恢复到桌面”才会重新显示在桌面。"); return; }
            displayedModifiedUtc = n.ModifiedUtc;
            if (n.Encrypted && host.Library.SessionPassword == null) { preview.Content = new StackPanel { Children = { new TextBlock { Text = Ui.Text("此便签已加密"), FontSize = 18, Margin = new Thickness(10) }, Ui.Button("解锁", () => { host.Unlock(); DisplaySelected(true); }) } }; return; }
            try
            {
                host.Library.Load(n); loading = true;
                var panel = new DockPanel();
                var titleHost = new Grid();
                var title = new TextBlock { Text = n.ManagerTitle, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), ToolTip = Ui.Text("双击或按 F2 修改标题"), Cursor = Cursors.IBeam };
                title.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { BeginInlineTitleEdit(n, titleHost); e.Handled = true; } };
                titleHost.Children.Add(title);
                inlineTitleHost = titleHost;
                DockPanel.SetDock(titleHost, Dock.Top); panel.Children.Add(titleHost);
                var folder = host.Library.Catalog.Folders.FirstOrDefault(f => f.Id == n.FolderId);
                var folderLine = folder == null ? "" : Ui.Text("文件夹") + ": " + host.FolderPath(folder) + "\n";
                var infoRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new TextBlock { Text = folderLine + StateText(n.State) + " · " + Ui.Text("修改时间") + " " + n.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "\n" + string.Join(" / ", n.Tags.Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)?.Name).Where(name => name != null)), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
                Grid.SetColumn(info, 0); infoRow.Children.Add(info);
                var previewActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
                previewActions.Children.Add(IconButton(IconRetry, "重试保存", SaveEditor));
                previewActions.Children.Add(IconButton(IconRename, "设置标题…", () => EditNoteTitle(n)));
                previewActions.Children.Add(IconButton(IconInfo, "属性…", () => ShowNoteProperties(n)));
                if (n.State != NoteState.Desktop) previewActions.Children.Add(IconButton(IconRestore, "恢复到桌面", () => { SaveEditor(); host.FlushNote(n); host.Library.Move(n, NoteState.Desktop); Refresh(); }, primary: true));
                if (n.State == NoteState.Sleeping || n.State == NoteState.Recurring) previewActions.Children.Add(IconButton(IconClock, "设置提醒…", () => new ScheduleWindow(host, n, true, n.State == NoteState.Recurring) { Owner = this }.ShowDialog()));
                if (n.Attachment != null) previewActions.Children.Add(IconButton(IconAttach, "设置附着…", () => new AttachWindow(host, n) { Owner = this }.ShowDialog()));
                Grid.SetColumn(previewActions, 1); infoRow.Children.Add(previewActions);
                DockPanel.SetDock(infoRow, Dock.Top); panel.Children.Add(infoRow);
                saveFeedback = new TextBlock { Text = Ui.Text(n.Kind == NoteKind.Text ? "正文可编辑，停止输入后自动保存。" : "图片仅预览；在桌面便签中编辑图片。"), Foreground = UiTheme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
                DockPanel.SetDock(saveFeedback, Dock.Top); panel.Children.Add(saveFeedback);
                if (n.Kind == NoteKind.Text) { editor = new TextEditor { LinksClickable = host.Library.Catalog.Settings.LinksClickable, CheckboxTrailingSpace = host.Library.Catalog.Settings.CheckboxTrailingSpace }; editor.Read(n); editor.Background = Ui.Brush(n.Color); editor.TextChanged += (s, e) => { if (!loading) { dirty = true; saveFeedback.Text = Ui.Text("有未保存的修改…"); saveFeedback.Foreground = UiTheme.Muted; saveTimer.Stop(); saveTimer.Start(); } }; panel.Children.Add(editor); }
                else { var image = new ImageEditor { Locked = true, MaxUndo = host.Library.Catalog.Settings.ImageUndoLevels, SaveFormat = host.Library.Catalog.Settings.ImageSaveType }; image.Read(n.Body); panel.Children.Add(new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); }
                preview.Content = panel; loading = false;
            }
            catch (Exception ex) { loading = false; preview.Content = EmptyState("无法加载便签内容", ex.Message, "重试", () => DisplaySelected(true)); }
        }

        public void FlushPending(Note note = null) { if (note == null || editing?.Id == note.Id) SaveEditor(); }
        private void EditNoteTitle(Note note)
        {
            if (note == null) return;
            if (!ReferenceEquals(note, editing) || inlineTitleHost == null) { SelectNoteInList(note.Id); DisplaySelected(true); }
            if (ReferenceEquals(note, editing) && inlineTitleHost != null) BeginInlineTitleEdit(note, inlineTitleHost);
        }
        private void BeginInlineTitleEdit(Note note, Panel titleHost)
        {
            SaveEditor();
            var box = new TextBox { Text = note.Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Padding = new Thickness(4, 2, 4, 2), BorderBrush = UiTheme.Accent, BorderThickness = new Thickness(1), Background = UiTheme.Surface, Margin = new Thickness(0, 0, 0, 6) };
            var finished = false;
            Action<bool> finish = commit =>
            {
                if (finished) return;
                try
                {
                    if (commit)
                    {
                        var text = box.Text ?? "";
                        if (text != note.Title)
                        {
                            var original = note.Title;
                            note.Title = text;
                            try { host.Library.Save(note); } catch { note.Title = original; throw; }
                        }
                    }
                    finished = true;
                    DisplaySelected(true);
                }
                catch (Exception ex) { Ui.Error(ex, this); box.Focus(); }
            };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { finish(true); e.Handled = true; } else if (e.Key == Key.Escape) { finish(false); e.Handled = true; } };
            box.LostFocus += (s, e) => finish(true);
            box.Loaded += (s, e) => box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
            titleHost.Children.Clear(); titleHost.Children.Add(box);
        }
        private void SaveEditor()
        {
            saveTimer.Stop();
            if (host.StorageFailed) return;
            if (!dirty || editing == null || editor == null || !host.Library.Notes.ContainsKey(editing.Id)) return;
            try
            {
                editor.Write(editing); host.Library.Save(editing); dirty = false; displayedModifiedUtc = editing.ModifiedUtc; host.ReloadWindow(editing);
                if (saveFeedback != null) { saveFeedback.Text = Ui.Text("已自动保存") + " · " + DateTime.Now.ToString("HH:mm:ss"); saveFeedback.Foreground = UiTheme.Muted; }
            }
            catch
            {
                if (saveFeedback != null) { saveFeedback.Text = Ui.Text("保存失败；修改仍保留，请重试保存。"); saveFeedback.Foreground = UiTheme.Danger; }
                throw;
            }
        }
        private Note[] Selected() { return notes.SelectedItems.Cast<Note>().ToArray(); }
        private void Each(Action<Note> action) { try { SaveEditor(); foreach (var n in Selected()) action(n); Refresh(); } catch (Exception ex) { Ui.Error(ex, this); } }
        private bool ConfirmClose(int number) { return !host.Library.Catalog.Settings.ConfirmClose || Ui.Confirm(string.Format(Ui.Text("关闭 {0} 张便签？关闭后可在“已关闭”中恢复。"), number), this); }
        private void CloseSelected() { var targets = Selected(); if (targets.Length == 0 || !ConfirmClose(targets.Length)) return; Each(n => host.Library.Move(n, NoteState.Closed)); }
        private ContextMenu Context()
        {
            var m = new ContextMenu();
            var selected = Selected(); bool any = selected.Length > 0, single = selected.Length == 1;
            Func<string, string> batch = text => selected.Length > 1 ? Ui.Text("批量") + Ui.Text(text) : Ui.Text(text);
            m.Items.Add(Ui.Item("新建便签", NewNoteForSelection));
            m.Items.Add(new Separator());
            m.Items.Add(Ui.Item("查看 / 编辑", OpenSelected, enabled: single));
            m.Items.Add(Ui.Item("设置标题…", () => EditNoteTitle(notes.SelectedItem as Note), "F2", enabled: single));
            m.Items.Add(Ui.Item("属性…", () => ShowNoteProperties(notes.SelectedItem as Note), enabled: single));
            m.Items.Add(new Separator());
            m.Items.Add(Ui.Item(batch("恢复到桌面"), RestoreSelected, enabled: selected.Any(n => n.State != NoteState.Desktop)));
            m.Items.Add(Ui.Item(batch("关闭便签（可恢复）"), CloseSelected, enabled: selected.Any(n => n.State != NoteState.Closed)));
            m.Items.Add(Ui.Item(batch("收藏"), () => Each(n => host.Library.Move(n, NoteState.Stored)), enabled: any));
            m.Items.Add(Ui.Item(batch("分离附着"), () => Each(n => { n.Attachment = null; host.Library.Save(n); }), enabled: selected.Any(n => n.Attachment != null)));
            m.Items.Add(new Separator());
            m.Items.Add(Ui.Item(batch("复制"), () => Each(n => { host.FlushNote(n); host.Library.Copy(n); }), enabled: any));
            m.Items.Add(Ui.Item(batch("标签…"), () => host.AssignTags(Selected()), enabled: any));
            bool ghost = any && selected.All(n => n.Ghost);
            m.Items.Add(Ui.Item(batch("Ghost（穿透鼠标）"), () => Each(n => { n.Ghost = !ghost; host.Library.Save(n); }), check: ghost, enabled: any));
            m.Items.Add(Ui.Item(batch("导出…"), () => Each(host.Export), enabled: any));
            m.Items.Add(new Separator());
            m.Items.Add(Ui.Item(batch("永久删除…"), () => { if (Ui.Confirm(string.Format(Ui.Text("永久删除选中的 {0} 张便签？此操作无法恢复。"), Selected().Length), this)) Each(host.Library.Delete); }, enabled: any));
            return m;
        }

        private void ShowNoteProperties(Note note)
        {
            if (note == null) return;
            var dialog = new Window { Title = Ui.Text("便签属性"), Owner = this, Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            UiTheme.Apply(dialog); Ui.FitDialog(dialog, 400, 0);
            var panel = new StackPanel { Margin = new Thickness(20) };
            var folder = host.Library.Catalog.Folders.FirstOrDefault(f => f.Id == note.FolderId);
            var stack = host.Library.Catalog.Stacks.FirstOrDefault(s => s.Id == note.StackId);
            string[,] values = {
                { "标题", note.DisplayTitle }, { "状态", StateText(note.State) }, { "类型", Ui.Text(note.Kind == NoteKind.Text ? "文本" : "图片") },
                { "创建时间", note.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") }, { "修改时间", note.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") },
                { "文件夹", folder == null ? Ui.Text("无") : host.FolderPath(folder) }, { "堆叠", stack == null ? Ui.Text("无") : stack.Name },
                { "标签", string.Join(" / ", note.Tags.Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)?.Name).Where(name => name != null)) },
                { "加密", Ui.Text(note.Encrypted ? "是" : "否") }, { "锁定", Ui.Text(note.Locked ? "是" : "否") }, { "标识符", note.Id }
            };
            for (int i = 0; i < values.GetLength(0); i++)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = Ui.Text(values[i, 0]), Foreground = UiTheme.Muted });
                var value = new TextBox { Text = values[i, 1] ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Foreground = UiTheme.Text };
                Grid.SetColumn(value, 1); row.Children.Add(value); panel.Children.Add(row);
            }
            var close = Ui.Button("关闭", () => dialog.Close()); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(close); dialog.Content = panel; dialog.ShowDialog();
        }

        private void SelectNoteInList(string id)
        {
            var item = notes.Items.Cast<Note>().FirstOrDefault(n => n.Id == id);
            if (item != null) { notes.SelectedItem = item; notes.ScrollIntoView(item); }
        }

        private void SaveManagerState()
        {
            SaveCategoryState(SelectedCategory);
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
            // Prefer per-category state; legacy settings only seed the first visit.
            if (!settings.ManagerFilters.ContainsKey(category))
            {
                folderFilter = settings.ManagerFolderFilter ?? "";
                stackFilter = settings.ManagerStackFilter ?? "";
                tagFilter = settings.ManagerTagFilter ?? "";
            }
            ApplyLayout(); BuildTree(); Refresh();
            if (category != 0 && !settings.ManagerSelectedNoteIds.ContainsKey(category) && !string.IsNullOrEmpty(settings.ManagerSelectedNoteId)) SelectNoteInList(settings.ManagerSelectedNoteId);
        }

        private static DataTemplate NoteCardTemplate()
        {
            var card = new FrameworkElementFactory(typeof(Border));
            card.SetValue(Border.BackgroundProperty, UiTheme.Surface); card.SetValue(Border.BorderBrushProperty, UiTheme.Border); card.SetValue(Border.BorderThicknessProperty, new Thickness(1)); card.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); card.SetValue(Border.PaddingProperty, new Thickness(8)); card.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
            card.SetBinding(FrameworkElement.ToolTipProperty, new Binding("DisplayTitle"));
            var panel = new FrameworkElementFactory(typeof(DockPanel));
            var thumb = new FrameworkElementFactory(typeof(Border));
            thumb.SetValue(FrameworkElement.WidthProperty, 64d); thumb.SetValue(FrameworkElement.HeightProperty, 48d); thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); thumb.SetValue(Border.BorderBrushProperty, UiTheme.Border); thumb.SetValue(Border.BorderThicknessProperty, new Thickness(1)); thumb.SetBinding(Border.BackgroundProperty, new Binding("ColorHex") { Converter = new ColorBrushConverter() }); thumb.SetValue(DockPanel.DockProperty, Dock.Left);
            var previewText = new FrameworkElementFactory(typeof(TextBlock)); previewText.SetBinding(TextBlock.TextProperty, new Binding(".") { Converter = new NoteKindConverter() }); previewText.SetValue(TextBlock.FontSizeProperty, 12d); previewText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); previewText.SetBinding(TextBlock.ForegroundProperty, new Binding("ColorHex") { Converter = new ContrastBrushConverter() }); previewText.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center); previewText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            thumb.AppendChild(previewText); panel.AppendChild(thumb);
            var right = new FrameworkElementFactory(typeof(StackPanel)); right.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
            var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding("ManagerTitle")); title.SetValue(TextBlock.FontSizeProperty, 14d); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); right.AppendChild(title);
            var summary = new FrameworkElementFactory(typeof(TextBlock)); summary.SetBinding(TextBlock.TextProperty, new Binding("DisplayPreview") { Converter = new CardSummaryConverter() }); summary.SetValue(TextBlock.FontSizeProperty, 12d); summary.SetValue(TextBlock.ForegroundProperty, UiTheme.Muted); summary.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); summary.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 3)); right.AppendChild(summary);
            var tagsHost = new FrameworkElementFactory(typeof(ItemsControl)); tagsHost.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("DisplayTags")); tagsHost.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 2));
            var tagPanel = new FrameworkElementFactory(typeof(WrapPanel));
            tagsHost.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(tagPanel));
            var tagBorder = new FrameworkElementFactory(typeof(Border));
            tagBorder.SetBinding(Border.BackgroundProperty, new Binding("ColorHex") { Converter = new ColorBrushConverter() }); tagBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); tagBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)); tagBorder.SetValue(Border.PaddingProperty, new Thickness(5, 1, 5, 1));
            var tagName = new FrameworkElementFactory(typeof(TextBlock)); tagName.SetBinding(TextBlock.TextProperty, new Binding("DisplayName")); tagName.SetBinding(TextBlock.ForegroundProperty, new Binding("ColorHex") { Converter = new ContrastBrushConverter() }); tagName.SetValue(TextBlock.FontSizeProperty, 10d);
            tagBorder.AppendChild(tagName); tagsHost.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate(typeof(Tag)) { VisualTree = tagBorder });
            right.AppendChild(tagsHost);
            var date = new FrameworkElementFactory(typeof(TextBlock)); date.SetBinding(TextBlock.TextProperty, new Binding("DateLabel")); date.SetValue(TextBlock.FontSizeProperty, 11d); date.SetValue(TextBlock.ForegroundProperty, UiTheme.Muted); right.AppendChild(date);
            panel.AppendChild(right);
            card.AppendChild(panel);
            return new DataTemplate(typeof(Note)) { VisualTree = card };
        }

        private sealed class ExpandGlyphConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { return value is bool && (bool)value ? "" : ""; }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotSupportedException(); }
        }

        private sealed class NoteKindConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { return Ui.Text((value as Note)?.Kind == NoteKind.Image ? "图片" : "文本"); }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotSupportedException(); }
        }

        private sealed class CardSummaryConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var text = string.Join(" ", (value as string ?? "").Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                return text.Length > 160 ? text.Substring(0, 160) + "…" : text;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotSupportedException(); }
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
