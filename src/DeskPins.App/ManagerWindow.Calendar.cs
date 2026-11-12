using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed partial class ManagerWindow
    {
        private readonly Border calendarHost;
        private DateTime calendarSelected = DateTime.Today;
        private DateTime calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private static readonly string[] WeekdayKeys = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

        private void BuildCalendar()
        {
            if (calendarHost == null) return;
            // The compact month fits without an inner scrollbar; the note list keeps the remaining height.
            calendarHost.Child = BuildCalendarView();
        }

        private FrameworkElement BuildCalendarView()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
            var navigation = new Grid { Height = 32, Background = UiTheme.Background };
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var previous = CalendarArrow("‹", -1); navigation.Children.Add(previous);
            var title = new TextBlock { Text = MonthTitle(), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1); navigation.Children.Add(title);
            var next = CalendarArrow("›", 1); Grid.SetColumn(next, 2); navigation.Children.Add(next);
            panel.Children.Add(navigation);

            var week = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 2, 0, 0) };
            for (int i = 0; i < 7; i++)
            {
                week.Children.Add(new Border
                {
                    Background = UiTheme.Background, Margin = new Thickness(1), Height = 22,
                    Child = new TextBlock { Text = Ui.Text(WeekdayKeys[i]), FontSize = 11, FontWeight = i >= 5 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = i >= 5 ? UiTheme.Accent : UiTheme.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
            }
            panel.Children.Add(week);

            var counts = new Dictionary<int, int>();
            int daysInMonth = DateTime.DaysInMonth(calendarMonth.Year, calendarMonth.Month);
            for (int day = 1; day <= daysInMonth; day++) counts[day] = host.Library.Notes.Values.Count(n => MatchesDate(n, new DateTime(calendarMonth.Year, calendarMonth.Month, day)));
            var days = new UniformGrid { Rows = 6, Columns = 7, Margin = new Thickness(0, 0, 0, 0) };
            int offset = ((int)calendarMonth.DayOfWeek + 6) % 7;
            for (int i = 0; i < 42; i++)
            {
                var date = calendarMonth.AddDays(i - offset);
                if (date.Month != calendarMonth.Month) { days.Children.Add(new Border { Background = UiTheme.Background, Margin = new Thickness(1), Height = 26 }); continue; }
                int noteCount; counts.TryGetValue(date.Day, out noteCount);
                days.Children.Add(CalendarDay(date, noteCount));
            }
            panel.Children.Add(days);
            return panel;
        }

        private Button CalendarArrow(string glyph, int delta)
        {
            var button = new Button { Content = glyph, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Accent, Background = Brushes.Transparent, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1), Width = 32, Height = 28, Padding = new Thickness(0), ToolTip = Ui.Text(delta < 0 ? "上个月" : "下个月") };
            System.Windows.Automation.AutomationProperties.SetName(button, Ui.Text(delta < 0 ? "上个月" : "下个月"));
            button.Click += (s, e) => { calendarMonth = calendarMonth.AddMonths(delta); calendarSelected = calendarMonth; Refresh(); FocusCalendarDate(); };
            return button;
        }

        private Button CalendarDay(DateTime date, int noteCount)
        {
            bool today = date.Date == DateTime.Today, selected = date.Date == calendarSelected.Date;
            Brush background = selected ? UiTheme.Accent : UiTheme.Surface;
            Brush foreground = selected ? Brushes.White : (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) ? UiTheme.Accent : UiTheme.Text;
            var indicator = new Border { Height = 2, Width = 16, Margin = new Thickness(0, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Background = noteCount > 0 ? foreground : Brushes.Transparent };
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = date.Day.ToString(), FontSize = 11, Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(indicator);
            var button = new Button
            {
                Tag = date, Content = content, Height = 26, Margin = new Thickness(1), Padding = new Thickness(0),
                Background = background, BorderBrush = today && !selected ? UiTheme.Accent : UiTheme.Border, BorderThickness = new Thickness(today ? 2 : 1),
                ToolTip = date.ToString("yyyy-MM-dd") + " · " + noteCount + " " + Ui.Text("张便签")
            };
            System.Windows.Automation.AutomationProperties.SetName(button, date.ToString("yyyy-MM-dd") + " · " + noteCount + " " + Ui.Text("张便签"));
            button.Click += (s, e) => { calendarSelected = (DateTime)((Button)s).Tag; Refresh(); FocusCalendarDate(); };
            button.KeyDown += (s, e) =>
            {
                var target = date;
                if (e.Key == Key.Left) target = date.AddDays(-1);
                else if (e.Key == Key.Right) target = date.AddDays(1);
                else if (e.Key == Key.Up) target = date.AddDays(-7);
                else if (e.Key == Key.Down) target = date.AddDays(7);
                else if (e.Key == Key.PageUp) target = target.AddMonths(-1);
                else if (e.Key == Key.PageDown) target = target.AddMonths(1);
                else if (e.Key == Key.Home) target = DateTime.Today;
                else return;
                e.Handled = true; calendarSelected = target; calendarMonth = new DateTime(target.Year, target.Month, 1); Refresh(); FocusCalendarDate();
            };
            return button;
        }

        private void FocusCalendarDate()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var panel = calendarHost.Child as Panel;
                var days = panel == null ? null : panel.Children.OfType<UniformGrid>().LastOrDefault();
                var selected = days == null ? null : days.Children.OfType<Button>().FirstOrDefault(b => b.Tag is DateTime date && date.Date == calendarSelected.Date);
                if (selected != null) { selected.Focus(); selected.BringIntoView(); }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private string MonthTitle()
        {
            var culture = new CultureInfo(Ui.CurrentLanguage == Localization.English ? "en-US" : "zh-CN");
            return calendarMonth.ToString(Ui.CurrentLanguage == Localization.English ? "MMMM yyyy" : "yyyy年M月", culture);
        }

        private static bool MatchesDate(Note note, DateTime date)
        {
            return note.CreatedUtc.ToLocalTime().Date == date.Date
                || (note.WakeUtc.HasValue && note.WakeUtc.Value.ToLocalTime().Date == date.Date)
                || (note.Repeat != null && RepeatSchedule.Next(note.Repeat, date.Date.AddTicks(-1))?.Date == date.Date);
        }
    }
}
