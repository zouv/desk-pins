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
            Title = repeat ? "重复提醒" : sleep ? "发送便签到休眠" : "设置闹钟";
            Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            Ui.FitDialog(this, 460, 220);
            var root = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 4, 12, 12) };
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var panel = new StackPanel { Margin = new Thickness(16, 6, 16, 6) };
            root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var mode = new ComboBox { ItemsSource = new[] { "指定日期时间", "从现在开始（分钟）" }, SelectedIndex = 0 };
            Ui.Field(panel, "时间方式", mode);
            var current = repeat && note.Repeat != null ? note.Repeat.StartLocal : (sleep ? note.WakeUtc : note.AlarmUtc)?.ToLocalTime() ?? DateTime.Now.AddMinutes(5);
            var absolute = new StackPanel(); var relative = new StackPanel { Visibility = Visibility.Collapsed };
            var date = new DatePicker { SelectedDate = current.Date }; var time = new TextBox { Text = current.ToString("HH:mm:ss") }; var minutes = new TextBox { Text = "5" };
            Ui.Field(absolute, repeat ? "开始日期" : "日期", date); Ui.Field(absolute, "时间", time); Ui.Field(relative, "分钟数（大于 0）", minutes);
            panel.Children.Add(absolute); panel.Children.Add(relative);
            mode.SelectionChanged += (s, e) => { absolute.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; relative.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; };
            var kind = new ComboBox { ItemsSource = new[] { "每天", "每周", "每月指定日", "每月第几个星期", "每年", "固定间隔" }, SelectedIndex = repeat && note.Repeat != null ? (int)note.Repeat.Kind : 0 };
            var day = new TextBox { Text = note.Repeat?.Day.ToString() ?? current.Day.ToString() };
            var month = new TextBox { Text = note.Repeat?.Month.ToString() ?? current.Month.ToString() };
            var position = new TextBox { Text = note.Repeat?.Position.ToString() ?? "1" };
            var weekDay = new ComboBox { ItemsSource = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }, SelectedIndex = note.Repeat?.WeekDay ?? (int)current.DayOfWeek };
            var weekly = new StackPanel(); weekly.Children.Add(new TextBlock { Text = "选择星期", Margin = new Thickness(2, 10, 2, 4) });
            var weekdays = new WrapPanel(); weekly.Children.Add(weekdays); var checks = new CheckBox[7];
            for (int i = 0; i < 7; i++) { checks[i] = new CheckBox { Content = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }[i], IsChecked = note.Repeat == null || ((note.Repeat.WeekDays & (1 << i)) != 0), Margin = new Thickness(5) }; weekdays.Children.Add(checks[i]); }
            var interval = new TextBox { Text = note.Repeat?.Interval.ToString() ?? "1" };
            var unit = new ComboBox { ItemsSource = new[] { "分钟", "小时", "天", "周" }, SelectedIndex = note.Repeat?.IntervalUnit ?? 2 };
            var end = new DatePicker { SelectedDate = note.Repeat?.EndLocal?.Date };
            if (repeat)
            {
                Ui.Field(panel, "重复方式", kind); panel.Children.Add(weekly);
                var monthlyDay = new StackPanel(); Ui.Field(monthlyDay, "日（1–31）", day); panel.Children.Add(monthlyDay);
                var yearlyMonth = new StackPanel(); Ui.Field(yearlyMonth, "月（1–12）", month); panel.Children.Add(yearlyMonth);
                var monthlyPosition = new StackPanel(); Ui.Field(monthlyPosition, "第几个（1–5，-1 表示最后）", position); Ui.Field(monthlyPosition, "星期", weekDay); panel.Children.Add(monthlyPosition);
                var fixedInterval = new StackPanel(); Ui.Field(fixedInterval, "间隔数量（正整数）", interval); Ui.Field(fixedInterval, "间隔单位", unit); panel.Children.Add(fixedInterval);
                Action update = () => { weekly.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; monthlyDay.Visibility = kind.SelectedIndex == 2 || kind.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed; yearlyMonth.Visibility = kind.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed; monthlyPosition.Visibility = kind.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed; fixedInterval.Visibility = kind.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed; };
                kind.SelectionChanged += (s, e) => update(); update(); Ui.Field(panel, "结束日期（留空表示持续）", end);
            }
            var top = new CheckBox { Content = "唤醒时置顶", IsChecked = note.WakeTopmost, Margin = new Thickness(4, 10, 4, 4) };
            var alarm = new CheckBox { Content = "唤醒时触发闹钟", IsChecked = note.WakeAlarm, Margin = new Thickness(4) };
            var sound = new CheckBox { Content = "唤醒时播放声音", IsChecked = note.WakeSound, Margin = new Thickness(4) };
            if (sleep) { panel.Children.Add(top); panel.Children.Add(alarm); panel.Children.Add(sound); }
            buttons.Children.Add(Ui.Button("清除", () => { host.FlushNote(note); host.Library.Atomic(() => { if (sleep) host.Library.Move(note, NoteState.Desktop); else { note.AlarmUtc = null; host.Library.Save(note); } }); Close(); }));
            var ok = Ui.Button("确定", () =>
            {
                DateTime when;
                if (mode.SelectedIndex == 1) { double value = Ui.Number(minutes.Text, -1); if (value <= 0) throw new ArgumentException("请输入大于零的分钟数。"); when = DateTime.Now.AddMinutes(value); }
                else { TimeSpan t; if (!TimeSpan.TryParse(time.Text, out t) || t.TotalHours >= 24 || t < TimeSpan.Zero || !date.SelectedDate.HasValue) throw new ArgumentException("请填写有效日期和时间。"); when = date.SelectedDate.Value.Date.Add(t); }
                RepeatRule rule = null;
                if (repeat)
                {
                    rule = new RepeatRule { Kind = (RepeatKind)kind.SelectedIndex, StartLocal = when, EndLocal = end.SelectedDate?.AddDays(1).AddTicks(-1) };
                    if (rule.Kind == RepeatKind.Weekly) { rule.WeekDays = 0; for (int i = 0; i < 7; i++) if (checks[i].IsChecked == true) rule.WeekDays |= 1 << i; if (rule.WeekDays == 0) throw new ArgumentException("至少选择一个星期。"); }
                    if (rule.Kind == RepeatKind.MonthlyDay || rule.Kind == RepeatKind.Yearly) rule.Day = Integer(day.Text, 1, 31, "日");
                    if (rule.Kind == RepeatKind.Yearly) rule.Month = Integer(month.Text, 1, 12, "月");
                    if (rule.Kind == RepeatKind.MonthlyPosition) { rule.Position = Integer(position.Text, -1, 5, "第几个"); if (rule.Position == 0) throw new ArgumentException("第几个需为 1–5 或 -1。"); rule.WeekDay = weekDay.SelectedIndex; }
                    if (rule.Kind == RepeatKind.Interval) { rule.Interval = Integer(interval.Text, 1, int.MaxValue, "间隔数量"); rule.IntervalUnit = unit.SelectedIndex; }
                    if (rule.EndLocal.HasValue && rule.EndLocal < when) throw new ArgumentException("结束日期不能早于开始日期。");
                    var next = RepeatSchedule.Next(rule, DateTime.Now); if (!next.HasValue) throw new ArgumentException("此规则没有未来提醒时间，请调整日期。"); when = next.Value;
                }
                else if (when <= DateTime.Now) throw new ArgumentException("请选择未来的提醒时间。");
                var utc = RepeatSchedule.ToUtc(when); host.FlushNote(note);
                host.Library.Atomic(() => { if (!sleep) { note.AlarmUtc = utc; host.Library.Save(note); } else { note.WakeTopmost = top.IsChecked == true; note.WakeAlarm = alarm.IsChecked == true; note.WakeSound = sound.IsChecked == true; host.Library.Sleep(note, utc, rule); } }); Close();
            });
            ok.IsDefault = true; buttons.Children.Add(ok); var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; buttons.Children.Add(cancel);
            Content = root; Loaded += (s, e) => time.Focus();
        }
        private static int Integer(string text, int min, int max, string label) { int value; if (!int.TryParse(text, out value) || value < min || value > max) throw new ArgumentException(label + " 需在 " + min + "–" + max + " 范围内。"); return value; }
    }
    public sealed class AttachWindow : Window
    {
        public AttachWindow(AppHost host, Note note)
        {
            Title = "附着到窗口"; Width = 700; Height = 470; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Ui.FitDialog(this, 540, 400);
            var panel = new DockPanel { Margin = new Thickness(16) }; var fields = new StackPanel(); DockPanel.SetDock(fields, Dock.Top); panel.Children.Add(fields);
            var title = new TextBox { Text = note.Attachment?.TitlePattern ?? "*" }; var cls = new TextBox { Text = note.Attachment?.ClassPattern ?? "*" }; var relative = new CheckBox { Content = "相对于目标窗口定位", IsChecked = note.Attachment?.Relative ?? false, Margin = new Thickness(2, 8, 2, 8) };
            Ui.Field(fields, "窗口标题（支持 * 和 ?）", title); Ui.Field(fields, "窗口类名（支持 * 和 ?）", cls); fields.Children.Add(relative);
            var list = new ListBox { ItemsSource = Native.Windows().Where(w => !host.OwnsWindow(w.Handle)).ToList() }; list.SelectionChanged += (s, e) => { var w = list.SelectedItem as WindowInfo; if (w != null) { title.Text = w.Title; cls.Text = w.Class; } };
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            buttons.Children.Add(Ui.Button("分离", () => { host.FlushNote(note); note.Attachment = null; host.Library.Save(note); host.UpdateAttachments(); Close(); }));
            buttons.Children.Add(Ui.Button("刷新", () => list.ItemsSource = Native.Windows(true).Where(w => !host.OwnsWindow(w.Handle)).ToList()));
            var ok = Ui.Button("确定", () => { if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(cls.Text)) throw new ArgumentException("请填写窗口标题和类名；匹配全部请使用 *。"); host.FlushNote(note); var w = list.SelectedItem as WindowInfo; host.Library.Atomic(() => { note.StackId = ""; note.Attachment = new Attachment { TitlePattern = title.Text, ClassPattern = cls.Text, Relative = relative.IsChecked == true, OffsetX = w == null ? note.Attachment?.OffsetX ?? 0 : note.Left - w.Bounds.Left, OffsetY = w == null ? note.Attachment?.OffsetY ?? 0 : note.Top - w.Bounds.Top }; host.Library.Save(note); }); host.UpdateAttachments(); Close(); }); ok.IsDefault = true; buttons.Children.Add(ok);
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; buttons.Children.Add(cancel); panel.Children.Add(list); Content = panel;
        }
    }
}
