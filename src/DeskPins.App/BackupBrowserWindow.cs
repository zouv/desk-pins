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
            Title = "从备份选择性恢复"; Width = 620; Height = 480; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            Ui.FitDialog(this, 460, 320);
            using (var source = new Sqlite(file, true))
            {
                var notes = source.Query("SELECT metadata FROM notes;").Select(r => Json.Read<Note>((string)r[0])).ToList(); var root = new DockPanel { Margin = new Thickness(12) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); var list = new ListBox { ItemsSource = notes, SelectionMode = SelectionMode.Extended, DisplayMemberPath = "DisplayTitle" }; root.Children.Add(list); buttons.Children.Add(Ui.Button("恢复选中便签", () => { try { var ids = list.SelectedItems.Cast<Note>().Select(n => n.Id).ToArray(); if (ids.Length == 0) return; host.FlushAll(); host.Library.Repository.ImportBackup(file, true, ids); foreach (var w in host.Windows.Values.ToArray()) w.CloseInternal(false); host.Windows.Clear(); host.Library.Reload(); host.UpdateAttachments(); host.Sync(); Close(); } catch (Exception ex) { Ui.Error(ex); } })); var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; buttons.Children.Add(cancel); Content = root;
            }
        }
    }
}
