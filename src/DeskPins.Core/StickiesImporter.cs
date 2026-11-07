using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DeskPins.Core
{
    public sealed class StickiesImportResult
    {
        public readonly List<Note> Notes = new List<Note>();
        public readonly Catalog Catalog = new Catalog();
        public int EncryptedSkipped;
    }
    public static class StickiesImporter
    {
        public static StickiesImportResult Read(string file)
        {
            var result = new StickiesImportResult(); using (var db = new Sqlite(file, true))
            {
                var tables = new HashSet<string>(db.Query("SELECT name FROM sqlite_master WHERE type='table';").Select(r => (string)r[0]), StringComparer.OrdinalIgnoreCase); if (!tables.Contains("stickies") || !tables.Contains("stickiesproperties")) throw new InvalidDataException("不是受支持的 Stickies v10 数据库。");
                var properties = db.Query("SELECT [key],[type],t_b,t_i,t_d,t_s FROM stickiesproperties;").GroupBy(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.ToDictionary(r => Convert.ToInt32(r[1]), r => r[2] ?? r[3] ?? r[4] ?? r[5]));
                var bodies = new Dictionary<string, byte[]>(); var searchable = new Dictionary<string, string>();
                if (tables.Contains("content_rtf")) foreach (var r in db.Query("SELECT [key],rtf,search FROM content_rtf;")) { string key = Convert.ToString(r[0], CultureInfo.InvariantCulture); bodies[key] = r[1] as byte[] ?? System.Text.Encoding.ASCII.GetBytes(r[1] as string ?? ""); searchable[key] = r[2] as string ?? ""; }
                if (tables.Contains("content_png")) foreach (var r in db.Query("SELECT [key],png FROM content_png;")) bodies[Convert.ToString(r[0], CultureInfo.InvariantCulture)] = r[1] as byte[] ?? new byte[0];
                var folderMap = new Dictionary<string, string>(); if (tables.Contains("categories")) { var rows = db.Query("SELECT [key],name,parent FROM categories;"); foreach (var r in rows) { string key = Convert.ToString(r[0]); var f = new Folder { Name = r[1] as string ?? "导入文件夹" }; folderMap[key] = f.Id; result.Catalog.Folders.Add(f); } for (int i = 0; i < rows.Count; i++) { string parent = Convert.ToString(rows[i][2]); if (folderMap.ContainsKey(parent)) result.Catalog.Folders[i].ParentId = folderMap[parent]; } }
                var stackMap = new Dictionary<string, string>(); if (tables.Contains("stacks")) { var stackProps = tables.Contains("stackproperties") ? db.Query("SELECT [key],[type],t_b,t_i,t_d,t_s FROM stackproperties;").GroupBy(r => Convert.ToString(r[0])).ToDictionary(g => g.Key, g => g.ToDictionary(r => Convert.ToInt32(r[1]), r => r[2] ?? r[3] ?? r[4] ?? r[5])) : new Dictionary<string, Dictionary<int, object>>(); foreach (var r in db.Query("SELECT [key],title,colour,open FROM stacks;")) { string key = Convert.ToString(r[0]); Dictionary<int, object> p; if (!stackProps.TryGetValue(key, out p)) p = new Dictionary<int, object>(); var s = new NoteStack { Name = r[1] as string ?? "导入堆叠", Color = Color(r[2]), Expanded = Num(r[3]) != 0, Left = Num(Value(p, 1, 100)), Top = Num(Value(p, 2, 100)), Topmost = Num(Value(p, 8, 0)) != 0 }; stackMap[key] = s.Id; result.Catalog.Stacks.Add(s); } }
                foreach (var r in db.Query("SELECT [key],noteclass,flags,title,colour,category FROM stickies;"))
                {
                    string key = Convert.ToString(r[0], CultureInfo.InvariantCulture); int flags = (int)Num(r[2]); if ((flags & 4) != 0) { result.EncryptedSkipped++; continue; }
                    Dictionary<int, object> p; if (!properties.TryGetValue(key, out p)) p = new Dictionary<int, object>(); byte[] body; string plain; int cls = (int)Num(r[1]);
                    var n = new Note { Title = r[3] as string ?? "", Color = Color(r[4]), Kind = (flags & 1) != 0 ? NoteKind.Image : NoteKind.Text, Body = bodies.TryGetValue(key, out body) ? body : new byte[0], PlainText = searchable.TryGetValue(key, out plain) ? plain : "", State = cls == 5 ? NoteState.Closed : cls == 6 ? NoteState.Stored : cls == 3 ? NoteState.Sleeping : NoteState.Desktop };
                    n.Left = Num(Value(p, 18, 140)); n.Top = Num(Value(p, 19, 140)); n.Width = Math.Max(100, Num(Value(p, 23, 260))); double height = Num(Value(p, 24, 190)); n.Height = n.ExpandedHeight = height < 24 ? 190 : height; n.Topmost = Num(Value(p, 1, 0)) != 0; n.Locked = Num(Value(p, 4, 0)) != 0; n.Rolled = Num(Value(p, 9, 0)) != 0; n.Scrollbar = Num(Value(p, 22, 0)) != 0; n.Ghost = Num(Value(p, 37, 0)) != 0; n.Opacity = Math.Max(.1, Math.Min(1, Num(Value(p, 10, 255)) / 255)); n.Source = Convert.ToString(Value(p, 11, "")); n.CreatedUtc = Date(Value(p, 20, null)) ?? DateTime.UtcNow; n.ModifiedUtc = Date(Value(p, 21, null)) ?? n.CreatedUtc; n.AlarmUtc = Date(Value(p, 50, null));
                    string category = Convert.ToString(r[5]); if (folderMap.ContainsKey(category)) n.FolderId = folderMap[category]; string stack = Convert.ToString(Value(p, 39, "")); if (stackMap.ContainsKey(stack)) n.StackId = stackMap[stack];
                    if (cls == 2) n.Attachment = new Attachment { TitlePattern = Convert.ToString(Value(p, 43, "*")), ClassPattern = Convert.ToString(Value(p, 44, "*")), Relative = Num(Value(p, 47, 0)) == 1, OffsetX = Num(Value(p, 48, 0)), OffsetY = Num(Value(p, 49, 0)) };
                    if (cls == 3) { n.WakeUtc = Date(Value(p, 28, null)) ?? DateTime.UtcNow; int wakeFlags = (int)Num(Value(p, 29, 2)); n.WakeSound = (wakeFlags & 1) != 0; n.WakeTopmost = (wakeFlags & 2) != 0; n.WakeAlarm = (wakeFlags & 4) != 0; if (Num(Value(p, 27, 0)) != 0) { n.State = NoteState.Recurring; int type = (int)Num(Value(p, 30, 1)), day = (int)Num(Value(p, 33, 1)); n.Repeat = new RepeatRule { Kind = type == 1 ? RepeatKind.Daily : type == 2 ? RepeatKind.Weekly : type == 3 ? RepeatKind.MonthlyDay : type == 4 ? RepeatKind.Yearly : type == 5 ? RepeatKind.MonthlyPosition : RepeatKind.Interval, StartLocal = (Date(Value(p, 55, null)) ?? n.WakeUtc.Value).ToLocalTime(), EndLocal = Date(Value(p, 34, null))?.ToLocalTime(), Day = day % 100, Month = type == 4 ? day / 100 : 1, WeekDays = day, WeekDay = day % 100, Position = day / 100, Interval = Math.Max(1, (int)Num(Value(p, 32, 1))), IntervalUnit = Math.Max(0, Math.Min(3, day - 1)) }; } }
                    result.Notes.Add(n);
                }
                if (tables.Contains("friends")) { var groupNames = new List<string>(); foreach (var r in db.Query("SELECT [level],[type],name,address,email FROM friends ORDER BY [order];")) { int level = (int)Num(r[0]), type = (int)Num(r[1]); while (groupNames.Count > level) groupNames.RemoveAt(groupNames.Count - 1); if (type == 2) { groupNames.Add(r[2] as string ?? ""); continue; } result.Catalog.Friends.Add(new Friend { Name = r[2] as string ?? "", Address = r[3] as string ?? "", Email = r[4] as string ?? "", Group = string.Join("/", groupNames), Separator = type == 1 }); } }
            }
            return result;
        }
        private static object Value(Dictionary<int, object> p, int key, object fallback) { object result; return p.TryGetValue(key, out result) ? result : fallback; }
        private static double Num(object value) { double result; return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0; }
        private static DateTime? Date(object value) { if (value == null) return null; DateTime date; if (DateTime.TryParse(Convert.ToString(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date)) return date; double seconds = Num(value); try { return seconds <= 0 ? (DateTime?)null : DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime; } catch { return null; } }
        private static string Color(object value) { int color = (int)Num(value); return "#" + (color & 255).ToString("X2") + ((color >> 8) & 255).ToString("X2") + ((color >> 16) & 255).ToString("X2"); }
    }
}
