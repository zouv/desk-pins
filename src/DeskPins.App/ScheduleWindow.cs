using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class ScheduleWindow : Window
    {
        public ScheduleWindow(AppHost host, Note note, bool sleep, bool repeat)
        {
            Title = Ui.Text(repeat ? "重复提醒" : sleep ? "发送便签到休眠" : "设置闹钟");
            Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            UiTheme.Apply(this); Ui.FitDialog(this, 460, 220);
            var culture = Ui.CurrentLanguage == Localization.English ? CultureInfo.GetCultureInfo("en-US") : Ui.CurrentLanguage == Localization.Chinese ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.CurrentCulture;
            Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 6) });
            panel.Children.Add(new TextBlock { Text = Ui.Text("填写时间后确定；取消不会更改现有提醒。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 6) });
            var mode = new ComboBox { ItemsSource = new[] { Ui.Text("指定日期时间"), Ui.Text("从现在开始（分钟）") }, SelectedIndex = 0 };
            Ui.Field(panel, "时间方式", mode);
            var current = repeat && note.Repeat != null ? note.Repeat.StartLocal : (sleep ? note.WakeUtc : note.AlarmUtc)?.ToLocalTime() ?? DateTime.Now.AddMinutes(5);
            var absolute = new StackPanel(); var relative = new StackPanel { Visibility = Visibility.Collapsed };
            var date = new DatePicker { SelectedDate = current.Date }; var time = new TextBox { Text = current.ToString(culture.DateTimeFormat.LongTimePattern, culture) }; var minutes = new TextBox { Text = "5" };
            Ui.Field(absolute, repeat ? "开始日期" : "日期", date); Ui.Field(absolute, "时间", time);
            absolute.Children.Add(new TextBlock { Text = string.Format(Ui.Text("时间格式示例：{0}"), new DateTime(2000, 1, 1, 14, 30, 0).ToString(culture.DateTimeFormat.LongTimePattern, culture)), Foreground = UiTheme.Muted, Margin = new Thickness(2, 4, 2, 4) });
            Ui.Field(relative, "分钟数（大于 0）", minutes);
            panel.Children.Add(absolute); panel.Children.Add(relative);
            mode.SelectionChanged += (s, e) => { absolute.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; relative.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; };
            var kind = new ComboBox { ItemsSource = new[] { Ui.Text("每天"), Ui.Text("每周"), Ui.Text("每月指定日"), Ui.Text("每月第几个星期"), Ui.Text("每年"), Ui.Text("固定间隔") }, SelectedIndex = repeat && note.Repeat != null ? (int)note.Repeat.Kind : 0 };
            var day = new TextBox { Text = note.Repeat?.Day.ToString() ?? current.Day.ToString() };
            var month = new TextBox { Text = note.Repeat?.Month.ToString() ?? current.Month.ToString() };
            var position = new TextBox { Text = note.Repeat?.Position.ToString() ?? "1" };
            var weekDay = new ComboBox { ItemsSource = new[] { Ui.Text("周日"), Ui.Text("周一"), Ui.Text("周二"), Ui.Text("周三"), Ui.Text("周四"), Ui.Text("周五"), Ui.Text("周六") }, SelectedIndex = note.Repeat?.WeekDay ?? (int)current.DayOfWeek };
            var weekly = new StackPanel(); weekly.Children.Add(new TextBlock { Text = Ui.Text("选择星期"), Margin = new Thickness(2, 10, 2, 4) });
            var weekdays = new WrapPanel(); weekly.Children.Add(weekdays); var checks = new CheckBox[7];
            for (int i = 0; i < 7; i++) { checks[i] = new CheckBox { Content = Ui.Text(new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }[i]), IsChecked = note.Repeat == null || ((note.Repeat.WeekDays & (1 << i)) != 0), Margin = new Thickness(5) }; weekdays.Children.Add(checks[i]); }
            var interval = new TextBox { Text = note.Repeat?.Interval.ToString() ?? "1" };
            var unit = new ComboBox { ItemsSource = new[] { Ui.Text("分钟"), Ui.Text("小时"), Ui.Text("天"), Ui.Text("周") }, SelectedIndex = note.Repeat?.IntervalUnit ?? 2 };
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
            var top = new CheckBox { Content = Ui.Text("唤醒时置顶"), IsChecked = note.WakeTopmost, Margin = new Thickness(4, 10, 4, 4) };
            var alarm = new CheckBox { Content = Ui.Text("唤醒时触发闹钟"), IsChecked = note.WakeAlarm, Margin = new Thickness(4) };
            var sound = new CheckBox { Content = Ui.Text("唤醒时播放声音"), IsChecked = note.WakeSound, Margin = new Thickness(4) };
            if (sleep) { panel.Children.Add(top); panel.Children.Add(alarm); panel.Children.Add(sound); }
            var clear = Ui.DangerButton("清除", () =>
            {
                try { host.FlushNote(note); host.Library.Atomic(() => { if (sleep) host.Library.Move(note, NoteState.Desktop); else { note.AlarmUtc = null; host.Library.Save(note); } }); Close(); }
                catch (Exception ex) { Ui.Error(ex, this); }
            });
            clear.IsEnabled = sleep ? note.WakeUtc.HasValue || note.Repeat != null : note.AlarmUtc.HasValue;
            var existing = new StackPanel { Margin = new Thickness(2, 18, 2, 0) };
            existing.Children.Add(new TextBlock { Text = Ui.Text("现有提醒"), FontWeight = FontWeights.SemiBold });
            existing.Children.Add(new TextBlock { Text = Ui.Text(sleep ? "清除将取消休眠或重复提醒，并将便签移回桌面。" : "清除将移除当前闹钟。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
            clear.HorizontalAlignment = HorizontalAlignment.Left; existing.Children.Add(clear); panel.Children.Add(existing);
            var ok = Ui.PrimaryButton("确定", () =>
            {
                FrameworkElement invalid = mode.SelectedIndex == 1 ? (FrameworkElement)minutes : date;
                bool validating = true;
                try
                {
                    DateTime when;
                    if (mode.SelectedIndex == 1)
                    {
                        double value;
                        if (!double.TryParse(minutes.Text, NumberStyles.Float, culture, out value) || double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new ArgumentException(Ui.Text("请输入大于零的分钟数。"));
                        try { when = DateTime.Now.AddMinutes(value); } catch (ArgumentOutOfRangeException) { throw new ArgumentException(Ui.Text("分钟数过大，请缩短提醒时间。")); }
                    }
                    else
                    {
                        DateTime parsedDate;
                        if (!date.SelectedDate.HasValue || !DateTime.TryParse(date.Text, culture, DateTimeStyles.AllowWhiteSpaces, out parsedDate)) throw new ArgumentException(Ui.Text("请填写有效日期和时间。"));
                        invalid = time;
                        DateTime parsedTime; TimeSpan duration;
                        var formats = new[] { culture.DateTimeFormat.ShortTimePattern, culture.DateTimeFormat.LongTimePattern, "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" };
                        if (DateTime.TryParseExact(time.Text.Trim(), formats, culture, DateTimeStyles.AllowWhiteSpaces, out parsedTime)) duration = parsedTime.TimeOfDay;
                        else if (!TimeSpan.TryParse(time.Text, culture, out duration) || duration.TotalHours >= 24 || duration < TimeSpan.Zero) throw new ArgumentException(Ui.Text("请填写有效日期和时间。"));
                        when = parsedDate.Date.Add(duration);
                    }
                    RepeatRule rule = null;
                    if (repeat)
                    {
                        invalid = end;
                        DateTime? endDate = null;
                        if (!string.IsNullOrWhiteSpace(end.Text)) { DateTime parsed; if (!end.SelectedDate.HasValue || !DateTime.TryParse(end.Text, culture, DateTimeStyles.AllowWhiteSpaces, out parsed)) throw new ArgumentException(Ui.Text("请填写有效结束日期。")); endDate = parsed.Date; }
                        rule = new RepeatRule { Kind = (RepeatKind)kind.SelectedIndex, StartLocal = when, EndLocal = endDate?.AddDays(1).AddTicks(-1) };
                        if (rule.Kind == RepeatKind.Weekly) { invalid = checks[0]; rule.WeekDays = 0; for (int i = 0; i < 7; i++) if (checks[i].IsChecked == true) rule.WeekDays |= 1 << i; if (rule.WeekDays == 0) throw new ArgumentException(Ui.Text("至少选择一个星期。")); }
                        if (rule.Kind == RepeatKind.MonthlyDay || rule.Kind == RepeatKind.Yearly) { invalid = day; rule.Day = Integer(day.Text, 1, 31, "日", culture); }
                        if (rule.Kind == RepeatKind.Yearly) { invalid = month; rule.Month = Integer(month.Text, 1, 12, "月", culture); }
                        if (rule.Kind == RepeatKind.MonthlyPosition) { invalid = position; rule.Position = Integer(position.Text, -1, 5, "第几个", culture); if (rule.Position == 0) throw new ArgumentException(Ui.Text("第几个需为 1–5 或 -1。")); rule.WeekDay = weekDay.SelectedIndex; }
                        if (rule.Kind == RepeatKind.Interval) { invalid = interval; rule.Interval = Integer(interval.Text, 1, int.MaxValue, "间隔数量", culture); rule.IntervalUnit = unit.SelectedIndex; }
                        invalid = end;
                        if (rule.EndLocal.HasValue && rule.EndLocal < when) throw new ArgumentException(Ui.Text("结束日期不能早于开始日期。"));
                        invalid = mode.SelectedIndex == 1 ? (FrameworkElement)minutes : date;
                        var next = RepeatSchedule.Next(rule, DateTime.Now); if (!next.HasValue) throw new ArgumentException(Ui.Text("此规则没有未来提醒时间，请调整日期。")); when = next.Value;
                    }
                    else if (when <= DateTime.Now) { invalid = mode.SelectedIndex == 1 ? (FrameworkElement)minutes : date; throw new ArgumentException(Ui.Text("请选择未来的提醒时间。")); }
                    var utc = RepeatSchedule.ToUtc(when); validating = false; host.FlushNote(note);
                    host.Library.Atomic(() => { if (!sleep) { note.AlarmUtc = utc; host.Library.Save(note); } else { note.WakeTopmost = top.IsChecked == true; note.WakeAlarm = alarm.IsChecked == true; note.WakeSound = sound.IsChecked == true; host.Library.Sleep(note, utc, rule); } }); Close();
                }
                catch (Exception ex) { if (validating) CollectionEditor.ValidationError(this, invalid, ex.Message); else Ui.Error(ex, this); }
            });
            ok.IsDefault = true; var cancel = Ui.Button("取消", Close); cancel.IsCancel = true;
            Ui.DialogContent(this, panel, ok, cancel);
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => { time.Focus(); time.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        }
        private static int Integer(string text, int min, int max, string label, CultureInfo culture)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, culture, out value) || value < min || value > max) throw new ArgumentException(string.Format(Ui.Text("{0}需为 {1}–{2} 范围内的整数。"), Ui.Text(label), min.ToString(culture), max.ToString(culture)));
            return value;
        }
    }
    public sealed class AttachWindow : Window
    {
        public AttachWindow(AppHost host, Note note)
        {
            Title = Ui.Text("附着到窗口"); Width = 700; Height = 570; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            UiTheme.Apply(this); Ui.FitDialog(this, 540, 400);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 6) });
            panel.Children.Add(new TextBlock { Text = Ui.Text("先选择一个窗口；需要通配符匹配时展开高级规则。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 10) });
            var title = new TextBox { Text = note.Attachment?.TitlePattern ?? "*" };
            var cls = new TextBox { Text = note.Attachment?.ClassPattern ?? "*" };
            var relative = new CheckBox { Content = Ui.Text("相对于目标窗口定位"), IsChecked = note.Attachment?.Relative ?? false, Margin = new Thickness(2, 10, 2, 10) };
            var list = new ListBox { ItemsSource = Native.Windows().Where(w => !host.OwnsWindow(w.Handle)).ToList(), Height = 210, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            System.Windows.Automation.AutomationProperties.SetName(list, Ui.Text("可附着窗口"));
            var summary = new TextBlock { Text = Ui.Text("尚未选择窗口；可使用高级规则。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 2, 6) };
            bool picking = false;
            list.SelectionChanged += (s, e) =>
            {
                var w = list.SelectedItem as WindowInfo;
                if (w == null) { summary.Text = Ui.Text("尚未选择窗口；可使用高级规则。"); return; }
                picking = true; title.Text = w.Title; cls.Text = w.Class; picking = false; summary.Text = w.Title;
            };
            title.TextChanged += (s, e) => { if (!picking) list.SelectedItem = null; };
            cls.TextChanged += (s, e) => { if (!picking) list.SelectedItem = null; };
            var listTools = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var refresh = Ui.Button("刷新", () =>
            {
                var selected = list.SelectedItem as WindowInfo;
                var windows = Native.Windows(true).Where(w => !host.OwnsWindow(w.Handle)).ToList();
                list.ItemsSource = windows; list.SelectedItem = selected == null ? null : windows.FirstOrDefault(w => w.Handle == selected.Handle); list.Focus();
            }); DockPanel.SetDock(refresh, Dock.Right); listTools.Children.Add(refresh);
            listTools.Children.Add(new TextBlock { Text = Ui.Text("可附着窗口"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2) });
            panel.Children.Add(listTools); panel.Children.Add(list); panel.Children.Add(summary); panel.Children.Add(relative);
            var fields = new StackPanel();
            Ui.Field(fields, "窗口标题（支持 * 和 ?）", title); Ui.Field(fields, "窗口类名（支持 * 和 ?）", cls);
            fields.Children.Add(new TextBlock { Text = Ui.Text("* 匹配任意内容，? 匹配单个字符；留空不可保存。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 2, 6) });
            var advanced = new Expander { Header = Ui.Text("高级匹配规则"), Content = fields, IsExpanded = note.Attachment != null, Margin = new Thickness(2, 4, 2, 8) }; panel.Children.Add(advanced);
            var detach = Ui.DangerButton("分离", () =>
            {
                try { host.FlushNote(note); host.Library.Atomic(() => { note.Attachment = null; host.Library.Save(note); }); host.UpdateAttachments(); Close(); }
                catch (Exception ex) { Ui.Error(ex, this); }
            }); detach.IsEnabled = note.Attachment != null;
            var ok = Ui.PrimaryButton("确定", () =>
            {
                if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(cls.Text)) { advanced.IsExpanded = true; CollectionEditor.ValidationError(this, string.IsNullOrWhiteSpace(title.Text) ? title : cls, "请填写窗口标题和类名；匹配全部请使用 *。"); return; }
                if (list.SelectedItem == null && !advanced.IsExpanded) { advanced.IsExpanded = true; CollectionEditor.ValidationError(this, title, "请选择窗口，或填写高级匹配规则。"); return; }
                try
                {
                    host.FlushNote(note); var w = list.SelectedItem as WindowInfo;
                    host.Library.Atomic(() => { note.StackId = ""; note.Attachment = new Attachment { TitlePattern = title.Text, ClassPattern = cls.Text, Relative = relative.IsChecked == true, OffsetX = w == null ? note.Attachment?.OffsetX ?? 0 : note.Left - w.Bounds.Left, OffsetY = w == null ? note.Attachment?.OffsetY ?? 0 : note.Top - w.Bounds.Top }; host.Library.Save(note); });
                    host.UpdateAttachments(); Close();
                }
                catch (Exception ex) { Ui.Error(ex, this); }
            }); ok.IsDefault = true;
            var cancel = Ui.Button("取消", Close); cancel.IsCancel = true;
            detach.HorizontalAlignment = HorizontalAlignment.Left; detach.Margin = new Thickness(2, 12, 2, 4); panel.Children.Add(detach);
            Ui.DialogContent(this, panel, ok, cancel);
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => { if (advanced.IsExpanded) { title.Focus(); title.SelectAll(); } else list.Focus(); }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
