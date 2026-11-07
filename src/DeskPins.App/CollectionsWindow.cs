using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class CollectionsWindow : Window
    {
        public CollectionsWindow(AppHost host, string type)
        {
            Title = type == "friends" ? "联系人" : type == "folders" ? "收藏文件夹" : "标签"; Width = 630; Height = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            var root = new DockPanel { Margin = new Thickness(12) }; var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); var list = new ListBox { DisplayMemberPath = "Name" }; root.Children.Add(list);
            Action refresh = () => { list.ItemsSource = null; list.ItemsSource = type == "friends" ? (System.Collections.IEnumerable)host.Library.Catalog.Friends : type == "folders" ? host.Library.Catalog.Folders : (System.Collections.IEnumerable)host.Library.Catalog.Tags; };
            Action<object> edit = item =>
            {
                if (type == "friends")
                {
                    var f = item as Friend ?? new Friend(); var name = Ui.Ask("联系人", "名称", f.Name); if (name == null) return; var address = Ui.Ask("联系人", "主机地址（可含 :端口；留空使用邮件）", f.Address); if (address == null) return; var email = Ui.Ask("联系人", "邮件地址", f.Email); if (email == null) return; var group = Ui.Ask("联系人", "组路径（例如 团队/开发）", f.Group); if (group == null) return; var favorite = Ui.Ask("联系人", "快捷发送序号 1–10（0=无）", f.Favorite.ToString()); if (favorite == null) return; f.Name = name; f.Address = address; f.Email = email; f.Group = group; f.Favorite = Math.Max(0, Math.Min(10, (int)Ui.Number(favorite, 0))); if (item == null) host.Library.Catalog.Friends.Add(f);
                }
                else if (type == "folders")
                {
                    var f = item as Folder ?? new Folder(); var dialog = new Window { Title = "收藏文件夹", Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize }; var panel = new StackPanel { Margin = new Thickness(16) }; var name = new TextBox { Text = f.Name }; Ui.Field(panel, "名称", name); var roots = new[] { new Folder { Id = "", Name = "根目录" } }.Concat(host.Library.Catalog.Folders.Where(x => x.Id != f.Id)).ToList(); var parent = new ComboBox { ItemsSource = roots, DisplayMemberPath = "Name", SelectedItem = roots.FirstOrDefault(x => x.Id == f.ParentId) ?? roots[0] }; Ui.Field(panel, "父文件夹", parent); panel.Children.Add(Ui.Button("保存", () => { var selected = (Folder)parent.SelectedItem; var current = selected.Id; for (int i = 0; i <= host.Library.Catalog.Folders.Count && current != ""; i++) { if (current == f.Id) { MessageBox.Show("文件夹不能循环嵌套。"); return; } current = host.Library.Catalog.Folders.First(x => x.Id == current).ParentId; } f.Name = name.Text; f.ParentId = selected.Id; dialog.DialogResult = true; })); dialog.Content = panel; if (dialog.ShowDialog() != true) return; if (item == null) host.Library.Catalog.Folders.Add(f);
                }
                else { var t = item as Tag ?? new Tag(); var name = Ui.Ask("标签", "名称", t.Name); if (name == null) return; var color = Ui.Color(t.Color); if (color == null) return; t.Name = name; t.Color = color; if (item == null) host.Library.Catalog.Tags.Add(t); }
                host.Library.SaveCatalog(); refresh();
            };
            buttons.Children.Add(Ui.Button("新建", () => { try { edit(null); } catch (Exception ex) { Ui.Error(ex); } })); buttons.Children.Add(Ui.Button("编辑", () => { if (list.SelectedItem != null) edit(list.SelectedItem); })); buttons.Children.Add(Ui.Button("删除", () => { if (list.SelectedItem == null || !Ui.Confirm("删除选中项？便签内容会保留。")) return; if (type == "friends") host.Library.Catalog.Friends.Remove((Friend)list.SelectedItem); else if (type == "folders") { var f = (Folder)list.SelectedItem; foreach (var n in host.Library.Notes.Values.Where(n => n.FolderId == f.Id).ToArray()) host.Library.Move(n, NoteState.Stored); foreach (var child in host.Library.Catalog.Folders.Where(x => x.ParentId == f.Id)) child.ParentId = f.ParentId; host.Library.Catalog.Folders.Remove(f); } else { var t = (Tag)list.SelectedItem; foreach (var n in host.Library.Notes.Values.Where(n => n.Tags.Contains(t.Id))) { n.Tags.Remove(t.Id); host.Library.Save(n); } host.Library.Catalog.Tags.Remove(t); } host.Library.SaveCatalog(); refresh(); }));
            if (type == "folders") buttons.Children.Add(Ui.Button("切换加密", () => { var f = list.SelectedItem as Folder; if (f == null) return; host.EnsureUnlocked(new Folder { Encrypted = true }); host.Library.SetFolderEncrypted(f, !f.Encrypted); refresh(); }));
            if (type == "friends") { buttons.Children.Add(Ui.Button("新建分隔线", () => { host.Library.Catalog.Friends.Add(new Friend { Name = "────────", Separator = true }); host.Library.SaveCatalog(); refresh(); })); buttons.Children.Add(Ui.Button("同步联系人", () => host.SyncFriends())); buttons.Children.Add(Ui.Button("上移", () => { var f = list.SelectedItem as Friend; if (f == null) return; int i = host.Library.Catalog.Friends.IndexOf(f); if (i > 0) { host.Library.Catalog.Friends.RemoveAt(i); host.Library.Catalog.Friends.Insert(i - 1, f); host.Library.SaveCatalog(); refresh(); list.SelectedItem = f; } })); }
            buttons.Children.Add(Ui.Button("关闭", () => Close())); list.MouseDoubleClick += (s, e) => { if (list.SelectedItem != null) edit(list.SelectedItem); }; refresh(); Content = root;
        }
    }
    public sealed class StackWindow : Window
    {
        public readonly NoteStack Stack;
        private readonly AppHost host;
        private readonly TextBlock caption;
        public StackWindow(AppHost host, NoteStack stack)
        {
            this.host = host; Stack = stack; Title = "DeskPins 堆叠 · " + stack.Name; Width = 180; Height = 44; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Left = stack.Left; Top = stack.Top; Topmost = stack.Topmost;
            caption = new TextBlock { Text = stack.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) }; Content = new Border { Background = Ui.Brush(stack.Color), BorderBrush = System.Windows.Media.Brushes.DimGray, BorderThickness = new Thickness(1), Child = caption }; MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Toggle(); return; } double x = Left, y = Top; try { DragMove(); } catch (InvalidOperationException) { } if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) foreach (var n in host.Library.Notes.Values.Where(n => n.StackId == Stack.Id)) { n.Left += Left - x; n.Top += Top - y; host.ReloadPosition(n); host.Library.Save(n); } Stack.Left = Left; Stack.Top = Top; host.Library.SaveCatalog(); }; SourceInitialized += (s, e) => Native.ToolWindow(this);
            var menu = new ContextMenu(); menu.Items.Add(Ui.Item("展开 / 收拢", Toggle, "Space")); menu.Items.Add(Ui.Item("重命名…", () => { var name = Ui.Ask("堆叠名称", "名称", Stack.Name); if (name != null) { Stack.Name = name; host.Library.SaveCatalog(); Refresh(); } })); menu.Items.Add(Ui.Item("颜色…", () => { var color = Ui.Color(Stack.Color); if (color != null) { Stack.Color = color; host.Library.SaveCatalog(); Refresh(); } })); menu.Items.Add(Ui.Item("置顶", () => { Stack.Topmost = !Stack.Topmost; Topmost = Stack.Topmost; host.Library.SaveCatalog(); }, "", Stack.Topmost)); menu.Items.Add(Ui.Item("关闭堆叠（保留便签）", () => { foreach (var n in host.Library.Notes.Values.Where(n => n.StackId == Stack.Id)) { n.StackId = ""; host.Library.Save(n); } host.Library.Catalog.Stacks.Remove(Stack); host.Library.SaveCatalog(); })); menu.Items.Add(Ui.Item("关闭堆叠内便签", () => { foreach (var n in host.Library.Notes.Values.Where(n => n.StackId == Stack.Id).ToArray()) host.Library.Move(n, NoteState.Closed); })); ContextMenu = menu; PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Space) Toggle(); };
        }
        private void Toggle() { Stack.Expanded = !Stack.Expanded; host.Library.SaveCatalog(); }
        public void Refresh() { caption.Text = (Stack.Expanded ? "▾ " : "▸ ") + Stack.Name + " (" + host.Library.Notes.Values.Count(n => n.StackId == Stack.Id) + ")"; ((Border)Content).Background = Ui.Brush(Stack.Color); }
    }
}
