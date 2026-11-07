using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DeskPins.Core
{
    public enum NoteKind { Text, Image }
    public enum NoteState { Desktop, Closed, Stored, Sleeping, Recurring }
    public enum RepeatKind { Daily, Weekly, MonthlyDay, MonthlyPosition, Yearly, Interval }
    [DataContract]
    public sealed class Note
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Title = "";
        [DataMember] public string Preview = "";
        [DataMember] public NoteKind Kind;
        [DataMember] public NoteState State;
        [DataMember] public DateTime CreatedUtc = DateTime.UtcNow;
        [DataMember] public DateTime ModifiedUtc = DateTime.UtcNow;
        [DataMember] public DateTime StateUtc = DateTime.UtcNow;
        [DataMember] public double Left = 140, Top = 140, Width = 260, Height = 190, ExpandedHeight = 190;
        [DataMember] public string Color = "#FFFFB4", Foreground = "#000000", FontName = "Verdana";
        [DataMember] public double FontSize = 13.333, Opacity = 1;
        [DataMember] public bool Topmost, Rolled, Locked, Ghost, Secret, Scrollbar, Stand, Encrypted;
        [DataMember] public string FolderId = "", StackId = "", RecurrenceSourceId = "", Source = "", SkinPath = "";
        [DataMember] public int SourcePort = 52673;
        [DataMember] public List<string> Tags = new List<string>();
        [DataMember] public DateTime? AlarmUtc, WakeUtc;
        [DataMember] public bool WakeTopmost = true, WakeAlarm, WakeSound;
        [DataMember] public RepeatRule Repeat;
        [DataMember] public Attachment Attachment;
        [IgnoreDataMember] public byte[] Body;
        [IgnoreDataMember] public string PlainText;
        // Encrypted describes the persisted row; this flag only describes the loaded body.
        [IgnoreDataMember] public bool ContentDecrypted;
        public string DisplayTitle { get { return string.IsNullOrWhiteSpace(Title) ? (string.IsNullOrWhiteSpace(Preview) ? (Kind == NoteKind.Image ? "图片便签" : "新便签") : Preview) : Title; } }
        [IgnoreDataMember] public bool CompactPreview;
        public string DisplayPreview { get { return CompactPreview ? "" : Preview; } }
        [IgnoreDataMember] public bool DisplayModifiedDates;
        public string DateLabel { get { var date = DisplayModifiedDates ? ModifiedUtc : State == NoteState.Sleeping || State == NoteState.Recurring ? WakeUtc ?? StateUtc : State == NoteState.Stored || State == NoteState.Closed ? StateUtc : CreatedUtc; return date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }
        public Note Clone()
        {
            var clone = Json.Read<Note>(Json.Bytes(this));
            clone.Body = Body == null ? null : (byte[])Body.Clone(); clone.PlainText = PlainText; clone.ContentDecrypted = ContentDecrypted;
            clone.Id = Guid.NewGuid().ToString("N"); clone.CreatedUtc = clone.ModifiedUtc = clone.StateUtc = DateTime.UtcNow;
            return clone;
        }
    }
    [DataContract]
    public sealed class RepeatRule
    {
        [DataMember] public RepeatKind Kind;
        [DataMember] public DateTime StartLocal = DateTime.Now;
        [DataMember] public DateTime? EndLocal;
        [DataMember] public int WeekDays = 127, Day = 1, Month = 1, Position = 1, WeekDay = 1;
        [DataMember] public int Interval = 1, IntervalUnit = 2;
    }
    [DataContract]
    public sealed class Attachment
    {
        [DataMember] public string TitlePattern = "*", ClassPattern = "*";
        [DataMember] public bool Relative;
        [DataMember] public double OffsetX, OffsetY;
    }
    [DataContract]
    public sealed class Folder
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N"), Name = "新文件夹", ParentId = "";
        [DataMember] public bool Encrypted;
    }
    [DataContract]
    public sealed class NoteStack
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N"), Name = "新堆叠", Color = "#FFFFB4";
        [DataMember] public double Left = 100, Top = 100;
        [DataMember] public bool Expanded, Topmost;
    }
    [DataContract]
    public sealed class Tag
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N"), Name = "新标签", Color = "#527DA5";
    }
    [DataContract]
    public sealed class Friend
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N"), Name = "", Address = "", Email = "", Group = "";
        [DataMember] public int Favorite;
        [DataMember] public bool Separator;
    }
    [DataContract]
    public sealed class NoteStyle
    {
        [DataMember] public string Name = "默认", Color = "#FFFFB4", Foreground = "#000000", FontName = "Verdana";
        [DataMember] public double FontSize = 13.333, Opacity = 1;
        [DataMember] public bool Topmost;
        public void Apply(Note n) { n.Color = Color; n.Foreground = Foreground; n.FontName = FontName; n.FontSize = FontSize; n.Opacity = Opacity; n.Topmost = Topmost; }
    }
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public bool Snap = true, OpaqueEditing = true, DoubleClickRoll = true, PreventDuplicates = true;
        [DataMember] public bool AlarmFlash = true, AlarmJiggle, AlarmLoop, Networking, UseDns, EmailEnabled, AutoStart, CompactList;
        [DataMember] public int SnoozeMinutes = 5, NetworkPort = 52673, SmtpPort = 587, BackupHours = 24, ClosedRetentionDays = 0;
        [DataMember] public bool SmtpSsl = true;
        [DataMember] public string AlarmSound = "", WakeSound = "", SmtpHost = "", SmtpUser = "", SmtpSecret = "", SmtpFrom = "";
        [DataMember] public string GlobalNewKey = "", GlobalToggleKey = "", TransferFile = "", SkinPath = "", FriendsServer = "", Language = "zh-CN";
        [DataMember] public string PasswordVerifier = "";
        [DataMember] public int TomorrowHour;
        [DataMember] public DateTime LastBackupUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        [DataMember] public NoteKind DefaultKind;
        [DataMember] public List<NoteStyle> Styles = new List<NoteStyle>();
        [DataMember] public Dictionary<string, string> Shortcuts = new Dictionary<string, string>();
        [DataMember] public bool ManagerModifiedDates;
        [DataMember] public bool EmailViaClient;
        [DataMember] public Dictionary<int, int> ManagerOrders = new Dictionary<int, int>();
        public void Normalize()
        {
            if (LastBackupUtc == DateTime.MinValue) LastBackupUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            if (Styles == null) Styles = new List<NoteStyle>();
            while (Styles.Count < 10) Styles.Add(new NoteStyle { Name = Styles.Count == 0 ? "默认" : "样式 " + Styles.Count });
            if (Shortcuts == null) Shortcuts = new Dictionary<string, string>();
            if (ManagerOrders == null) ManagerOrders = new Dictionary<int, int>();
            NetworkPort = Math.Max(1, Math.Min(65535, NetworkPort)); SnoozeMinutes = Math.Max(1, SnoozeMinutes);
            TomorrowHour = Math.Max(0, Math.Min(23, TomorrowHour));
        }
    }
    [DataContract]
    public sealed class Catalog
    {
        [DataMember] public Settings Settings = new Settings();
        [DataMember] public List<Folder> Folders = new List<Folder>();
        [DataMember] public List<NoteStack> Stacks = new List<NoteStack>();
        [DataMember] public List<Tag> Tags = new List<Tag>();
        [DataMember] public List<Friend> Friends = new List<Friend>();
        public void Normalize() { if (Settings == null) Settings = new Settings(); Settings.Normalize(); if (Folders == null) Folders = new List<Folder>(); if (Stacks == null) Stacks = new List<NoteStack>(); if (Tags == null) Tags = new List<Tag>(); if (Friends == null) Friends = new List<Friend>(); }
    }
    public static class Json
    {
        public static byte[] Bytes<T>(T value) { using (var s = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(s, value); return s.ToArray(); } }
        public static string Text<T>(T value) { return Encoding.UTF8.GetString(Bytes(value)); }
        public static T Read<T>(byte[] bytes) { using (var s = new MemoryStream(bytes)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s); }
        public static T Read<T>(string text) { return Read<T>(Encoding.UTF8.GetBytes(text)); }
    }
}
