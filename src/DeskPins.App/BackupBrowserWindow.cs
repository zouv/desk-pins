using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class BackupBrowserWindow : Window
    {
        public BackupBrowserWindow(AppHost host, string file)
        {
            Title = Ui.Text("从备份选择性恢复"); Width = 620; Height = 480; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            Ui.FitDialog(this, 460, 320); UiTheme.Apply(this);
            using (var source = new Sqlite(file, true))
            {
                var items = source.Query("SELECT metadata FROM notes;").Select(r => Json.Read<Note>((string)r[0])).ToList();
                var root = new DockPanel();
                var help = new TextBlock { Text = Ui.Text("选择要恢复的便签。相同标识的便签将被备份版本覆盖，恢复前会自动备份当前数据。"), TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted, Margin = new Thickness(0, 0, 0, 12) };
                DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
                var count = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = UiTheme.Muted };
                DockPanel.SetDock(count, Dock.Bottom); root.Children.Add(count);
                var list = new ListBox { ItemsSource = items, SelectionMode = SelectionMode.Extended, BorderBrush = UiTheme.Border };
                var template = new DataTemplate(typeof(Note)); var row = new FrameworkElementFactory(typeof(StackPanel));
                var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayTitle")); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); row.AppendChild(title);
                var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Identifier")); detail.SetValue(TextBlock.ForegroundProperty, UiTheme.Muted); detail.SetValue(TextBlock.FontSizeProperty, 11d); row.AppendChild(detail);
                template.VisualTree = row; list.ItemTemplate = template; root.Children.Add(list);
                var restore = Ui.DangerButton("恢复选中便签", () => { var ids = list.SelectedItems.Cast<Note>().Select(n => n.Id).ToArray(); if (ids.Length == 0 || !host.RestoreBackup(file, true, ids, this)) return; if (IsVisible) DialogResult = true; else Close(); });
                restore.IsEnabled = false;
                list.SelectionChanged += (s, e) => { restore.IsEnabled = list.SelectedItems.Count > 0; count.Text = string.Format(Ui.Text("共 {0} 张，已选 {1} 张"), items.Count, list.SelectedItems.Count); };
                count.Text = string.Format(Ui.Text("共 {0} 张，已选 {1} 张"), items.Count, 0);
                var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true;
                Ui.DialogContent(this, root, restore, cancel);
                Loaded += (s, e) => list.Focus();
            }
        }
    }
}
