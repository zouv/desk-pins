using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskPins.Core;

namespace DeskPins.App
{
    // 桌面便签与管理器共用；只读展示元数据，序号是本地自增编号，标识符才是 GUID 身份。
    public sealed class NotePropertiesWindow : Window
    {
        public static void Show(AppHost host, Note note, Window owner) { if (note == null) return; new NotePropertiesWindow(host, note, owner).ShowDialog(); }
        public NotePropertiesWindow(AppHost host, Note note, Window owner)
        {
            Title = Ui.Text("便签属性"); Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            if (owner != null) Owner = owner;
            UiTheme.Apply(this); Ui.FitDialog(this, 400, 0);
            var panel = new StackPanel { Margin = new Thickness(20) };
            var folder = host.Library.Catalog.Folders.FirstOrDefault(f => f.Id == note.FolderId);
            var stack = host.Library.Catalog.Stacks.FirstOrDefault(s => s.Id == note.StackId);
            string[,] values = {
                { "标题", note.DisplayTitle }, { "状态", Ui.StateText(note.State) }, { "类型", Ui.Text(note.Kind == NoteKind.Text ? "文本" : "图片") },
                { "创建时间", note.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") }, { "修改时间", note.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") },
                { "文件夹", folder == null ? Ui.Text("无") : host.FolderPath(folder) }, { "堆叠", stack == null ? Ui.Text("无") : stack.Name },
                { "标签", string.Join(" / ", note.Tags.Select(id => host.Library.Catalog.Tags.FirstOrDefault(t => t.Id == id)?.Name).Where(name => name != null)) },
                { "加密", Ui.Text(note.Encrypted ? "是" : "否") }, { "锁定", Ui.Text(note.Locked ? "是" : "否") }, { "序号", note.Number > 0 ? note.Number.ToString() : "" }, { "标识符", note.Id }
            };
            for (int i = 0; i < values.GetLength(0); i++)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = Ui.Text(values[i, 0]), Foreground = UiTheme.Muted });
                var value = new TextBox { Text = values[i, 1] ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Foreground = UiTheme.Text };
                Grid.SetColumn(value, 1); row.Children.Add(value); panel.Children.Add(row);
            }
            var close = Ui.Button("关闭", () => Close()); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(close); Content = panel;
        }
    }
}
