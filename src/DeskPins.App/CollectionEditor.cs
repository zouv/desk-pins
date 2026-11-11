using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskPins.Core;

namespace DeskPins.App
{
    internal static class CollectionEditor
    {
        public static bool Edit(Window owner, AppHost host, object item, string kind)
        {
            var folder = item as Folder;
            var stack = item as NoteStack;
            var tag = item as Tag;
            var window = new Window
            {
                Title = Ui.Text(kind == "folders" ? "文件夹属性" : kind == "stacks" ? "堆叠属性" : "标签属性"),
                Width = 430, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Owner = owner
            };
            Ui.FitDialog(window, 380, 0);
            var panel = new StackPanel { Margin = new Thickness(16) };
            var name = new TextBox { Text = folder != null ? folder.Name : stack != null ? stack.Name : tag.Name };
            Ui.Field(panel, "名称", name);

            ComboBox parent = null;
            if (folder != null)
            {
                var roots = new[] { new Folder { Id = "", Name = Ui.Text("根目录") } }
                    .Concat(host.Library.Catalog.Folders.Where(f => f.Id != folder.Id && !IsUnder(host, f.Id, folder.Id))).ToList();
                parent = new ComboBox { ItemsSource = roots, DisplayMemberPath = "Name", SelectedItem = roots.FirstOrDefault(f => f.Id == folder.ParentId) ?? roots[0] };
                Ui.Field(panel, "父文件夹", parent);
            }

            string color = stack != null ? stack.Color : tag != null ? tag.Color : null;
            Border swatch = null;
            if (color != null)
            {
                var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
                swatch = new Border { Width = 64, Height = 26, Background = Ui.Brush(color), BorderBrush = Ui.Brush("#9CA3AF"), BorderThickness = new Thickness(1), Margin = new Thickness(2, 0, 8, 0) };
                var choose = Ui.Button("颜色…", () => { var value = Ui.Color(color); if (value != null) { color = value; swatch.Background = Ui.Brush(value); } });
                colorRow.Children.Add(swatch); colorRow.Children.Add(choose);
                Ui.Field(panel, "颜色", colorRow);
            }

            CheckBox topmost = null;
            if (stack != null)
            {
                topmost = new CheckBox { Content = Ui.Text("置顶"), IsChecked = stack.Topmost, Margin = new Thickness(2, 8, 2, 4) };
                panel.Children.Add(topmost);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var save = Ui.Button("保存", () =>
            {
                var value = (name.Text ?? "").Trim();
                if (value.Length == 0) { MessageBox.Show(Ui.Text("名称不能为空。")); return; }
                if (folder != null) { folder.Name = value; folder.ParentId = ((Folder)parent.SelectedItem).Id; }
                else if (stack != null) { stack.Name = value; stack.Color = color; stack.Topmost = topmost.IsChecked == true; }
                else if (tag != null) { tag.Name = value; tag.Color = color; }
                window.DialogResult = true;
            });
            save.IsDefault = true;
            buttons.Children.Add(save);
            var cancel = Ui.Button("取消", () => window.Close()); cancel.IsCancel = true; buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            window.Content = panel;
            return window.ShowDialog() == true;
        }

        private static bool IsUnder(AppHost host, string folderId, string ancestorId)
        {
            var folders = host.Library.Catalog.Folders; var current = folders.FirstOrDefault(f => f.Id == folderId); var visited = new HashSet<string>();
            while (current != null && visited.Add(current.Id))
            {
                if (current.Id == ancestorId) return true;
                current = folders.FirstOrDefault(f => f.Id == current.ParentId);
            }
            return false;
        }
    }

    internal sealed class SearchTagWindow : Window
    {
        public List<string> SelectedTags = new List<string>();

        public SearchTagWindow(AppHost host, IEnumerable<string> initial)
        {
            Title = Ui.Text("设置搜索标签过滤"); Width = 440; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false;
            Ui.FitDialog(this, 360, 260);
            var root = new DockPanel { Margin = new Thickness(12) };
            var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var list = new StackPanel { Margin = new Thickness(4) };
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var selected = new HashSet<string>(initial ?? new string[0]);
            var checks = new Dictionary<string, CheckBox>();
            if (host.Library.Catalog.Tags.Count == 0) list.Children.Add(new TextBlock { Text = Ui.Text("没有标签。"), Foreground = Brushes.Gray, Margin = new Thickness(4, 8, 4, 8) });
            foreach (var tag in host.Library.Catalog.Tags)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = Ui.Brush(tag.Color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                content.Children.Add(new TextBlock { Text = tag.Name, Foreground = Ui.Brush("#111827"), VerticalAlignment = VerticalAlignment.Center });
                var check = new CheckBox { Content = content, IsChecked = selected.Contains(tag.Id), Margin = new Thickness(4, 7, 4, 7) };
                checks[tag.Id] = check; list.Children.Add(check);
            }
            var apply = Ui.Button("设置以上标签", () => { SelectedTags = checks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList(); DialogResult = true; });
            buttons.Children.Add(apply);
            buttons.Children.Add(Ui.Button("清除标签", () => { SelectedTags = new List<string>(); DialogResult = true; }));
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; buttons.Children.Add(cancel);
            Content = root;
        }
    }
}
