using System;
using System.Text.RegularExpressions;

namespace DeskPins.Core
{
    public static class RepeatSchedule
    {
        public static DateTime? Next(RepeatRule rule, DateTime afterLocal)
        {
            if (rule == null) return null;
            if (!Enum.IsDefined(typeof(RepeatKind), rule.Kind)) throw new ArgumentException("重复方式无效。");
            if (rule.Kind == RepeatKind.Weekly && (rule.WeekDays & 127) == 0) throw new ArgumentException("请至少选择一个星期。");
            if (rule.Kind == RepeatKind.Interval && (rule.Interval < 1 || rule.IntervalUnit < 0 || rule.IntervalUnit > 3)) throw new ArgumentException("重复间隔无效。");
            try { return NextCore(rule, afterLocal); } catch (ArgumentOutOfRangeException) { return null; }
        }
        private static DateTime? NextCore(RepeatRule rule, DateTime afterLocal)
        {
            if (rule == null) return null;
            var start = DateTime.SpecifyKind(rule.StartLocal, DateTimeKind.Unspecified);
            var after = DateTime.SpecifyKind(afterLocal, DateTimeKind.Unspecified);
            DateTime candidate;
            if (after < start) candidate = start;
            else if (rule.Kind == RepeatKind.Interval)
            {
                double seconds = Math.Max(1, rule.Interval) * (rule.IntervalUnit == 0 ? 60d : rule.IntervalUnit == 1 ? 3600d : rule.IntervalUnit == 2 ? 86400d : 604800d);
                double steps = Math.Floor((after - start).TotalSeconds / seconds) + 1;
                try { candidate = start.AddSeconds(steps * seconds); } catch (ArgumentOutOfRangeException) { return null; }
            }
            else
            {
                candidate = after.Date.Add(start.TimeOfDay);
                if (candidate <= after) candidate = candidate.AddDays(1);
                if (rule.Kind == RepeatKind.Daily) { }
                else if (rule.Kind == RepeatKind.Weekly)
                {
                    if ((rule.WeekDays & 127) == 0) throw new ArgumentException("请至少选择一个星期。");
                    for (int i = 0; i < 7; i++) { int day = ((int)candidate.DayOfWeek + 6) % 7; if ((rule.WeekDays & (1 << day)) != 0) break; candidate = candidate.AddDays(1); }
                }
                else if (rule.Kind == RepeatKind.Yearly)
                {
                    for (int year = after.Year; ; year++) { candidate = new DateTime(year, Clamp(rule.Month, 1, 12), Math.Min(Clamp(rule.Day, 1, 31), DateTime.DaysInMonth(year, Clamp(rule.Month, 1, 12)))).Add(start.TimeOfDay); if (candidate > after && candidate >= start) break; if (year == 9999) return null; }
                }
                else
                {
                    var month = new DateTime(after.Year, after.Month, 1);
                    for (int i = 0; i < 120000; i++)
                    {
                        int day;
                        if (rule.Kind == RepeatKind.MonthlyDay) day = Math.Min(Clamp(rule.Day, 1, 31), DateTime.DaysInMonth(month.Year, month.Month));
                        else
                        {
                            int desired = Clamp(rule.WeekDay, 0, 6);
                            if (rule.Position < 0) { day = DateTime.DaysInMonth(month.Year, month.Month); day -= ((int)new DateTime(month.Year, month.Month, day).DayOfWeek - desired + 7) % 7; }
                            else { day = 1 + (desired - (int)month.DayOfWeek + 7) % 7 + (Clamp(rule.Position, 1, 5) - 1) * 7; if (day > DateTime.DaysInMonth(month.Year, month.Month)) day -= 7; }
                        }
                        candidate = month.AddDays(day - 1).Add(start.TimeOfDay);
                        if (candidate > after && candidate >= start) break;
                        if (month.Year == 9999 && month.Month == 12) return null;
                        month = month.AddMonths(1);
                    }
                }
            }
            if (rule.EndLocal.HasValue && candidate > rule.EndLocal.Value) return null;
            return DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
        }
        public static DateTime ToUtc(DateTime local)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local)) local = local.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(local);
        }
        private static int Clamp(int x, int min, int max) { return Math.Max(min, Math.Min(max, x)); }
    }
    public static class WindowMatch
    {
        public static bool Wildcard(string text, string pattern) { return Regex.IsMatch(text ?? "", "^" + Regex.Escape(pattern ?? "*").Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
    }
}
