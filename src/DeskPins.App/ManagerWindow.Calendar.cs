using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        private void BuildCalendar() { if (calendarHost != null) calendarHost.Child = BuildCalendarView(); }

        private FrameworkElement BuildCalendarView()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            var navigation = new Grid { Height = 40, Background = Ui.Brush("#1268B3") };
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            var previous = CalendarArrow("‹", -1); navigation.Children.Add(previous);
            var title = new TextBlock { Text = MonthTitle(), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1); navigation.Children.Add(title);
            var next = CalendarArrow("›", 1); Grid.SetColumn(next, 2); navigation.Children.Add(next);
            panel.Children.Add(navigation);

            var week = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 2, 0, 0) };
            for (int i = 0; i < 7; i++)
            {
                week.Children.Add(new Border
                {
                    Background = Ui.Brush("#1F1F1F"), Margin = new Thickness(1), Height = 26,
                    Child = new TextBlock { Text = Ui.Text(WeekdayKeys[i]), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
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
                if (date.Month != calendarMonth.Month) { days.Children.Add(new Border { Background = Ui.Brush("#F3F4F6"), Margin = new Thickness(1), Height = 32 }); continue; }
                int noteCount; counts.TryGetValue(date.Day, out noteCount);
                days.Children.Add(CalendarDay(date, noteCount));
            }
            panel.Children.Add(days);
            return panel;
        }

        private Button CalendarArrow(string glyph, int delta)
        {
            var button = new Button { Content = glyph, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Width = 40, Height = 36, Padding = new Thickness(0), Focusable = false };
            button.Click += (s, e) => { calendarMonth = calendarMonth.AddMonths(delta); calendarSelected = calendarMonth; Refresh(); };
            return button;
        }

        private Button CalendarDay(DateTime date, int noteCount)
        {
            bool today = date.Date == DateTime.Today, selected = date.Date == calendarSelected.Date;
            Brush background = selected ? Ui.Brush("#1268B3") : today ? Brushes.White : Ui.Brush("#E3E5E8");
            Brush foreground = selected ? Brushes.White : Ui.Brush("#111827");
            var indicator = new Border { Height = 3, Width = 18, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Background = noteCount > 0 ? foreground : Brushes.Transparent };
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = date.Day.ToString(), FontSize = 12, Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(indicator);
            var button = new Button
            {
                Tag = date, Content = content, Height = 32, Margin = new Thickness(1), Padding = new Thickness(0), Focusable = false,
                Background = background, BorderBrush = today && !selected ? Brushes.Black : Brushes.White, BorderThickness = new Thickness(1),
                ToolTip = date.ToString("yyyy-MM-dd") + " · " + noteCount + " " + Ui.Text("张便签")
            };
            button.Click += (s, e) => { calendarSelected = (DateTime)((Button)s).Tag; Refresh(); };
            return button;
        }

        private string MonthTitle()
        {
            var culture = new CultureInfo(Ui.CurrentLanguage == Localization.English ? "en-US" : "zh-CN");
            return culture.DateTimeFormat.MonthNames[calendarMonth.Month - 1] + " " + calendarMonth.Year;
        }

        private static bool MatchesDate(Note note, DateTime date)
        {
            return note.CreatedUtc.ToLocalTime().Date == date.Date
                || (note.WakeUtc.HasValue && note.WakeUtc.Value.ToLocalTime().Date == date.Date)
                || (note.Repeat != null && RepeatSchedule.Next(note.Repeat, date.Date.AddTicks(-1))?.Date == date.Date);
        }
    }
}
