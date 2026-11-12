using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
                Width = 450, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Owner = owner
            };
            UiTheme.Apply(window); Ui.FitDialog(window, 380, 0);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = window.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 6) });
            panel.Children.Add(new TextBlock { Text = Ui.Text("修改属性后保存；取消不会更改原项目。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 6) });
            var name = new TextBox { Text = folder != null ? folder.Name : stack != null ? stack.Name : tag.Name };
            Ui.Field(panel, "名称", name);

            ComboBox parent = null;
            if (folder != null)
            {
                var roots = new[] { new Folder { Id = "", Name = Ui.Text("根目录") } }
                    .Concat(host.Library.Catalog.Folders.Where(f => f.Id != folder.Id && !IsUnder(host, f.Id, folder.Id))).ToList();
                parent = new ComboBox { ItemsSource = roots, ItemTemplate = NameTemplate(), SelectedItem = roots.FirstOrDefault(f => f.Id == folder.ParentId) ?? roots[0] };
                Ui.Field(panel, "父文件夹", parent);
            }

            string color = stack != null ? stack.Color : tag != null ? tag.Color : null;
            Border swatch = null;
            if (color != null)
            {
                var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
                swatch = new Border { Width = 64, Height = 28, Background = Ui.Brush(color), BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1), Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var choose = Ui.Button("颜色…", () => { var value = Ui.Color(color); if (value != null) { color = value; swatch.Background = Ui.Brush(value); } });
                colorRow.Children.Add(swatch); colorRow.Children.Add(choose);
                Ui.Field(panel, "颜色", colorRow);
            }

            CheckBox topmost = null;
            if (stack != null)
            {
                topmost = new CheckBox { Content = Ui.Text("置顶"), IsChecked = stack.Topmost, Margin = new Thickness(2, 12, 2, 4) };
                panel.Children.Add(topmost);
            }

            var save = Ui.PrimaryButton("保存", () =>
            {
                var value = (name.Text ?? "").Trim();
                if (value.Length == 0) { ValidationError(window, name, "名称不能为空。"); return; }
                if (folder != null)
                {
                    var selected = parent.SelectedItem as Folder;
                    if (selected == null || selected.Id == folder.Id || IsUnder(host, selected.Id, folder.Id)) { ValidationError(window, parent, "文件夹不能循环嵌套。"); return; }
                    folder.Name = value; folder.ParentId = selected.Id;
                }
                else if (stack != null) { stack.Name = value; stack.Color = color; stack.Topmost = topmost.IsChecked == true; }
                else if (tag != null) { tag.Name = value; tag.Color = color; }
                window.DialogResult = true;
            });
            save.IsDefault = true;
            var cancel = Ui.Button("取消", () => window.Close()); cancel.IsCancel = true;
            Ui.DialogContent(window, panel, save, cancel);
            window.Loaded += (s, e) => window.Dispatcher.BeginInvoke(new Action(() => { name.Focus(); name.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
            return window.ShowDialog() == true;
        }

        internal static DataTemplate NameTemplate()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new CollectionNameConverter() });
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            return new DataTemplate { VisualTree = text };
        }

        private sealed class CollectionNameConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var folder = value as Folder; if (folder != null) return folder.Name;
                var friend = value as Friend; if (friend != null) return friend.Name;
                var tag = value as Tag; if (tag != null) return tag.Name;
                return value == null ? "" : value.ToString();
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return Binding.DoNothing; }
        }

        internal static void ValidationError(Window window, FrameworkElement control, string message)
        {
            Ui.Error(new ArgumentException(Ui.Text(message)), window);
            control.BringIntoView(); control.Focus();
            var text = control as TextBox; if (text != null) text.SelectAll();
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
            UiTheme.Apply(this); Ui.FitDialog(this, 360, 260);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = Ui.Text("搜索标签"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 8) });
            panel.Children.Add(new TextBlock { Text = Ui.Text("匹配任一选中标签；不选标签表示不限制标签。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 10) });
            var selected = new HashSet<string>(initial ?? new string[0]);
            var checks = new Dictionary<string, CheckBox>();
            if (host.Library.Catalog.Tags.Count == 0) panel.Children.Add(new TextBlock { Text = Ui.Text("没有标签。"), Foreground = UiTheme.Muted, Margin = new Thickness(4, 8, 4, 8) });
            foreach (var tag in host.Library.Catalog.Tags)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = Ui.Brush(tag.Color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                content.Children.Add(new TextBlock { Text = tag.Name, Foreground = UiTheme.Text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                var check = new CheckBox { Content = content, IsChecked = selected.Contains(tag.Id), Margin = new Thickness(4, 7, 4, 7) };
                System.Windows.Automation.AutomationProperties.SetName(check, tag.Name);
                checks[tag.Id] = check; panel.Children.Add(check);
            }
            var apply = Ui.PrimaryButton("应用标签", () => { SelectedTags = checks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList(); DialogResult = true; });
            apply.IsDefault = true;
            var clear = Ui.Button("清除筛选", () => { SelectedTags = new List<string>(); DialogResult = true; });
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true;
            Ui.DialogContent(this, panel, clear, apply, cancel);
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => { var first = checks.Values.FirstOrDefault(); if (first != null) first.Focus(); else apply.Focus(); }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
