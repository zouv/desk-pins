using System;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class CollectionsWindow : Window
    {
        public CollectionsWindow(AppHost host, string type)
        {
            Title = Ui.Text(type == "friends" ? "联系人" : type == "folders" ? "收藏文件夹" : "标签");
            Width = 680; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            UiTheme.Apply(this); Ui.FitDialog(this, 460, 320);
            var panel = new DockPanel();
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
            header.Children.Add(new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = Ui.Text("选择项目后编辑；删除项目不会删除便签内容。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(tools, Dock.Top); panel.Children.Add(tools);
            var dangerTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(dangerTools, Dock.Bottom); panel.Children.Add(dangerTools);
            var list = new ListBox { ItemTemplate = CollectionEditor.NameTemplate(), MinHeight = 180, Height = 280 };
            System.Windows.Automation.AutomationProperties.SetName(list, Title);
            panel.Children.Add(list);
            Action<object> refresh = selected =>
            {
                list.ItemsSource = null;
                list.ItemsSource = type == "friends" ? (System.Collections.IEnumerable)host.Library.Catalog.Friends : type == "folders" ? host.Library.Catalog.Folders : (System.Collections.IEnumerable)host.Library.Catalog.Tags;
                list.SelectedItem = selected; if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
            };
            Action<object> edit = item =>
            {
                object edited;
                try
                {
                    if (type == "friends")
                    {
                        var draft = EditFriend(item as Friend); if (draft == null) return;
                        edited = item ?? draft;
                        host.Library.Atomic(() =>
                        {
                            if (item == null) host.Library.Catalog.Friends.Add(draft);
                            else { var friend = (Friend)item; friend.Name = draft.Name; friend.Address = draft.Address; friend.Email = draft.Email; friend.Group = draft.Group; friend.Favorite = draft.Favorite; }
                            host.Library.SaveCatalog();
                        });
                    }
                    else
                    {
                        edited = item == null ? (type == "folders" ? (object)new Folder() : new Tag()) : item is Folder folder ? (object)Json.Read<Folder>(Json.Bytes(folder)) : Json.Read<Tag>(Json.Bytes((Tag)item));
                        if (!CollectionEditor.Edit(this, host, edited, type)) return;
                        var draft = edited;
                        host.Library.Atomic(() =>
                        {
                            if (type == "folders")
                            {
                                var f = (Folder)draft;
                                if (item == null) host.Library.Catalog.Folders.Add(f);
                                else { var original = (Folder)item; original.Name = f.Name; original.ParentId = f.ParentId; }
                            }
                            else
                            {
                                var t = (Tag)draft;
                                if (item == null) host.Library.Catalog.Tags.Add(t);
                                else { var original = (Tag)item; original.Name = t.Name; original.Color = t.Color; }
                            }
                            host.Library.SaveCatalog();
                        });
                        if (item != null) edited = item;
                    }
                    refresh(edited); list.Focus();
                }
                catch (Exception ex) { refresh(null); Ui.Error(ex, this); }
            };
            var create = Ui.PrimaryButton("新建…", () => edit(null)); tools.Children.Add(create);
            var editButton = Ui.Button("编辑…", () => { if (list.SelectedItem != null) edit(list.SelectedItem); }); tools.Children.Add(editButton);
            var delete = Ui.DangerButton("删除", () =>
            {
                var selected = list.SelectedItem; if (selected == null || !Ui.Confirm("删除选中项？便签内容会保留。", this)) return;
                int index = list.SelectedIndex;
                try
                {
                    host.Library.Atomic(() =>
                    {
                        if (type == "friends") host.Library.Catalog.Friends.Remove((Friend)selected);
                        else if (type == "folders")
                        {
                            var f = (Folder)selected;
                            foreach (var n in host.Library.Notes.Values.Where(n => n.FolderId == f.Id).ToArray()) host.Library.Move(n, NoteState.Stored);
                            foreach (var child in host.Library.Catalog.Folders.Where(x => x.ParentId == f.Id)) child.ParentId = f.ParentId;
                            host.Library.Catalog.Folders.Remove(f);
                        }
                        else
                        {
                            var t = (Tag)selected;
                            foreach (var n in host.Library.Notes.Values.Where(n => n.Tags.Contains(t.Id))) { n.Tags.Remove(t.Id); host.Library.Save(n); }
                            host.Library.Catalog.Tags.Remove(t);
                        }
                        host.Library.SaveCatalog();
                    });
                    refresh(null); if (list.Items.Count > 0) list.SelectedIndex = Math.Min(index, list.Items.Count - 1); list.Focus();
                }
                catch (Exception ex) { refresh(null); Ui.Error(ex, this); }
            }); dangerTools.Children.Add(delete);
            Button encryption = null, up = null;
            if (type == "folders")
            {
                encryption = Ui.Button("切换加密", () =>
                {
                    var f = list.SelectedItem as Folder; if (f == null) return;
                    try { host.EnsureUnlocked(new Folder { Encrypted = true }); host.Library.SetFolderEncrypted(f, !f.Encrypted); refresh(f); list.Focus(); }
                    catch (Exception ex) { refresh(null); Ui.Error(ex, this); }
                }); tools.Children.Add(encryption);
            }
            if (type == "friends")
            {
                tools.Children.Add(Ui.Button("新建分隔线", () => { var f = new Friend { Name = "────────", Separator = true }; host.Library.Atomic(() => { host.Library.Catalog.Friends.Add(f); host.Library.SaveCatalog(); }); refresh(f); list.Focus(); }));
                tools.Children.Add(Ui.Button("同步联系人", () => { host.SyncFriends(); refresh(list.SelectedItem); }));
                up = Ui.Button("上移", () =>
                {
                    var f = list.SelectedItem as Friend; if (f == null) return; int i = host.Library.Catalog.Friends.IndexOf(f); if (i <= 0) return;
                    host.Library.Atomic(() => { host.Library.Catalog.Friends.RemoveAt(i); host.Library.Catalog.Friends.Insert(i - 1, f); host.Library.SaveCatalog(); }); refresh(f); list.Focus();
                }); tools.Children.Add(up);
            }
            Action update = () =>
            {
                editButton.IsEnabled = delete.IsEnabled = list.SelectedItem != null;
                if (encryption != null) encryption.IsEnabled = list.SelectedItem != null;
                if (up != null) up.IsEnabled = list.SelectedIndex > 0;
            };
            list.SelectionChanged += (s, e) => update();
            list.MouseDoubleClick += (s, e) => { if (list.SelectedItem != null) edit(list.SelectedItem); };
            list.KeyDown += (s, e) => { if ((e.Key == Key.Enter || e.Key == Key.F2) && list.SelectedItem != null) { e.Handled = true; edit(list.SelectedItem); } };
            var close = Ui.Button("关闭", Close); close.IsCancel = true;
            Ui.DialogContent(this, panel, close); refresh(null); update();
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => list.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        private Friend EditFriend(Friend original)
        {
            var draft = original == null ? new Friend() : Json.Read<Friend>(Json.Bytes(original));
            var dialog = new Window { Owner = this, Title = Ui.Text(original == null ? "新建联系人" : "编辑联系人"), Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            UiTheme.Apply(dialog); Ui.FitDialog(dialog, 400, 0);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = dialog.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 6) });
            panel.Children.Add(new TextBlock { Text = Ui.Text(draft.Separator ? "此项目是联系人菜单分隔线。" : "填写主机地址或邮件地址；同时填写时优先使用主机地址。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 6) });
            var name = new TextBox { Text = draft.Name }; var address = new TextBox { Text = draft.Address }; var email = new TextBox { Text = draft.Email }; var group = new TextBox { Text = draft.Group }; var favorite = new TextBox { Text = draft.Favorite.ToString(CultureInfo.CurrentCulture) };
            Ui.Field(panel, "名称", name);
            if (!draft.Separator)
            {
                Ui.Field(panel, "主机地址（可含 :端口；留空使用邮件）", address);
                Ui.Field(panel, "邮件地址", email); Ui.Field(panel, "组路径（例如 团队/开发）", group); Ui.Field(panel, "快捷发送序号 1–10（0=无）", favorite);
            }
            var save = Ui.PrimaryButton("保存", () =>
            {
                var n = (name.Text ?? "").Trim(); var a = (address.Text ?? "").Trim(); var mail = (email.Text ?? "").Trim(); int order = draft.Favorite;
                if (n.Length == 0) { CollectionEditor.ValidationError(dialog, name, "名称不能为空。"); return; }
                if (!draft.Separator)
                {
                    if (a.Length == 0 && mail.Length == 0) { CollectionEditor.ValidationError(dialog, address, "请填写主机地址或邮件地址。"); return; }
                    if (a.Length > 0 && !ValidAddress(a)) { CollectionEditor.ValidationError(dialog, address, "请输入有效主机地址；端口需为 1–65535 的整数。"); return; }
                    if (mail.Length > 0)
                    {
                        try { var parsed = new MailAddress(mail); if (!string.Equals(parsed.Address, mail, StringComparison.OrdinalIgnoreCase) || !mail.Contains("@")) throw new FormatException(); }
                        catch (FormatException) { CollectionEditor.ValidationError(dialog, email, "请输入有效邮件地址。"); return; }
                    }
                    if (!int.TryParse(favorite.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out order) || order < 0 || order > 10) { CollectionEditor.ValidationError(dialog, favorite, "快捷发送序号需为 0–10 的整数。"); return; }
                }
                draft.Name = n; draft.Address = a; draft.Email = mail; draft.Group = (group.Text ?? "").Trim(); draft.Favorite = order; dialog.DialogResult = true;
            }); save.IsDefault = true;
            var cancel = Ui.Button("取消", dialog.Close); cancel.IsCancel = true;
            Ui.DialogContent(dialog, panel, save, cancel);
            dialog.Loaded += (s, e) => dialog.Dispatcher.BeginInvoke(new Action(() => { name.Focus(); name.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
            return dialog.ShowDialog() == true ? draft : null;
        }

        private static bool ValidAddress(string address)
        {
            if (address.Any(char.IsWhiteSpace) || address.IndexOfAny(new[] { '/', '\\', '@', ';', '[', ']' }) >= 0) return false;
            IPAddress ip; if (IPAddress.TryParse(address, out ip)) return true;
            string host = address; int colon = address.LastIndexOf(':');
            if (colon >= 0)
            {
                if (colon == 0 || colon != address.IndexOf(':')) return false;
                int port; if (!int.TryParse(address.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535) return false;
                host = address.Substring(0, colon);
            }
            return Uri.CheckHostName(host) != UriHostNameType.Unknown;
        }
    }
    public sealed class StackDockWindow : Window
    {
        private const double HeaderHeight = 22, RowHeight = 30;
        private readonly AppHost host;
        private readonly StackPanel rows;
        private readonly Grid header;
        private readonly Border pinIndicator;
        private readonly TextBlock headerCaption;
        private readonly Border root;
        private readonly MenuItem topmostItem, minimalItem;
        public StackDockWindow(AppHost host)
        {
            this.host = host; var catalog = host.Library.Catalog;
            Title = Ui.Text("DeskPins 堆叠"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent;
            MinWidth = 40; MinHeight = 40; Width = Math.Max(40, catalog.StackDockWidth);
            var position = Native.Clamp(new Rect(catalog.StackDockLeft, catalog.StackDockTop, Width, 60)); Left = position.Left; Top = position.Top; Topmost = catalog.StackDockTopmost;
            rows = new StackPanel();
            pinIndicator = new Border { Width = 6 };
            headerCaption = new TextBlock { FontSize = 11, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            header = new Grid { Height = HeaderHeight, Cursor = Cursors.SizeAll };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.Children.Add(pinIndicator); Grid.SetColumn(headerCaption, 1); header.Children.Add(headerCaption);
            header.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch (InvalidOperationException) { } var c = host.Library.Catalog; c.StackDockLeft = Left; c.StackDockTop = Top; host.Library.SaveCatalog(); };
            var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(header); Grid.SetRow(rows, 1); layout.Children.Add(rows);
            root = new Border { Background = System.Windows.Media.Brushes.White, BorderBrush = System.Windows.Media.Brushes.DimGray, BorderThickness = new Thickness(1), Child = layout };
            Content = root;
            Activated += (s, e) => UpdateHeaderChrome(); Deactivated += (s, e) => UpdateHeaderChrome();
            var menu = new ContextMenu();
            menu.Items.Add(Ui.Item("全部展开", () => { foreach (var stack in host.Library.Catalog.Stacks) stack.Expanded = true; host.Library.SaveCatalog(); }));
            menu.Items.Add(Ui.Item("全部收拢", () => { foreach (var stack in host.Library.Catalog.Stacks) stack.Expanded = false; host.Library.SaveCatalog(); }));
            minimalItem = Ui.Item("极简模式（隐藏名称）", () => { var c = host.Library.Catalog; c.StackDockMinimal = !c.StackDockMinimal; host.Library.SaveCatalog(); });
            menu.Items.Add(minimalItem);
            menu.Items.Add(new Separator());
            topmostItem = Ui.Item("置顶堆叠面板", () => { var c = host.Library.Catalog; c.StackDockTopmost = !c.StackDockTopmost; Topmost = c.StackDockTopmost; host.Library.SaveCatalog(); });
            menu.Items.Add(topmostItem); ContextMenu = menu;
            SourceInitialized += (s, e) => Native.ToolWindow(this);
            WindowResize.Attach(this, 40, 40, () => false, null, () => { host.Library.Catalog.StackDockWidth = Width; host.Library.SaveCatalog(); FitHeight(); });
        }
        public void Refresh()
        {
            var catalog = host.Library.Catalog;
            rows.Children.Clear();
            foreach (var stack in catalog.Stacks) rows.Children.Add(BuildRow(stack));
            headerCaption.Text = Ui.Text("堆叠") + " · " + catalog.Stacks.Count;
            headerCaption.Visibility = catalog.StackDockMinimal ? Visibility.Collapsed : Visibility.Visible;
            Topmost = catalog.StackDockTopmost;
            topmostItem.IsChecked = catalog.StackDockTopmost;
            minimalItem.IsChecked = catalog.StackDockMinimal;
            root.Effect = Ui.NoteShadow(catalog.Settings.ShowShadow);
            UpdateHeaderChrome();
            FitHeight();
        }
        public void BringForward() { if (!IsVisible) Show(); Topmost = host.Library.Catalog.StackDockTopmost; Native.RaiseNoActivate(this); }
        private void UpdateHeaderChrome()
        {
            var brush = Ui.Brush(IsActive ? UiTheme.TitleActiveColor : UiTheme.TitleInactiveColor);
            header.Background = brush;
            pinIndicator.Background = host.Library.Catalog.StackDockTopmost ? Ui.Brush(UiTheme.PinColor) : brush;
        }
        private void FitHeight() { Height = HeaderHeight + host.Library.Catalog.Stacks.Count * RowHeight + 6; }
        private void Toggle(NoteStack stack) { stack.Expanded = !stack.Expanded; host.Library.SaveCatalog(); }
        // 展开行用组色掺白的浅底表达状态；颜色非法时退回白色，避免面板崩溃。
        private static string TintHex(string color, double amount)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
                Func<byte, byte> mix = v => (byte)Math.Round(v * amount + 255 * (1 - amount));
                return "#" + mix(c.R).ToString("X2") + mix(c.G).ToString("X2") + mix(c.B).ToString("X2");
            }
            catch (FormatException) { return "#FFFFFF"; }
        }
        private Border BuildRow(NoteStack stack)
        {
            var minimal = host.Library.Catalog.StackDockMinimal;
            var count = host.Library.Notes.Values.Count(n => n.StackId == stack.Id);
            var swatch = new Border { Width = 10, Height = 10, Background = Ui.Brush(stack.Color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
            var title = new TextBlock { Text = stack.Name, FontSize = 12, Foreground = Ui.Brush("#333333"), FontWeight = stack.Expanded ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = minimal ? Visibility.Collapsed : Visibility.Visible };
            var badge = new TextBlock { Text = count.ToString(), FontSize = 10, Foreground = Ui.Brush("#8A8F98"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0), Visibility = minimal ? Visibility.Collapsed : Visibility.Visible };
            var chevron = new TextBlock { Text = stack.Expanded ? "▼" : "▶", FontSize = stack.Expanded ? 12 : 11, Foreground = Ui.Brush(stack.Expanded ? "#333333" : "#8A8F98"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(swatch); Grid.SetColumn(title, 1); grid.Children.Add(title); Grid.SetColumn(badge, 2); grid.Children.Add(badge); Grid.SetColumn(chevron, 3); grid.Children.Add(chevron);
            var rest = stack.Expanded ? Ui.Brush(TintHex(stack.Color, .22)) : System.Windows.Media.Brushes.Transparent;
            var hover = stack.Expanded ? Ui.Brush(TintHex(stack.Color, .34)) : Ui.Brush("#EFEFEF");
            var row = new Border { Height = RowHeight - 2, Margin = new Thickness(4, 1, 4, 1), Background = rest, Cursor = Cursors.Hand, Child = grid, ToolTip = stack.Name + " · " + count + " " + Ui.Text("张便签") };
            row.MouseEnter += (s, e) => row.Background = hover;
            row.MouseLeave += (s, e) => row.Background = rest;
            row.MouseLeftButtonUp += (s, e) => Toggle(stack);
            var menu = new ContextMenu();
            menu.Items.Add(Ui.Item("展开 / 收拢", () => Toggle(stack)));
            menu.Items.Add(Ui.Item("重命名…", () => { var name = Ui.Ask("堆叠名称", "名称", stack.Name); if (name != null) { stack.Name = name; host.Library.SaveCatalog(); } }));
            menu.Items.Add(Ui.Item("颜色…", () => { var color = Ui.Color(stack.Color); if (color != null) { stack.Color = color; host.Library.SaveCatalog(); } }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Item("关闭堆叠（保留便签）", () => { foreach (var n in host.Library.Notes.Values.Where(n => n.StackId == stack.Id)) { n.StackId = ""; host.Library.Save(n); } host.Library.Catalog.Stacks.Remove(stack); host.Library.SaveCatalog(); }));
            menu.Items.Add(Ui.Item("关闭堆叠内便签", () => { foreach (var n in host.Library.Notes.Values.Where(n => n.StackId == stack.Id).ToArray()) host.Library.Move(n, NoteState.Closed); }));
            row.ContextMenu = menu;
            return row;
        }
    }
}
