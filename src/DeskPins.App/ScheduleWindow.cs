using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class ScheduleWindow : Window
    {
        public ScheduleWindow(AppHost host, Note note, bool sleep, bool repeat)
        {
            Title = repeat ? "重复提醒" : sleep ? "发送便签到休眠" : "设置闹钟"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            var panel = new StackPanel { Margin = new Thickness(16) }; var mode = new ComboBox { ItemsSource = new[] { "指定日期时间", "从现在开始（分钟）" }, SelectedIndex = 0 }; Ui.Field(panel, "时间方式", mode);
            var current = (sleep ? note.WakeUtc : note.AlarmUtc)?.ToLocalTime() ?? DateTime.Now.AddMinutes(5); var date = new DatePicker { SelectedDate = current.Date }; Ui.Field(panel, "日期", date); var time = new TextBox { Text = current.ToString("HH:mm:ss") }; Ui.Field(panel, "时间 / 分钟数", time);
            var kind = new ComboBox { ItemsSource = new[] { "每天", "每周", "每月指定日", "每月第几个星期", "每年", "固定间隔" }, SelectedIndex = repeat && note.Repeat != null ? (int)note.Repeat.Kind : 0 };
            var day = new TextBox { Text = note.Repeat?.Day.ToString() ?? current.Day.ToString() }; var month = new TextBox { Text = note.Repeat?.Month.ToString() ?? current.Month.ToString() }; var position = new TextBox { Text = note.Repeat?.Position.ToString() ?? "1" }; var weekDay = new ComboBox { ItemsSource = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }, SelectedIndex = note.Repeat?.WeekDay ?? 1 };
            var weekdays = new StackPanel { Orientation = Orientation.Horizontal }; var checks = new CheckBox[7]; for (int i = 0; i < 7; i++) { checks[i] = new CheckBox { Content = new[] { "一", "二", "三", "四", "五", "六", "日" }[i], IsChecked = note.Repeat == null || ((note.Repeat.WeekDays & (1 << i)) != 0), Margin = new Thickness(5) }; weekdays.Children.Add(checks[i]); }
            var interval = new TextBox { Text = note.Repeat?.Interval.ToString() ?? "1" }; var unit = new ComboBox { ItemsSource = new[] { "分钟", "小时", "天", "周" }, SelectedIndex = note.Repeat?.IntervalUnit ?? 2 }; var end = new DatePicker { SelectedDate = note.Repeat?.EndLocal?.Date };
            if (repeat) { Ui.Field(panel, "重复方式", kind); panel.Children.Add(new TextBlock { Text = "每周：选择星期", Margin = new Thickness(2, 8, 2, 3) }); panel.Children.Add(weekdays); var row = new Grid(); for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition()); var fields = new[] { day, month, position, interval }; var labels = new[] { "日（1–31）", "月（1–12）", "第几个（-1=最后）", "间隔数量" }; for (int i = 0; i < 4; i++) { var cell = new StackPanel(); Ui.Field(cell, labels[i], fields[i]); Grid.SetColumn(cell, i); row.Children.Add(cell); } panel.Children.Add(row); Ui.Field(panel, "每月指定星期", weekDay); Ui.Field(panel, "间隔单位", unit); Ui.Field(panel, "结束日期（留空表示持续）", end); }
            var top = new CheckBox { Content = "唤醒时置顶", IsChecked = note.WakeTopmost, Margin = new Thickness(4, 10, 4, 4) }; var alarm = new CheckBox { Content = "唤醒时触发闹钟", IsChecked = note.WakeAlarm, Margin = new Thickness(4) }; var sound = new CheckBox { Content = "唤醒时播放声音", IsChecked = note.WakeSound, Margin = new Thickness(4) }; if (sleep) { panel.Children.Add(top); panel.Children.Add(alarm); panel.Children.Add(sound); }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(Ui.Button("清除", () => { note.AlarmUtc = null; if (sleep) { note.WakeUtc = null; note.Repeat = null; note.State = NoteState.Desktop; } host.Library.Save(note); Close(); }));
            var ok = Ui.Button("确定", () => { try { DateTime when; if (mode.SelectedIndex == 1) { double minutes = Ui.Number(time.Text, -1); if (minutes < 0) throw new ArgumentException("请输入有效分钟数。"); when = DateTime.Now.AddMinutes(minutes); } else { TimeSpan t; if (!TimeSpan.TryParse(time.Text, out t) || t.TotalHours >= 24 || t < TimeSpan.Zero || !date.SelectedDate.HasValue) throw new ArgumentException("请填写有效日期和时间。"); when = date.SelectedDate.Value.Date.Add(t); } if (!sleep) { note.AlarmUtc = RepeatSchedule.ToUtc(when); host.Library.Save(note); } else { RepeatRule rule = null; if (repeat) { rule = new RepeatRule { Kind = (RepeatKind)kind.SelectedIndex, StartLocal = when, EndLocal = end.SelectedDate?.AddDays(1).AddTicks(-1), Day = (int)Ui.Number(day.Text, 1), Month = (int)Ui.Number(month.Text, 1), Position = (int)Ui.Number(position.Text, 1), WeekDay = weekDay.SelectedIndex, Interval = (int)Ui.Number(interval.Text, 1), IntervalUnit = unit.SelectedIndex, WeekDays = 0 }; for (int i = 0; i < 7; i++) if (checks[i].IsChecked == true) rule.WeekDays |= 1 << i; if (rule.Kind == RepeatKind.Weekly && rule.WeekDays == 0) throw new ArgumentException("至少选择一个星期。"); if (rule.Interval < 1) throw new ArgumentException("间隔必须大于零。"); } note.WakeTopmost = top.IsChecked == true; note.WakeAlarm = alarm.IsChecked == true; note.WakeSound = sound.IsChecked == true; host.Library.Sleep(note, RepeatSchedule.ToUtc(when), rule); } Close(); } catch (Exception ex) { Ui.Error(ex); } }); ok.IsDefault = true; buttons.Children.Add(ok); var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = new ScrollViewer { Content = panel, MaxHeight = 760, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
    public sealed class AttachWindow : Window
    {
        public AttachWindow(AppHost host, Note note)
        {
            Title = "附着到窗口"; Width = 700; Height = 470; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            var panel = new DockPanel { Margin = new Thickness(16) }; var fields = new StackPanel(); DockPanel.SetDock(fields, Dock.Top); panel.Children.Add(fields);
            var title = new TextBox { Text = note.Attachment?.TitlePattern ?? "*" }; var cls = new TextBox { Text = note.Attachment?.ClassPattern ?? "*" }; var relative = new CheckBox { Content = "相对于目标窗口定位", IsChecked = note.Attachment?.Relative ?? false, Margin = new Thickness(2, 8, 2, 8) };
            Ui.Field(fields, "窗口标题（支持 * 和 ?）", title); Ui.Field(fields, "窗口类名（支持 * 和 ?）", cls); fields.Children.Add(relative);
            var list = new ListBox { ItemsSource = Native.Windows().Where(w => !host.OwnsWindow(w.Handle)).ToList() }; list.SelectionChanged += (s, e) => { var w = list.SelectedItem as WindowInfo; if (w != null) { title.Text = w.Title; cls.Text = w.Class; } };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            buttons.Children.Add(Ui.Button("分离", () => { note.Attachment = null; host.Library.Save(note); Close(); })); buttons.Children.Add(Ui.Button("刷新", () => list.ItemsSource = Native.Windows(true).Where(w => !host.OwnsWindow(w.Handle)).ToList())); buttons.Children.Add(Ui.Button("确定", () => { var w = list.SelectedItem as WindowInfo; note.StackId = ""; note.Attachment = new Attachment { TitlePattern = title.Text, ClassPattern = cls.Text, Relative = relative.IsChecked == true, OffsetX = w == null ? 0 : note.Left - w.Bounds.Left, OffsetY = w == null ? 0 : note.Top - w.Bounds.Top }; host.Library.Save(note); host.UpdateAttachments(); Close(); })); buttons.Children.Add(Ui.Button("取消", () => Close())); panel.Children.Add(list); Content = panel;
        }
    }
}
