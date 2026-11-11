using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeskPins.Core
{
    public sealed class NoteLibrary : IDisposable
    {
        public readonly NoteRepository Repository;
        public readonly Dictionary<string, Note> Notes = new Dictionary<string, Note>();
        public Catalog Catalog { get; private set; }
        public string SessionPassword { get; private set; }
        public event Action Changed;
        public event Action<Note> PreparingChange;
        public event Action<Note> Alarm;
        public event Action<Note> Woke;
        private bool inTransaction;
        public NoteLibrary(string path) { Repository = new NoteRepository(path); try { Reload(); } catch { Repository.Dispose(); throw; } }
        public void Reload() { Catalog = Repository.LoadCatalog(); SessionPassword = null; Notes.Clear(); foreach (var n in Repository.LoadMetadata()) Notes.Add(n.Id, n); Notify(); }
        public Note Create(NoteKind kind = NoteKind.Text) { var n = new Note { Kind = kind, Body = new byte[0], PlainText = "" }; Catalog.Settings.Styles[0].Apply(n); n.WakeTopmost = Catalog.Settings.WakeTopmostMode == "off" ? false : Catalog.Settings.WakeTopmostMode == "on" ? true : n.Topmost; n.WakeAlarm = Catalog.Settings.WakeAlarm; n.WakeSound = Catalog.Settings.WakeSoundEnabled; n.Left += Notes.Count % 8 * 22; n.Top += Notes.Count % 8 * 22; Notes.Add(n.Id, n); Save(n); return n; }
        public void Add(Note n) { if (Notes.ContainsKey(n.Id)) n.Id = Guid.NewGuid().ToString("N"); Notes.Add(n.Id, n); Save(n); }
        public Note Copy(Note n) { Load(n); var c = n.Clone(); c.Left += 20; c.Top += 20; c.State = NoteState.Desktop; c.StackId = ""; c.Attachment = null; c.Repeat = null; c.WakeUtc = null; c.AlarmUtc = null; c.Encrypted = false; Notes.Add(c.Id, c); Save(c); return c; }
        public void Load(Note n)
        {
            Repository.LoadContent(n);
            if (n.Encrypted && !n.ContentDecrypted) { if (SessionPassword == null) throw new InvalidOperationException("请先解锁加密存储。"); n.Body = Crypto.Unprotect(n.Body, SessionPassword); n.ContentDecrypted = true; }
        }
        public void Save(Note n)
        {
            bool encrypted = Catalog.Folders.Any(f => f.Id == n.FolderId && f.Encrypted) && n.State == NoteState.Stored;
            if (n.Body == null && encrypted == n.Encrypted)
            {
                n.ModifiedUtc = DateTime.UtcNow; if (encrypted) n.Preview = "加密便签"; Repository.Save(n); Notify(); return;
            }
            Load(n);
            var body = n.Body; var text = n.PlainText; bool wasEncrypted = n.Encrypted, wasDecrypted = n.ContentDecrypted;
            string preview = n.Preview; DateTime modified = n.ModifiedUtc;
            try
            {
                n.ModifiedUtc = DateTime.UtcNow; n.Encrypted = encrypted;
                n.Preview = encrypted ? "加密便签" : n.Kind == NoteKind.Image ? "图片便签" : (n.PlainText ?? n.Preview ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (n.Preview.Length > 100) n.Preview = n.Preview.Substring(0, 100);
                if (encrypted) { if (SessionPassword == null) throw new InvalidOperationException("请先解锁加密存储。"); n.Body = Crypto.Protect(body, SessionPassword); }
                Repository.Save(n);
                n.Body = encrypted ? null : body; n.PlainText = encrypted ? null : text; n.ContentDecrypted = false;
            }
            catch { n.Body = body; n.PlainText = text; n.Encrypted = wasEncrypted; n.ContentDecrypted = wasDecrypted; n.Preview = preview; n.ModifiedUtc = modified; throw; }
            Notify();
        }
        public void SaveCatalog() { Catalog.Normalize(); Repository.SaveCatalog(Catalog); Notify(); }
        public void Atomic(Action action)
        {
            if (inTransaction) { action(); return; }
            var catalog = Json.Read<Catalog>(Json.Bytes(Catalog));
            var snapshots = Notes.Values.Select(n => { var copy = Json.Read<Note>(Json.Bytes(n)); copy.Body = n.Body; copy.PlainText = n.PlainText; copy.ContentDecrypted = n.ContentDecrypted; return copy; }).ToArray();
            var originals = new Dictionary<string, Note>(Notes);
            inTransaction = true;
            try { Repository.Transaction(action); }
            catch
            {
                Catalog = catalog; Notes.Clear();
                foreach (var snapshot in snapshots) { var original = originals[snapshot.Id]; foreach (var field in typeof(Note).GetFields()) field.SetValue(original, field.GetValue(snapshot)); Notes.Add(original.Id, original); }
                throw;
            }
            finally { inTransaction = false; Notify(); }
        }
        public void SetFolderEncrypted(Folder folder, bool encrypted)
        {
            if (!Catalog.Folders.Contains(folder)) throw new ArgumentException("文件夹不存在。");
            if (SessionPassword == null) throw new InvalidOperationException("请先解锁加密存储。");
            var affected = Notes.Values.Where(n => n.FolderId == folder.Id && n.State == NoteState.Stored).ToArray();
            foreach (var n in affected) Prepare(n);
            var snapshots = affected.Select(n => { var copy = Json.Read<Note>(Json.Bytes(n)); copy.Body = n.Body; copy.PlainText = n.PlainText; copy.ContentDecrypted = n.ContentDecrypted; return copy; }).ToArray();
            bool previous = folder.Encrypted;
            try { Atomic(() => { foreach (var n in affected) Load(n); folder.Encrypted = encrypted; foreach (var n in affected) Save(n); SaveCatalog(); }); }
            catch { folder.Encrypted = previous; for (int i = 0; i < affected.Length; i++) foreach (var field in typeof(Note).GetFields()) field.SetValue(affected[i], field.GetValue(snapshots[i])); throw; }
        }
        public void Move(Note n, NoteState state, string folder = "")
        {
            Prepare(n);
            var previous = Json.Read<Note>(Json.Bytes(n)); previous.Body = n.Body; previous.PlainText = n.PlainText; previous.ContentDecrypted = n.ContentDecrypted;
            try
            {
                Load(n); n.State = state; n.StateUtc = DateTime.UtcNow; n.FolderId = folder;
                if (state != NoteState.Desktop) { n.StackId = ""; n.Attachment = null; }
                if (state == NoteState.Closed) { n.AlarmUtc = null; n.WakeUtc = null; n.Repeat = null; }
                if (state == NoteState.Desktop) { n.Repeat = null; n.WakeUtc = null; }
                Save(n);
            }
            catch { foreach (var field in typeof(Note).GetFields()) field.SetValue(n, field.GetValue(previous)); throw; }
        }
        public void Delete(Note n) { Repository.Delete(n.Id); Notes.Remove(n.Id); Notify(); }
        public void Sleep(Note n, DateTime wakeUtc, RepeatRule repeat = null)
        {
            Prepare(n); Load(n);
            n.State = repeat == null ? NoteState.Sleeping : NoteState.Recurring; n.StateUtc = DateTime.UtcNow;
            n.StackId = ""; n.Attachment = null; n.WakeUtc = wakeUtc; n.Repeat = repeat; if (repeat != null) n.AlarmUtc = null; Save(n);
        }
        public void Tick(DateTime nowUtc)
        {
            foreach (var n in Notes.Values.ToArray())
            {
                if (n.WakeUtc.HasValue && n.WakeUtc <= nowUtc && (n.State == NoteState.Sleeping || n.State == NoteState.Recurring))
                {
                    if (n.State == NoteState.Sleeping) { n.State = NoteState.Desktop; n.WakeUtc = null; n.Topmost = n.WakeTopmost; Save(n); Wake(n); }
                    else
                    {
                        Repository.LoadContent(n);
                        bool duplicate = Catalog.Settings.PreventDuplicates && n.Kind == NoteKind.Text && Notes.Values.Any(x => { if (x.State != NoteState.Desktop || x.Kind != NoteKind.Text || x.Left != n.Left || x.Top != n.Top || x.Preview != n.Preview) return false; Load(x); return x.PlainText == n.PlainText && x.Body.SequenceEqual(n.Body); });
                        if (!duplicate) { Load(n); var clone = n.Clone(); clone.State = NoteState.Desktop; clone.RecurrenceSourceId = n.Id; clone.Repeat = null; clone.WakeUtc = null; clone.Topmost = n.WakeTopmost; Notes.Add(clone.Id, clone); Save(clone); Wake(clone); }
                        var next = RepeatSchedule.Next(n.Repeat, nowUtc.ToLocalTime());
                        if (next == null) Delete(n); else { n.WakeUtc = RepeatSchedule.ToUtc(next.Value); Save(n); }
                    }
                }
                if (Notes.ContainsKey(n.Id) && n.AlarmUtc.HasValue && n.AlarmUtc <= nowUtc && n.State == NoteState.Desktop)
                {
                    n.AlarmUtc = null; if (n.State == NoteState.Sleeping) { n.State = NoteState.Desktop; n.WakeUtc = null; }
                    var stack = Catalog.Stacks.FirstOrDefault(s => s.Id == n.StackId); if (stack != null) { stack.Expanded = true; SaveCatalog(); }
                    Save(n); RaiseAlarm(n);
                }
            }
        }
        public DateTime? NextDue() { var dates = Notes.Values.Select(n => n.State == NoteState.Desktop ? n.AlarmUtc : n.State == NoteState.Sleeping || n.State == NoteState.Recurring ? n.WakeUtc : null).Where(d => d.HasValue).Select(d => d.Value).ToList(); return dates.Count == 0 ? (DateTime?)null : dates.Min(); }
        public void Unlock(string password)
        {
            if (string.IsNullOrEmpty(Catalog.Settings.PasswordVerifier)) { Catalog.Settings.PasswordVerifier = Convert.ToBase64String(Crypto.Protect(Encoding.UTF8.GetBytes("DeskPins password"), password)); }
            else if (Encoding.UTF8.GetString(Crypto.Unprotect(Convert.FromBase64String(Catalog.Settings.PasswordVerifier), password)) != "DeskPins password") throw new InvalidOperationException("密码错误。");
            SessionPassword = password; SaveCatalog();
        }
        public void Lock() { foreach (var n in Notes.Values.Where(x => x.Encrypted)) { n.Body = null; n.PlainText = null; n.ContentDecrypted = false; } SessionPassword = null; Notify(); }
        public IEnumerable<Note> Search(string term, IEnumerable<string> tags = null, bool allTags = true)
        {
            var ids = new HashSet<string>(Repository.Search(term)); var t = (tags ?? new string[0]).ToArray();
            return Notes.Values.Where(n => ids.Contains(n.Id) && (t.Length == 0 || (allTags ? t.All(id => n.Tags.Contains(id)) : t.Any(id => n.Tags.Contains(id)))));
        }
        public void Notify() { if (inTransaction) return; var changed = Changed; if (changed != null) changed(); }
        private void Prepare(Note n) { var prepare = PreparingChange; if (prepare != null) prepare(n); }
        private void RaiseAlarm(Note n) { var alarm = Alarm; if (alarm != null) alarm(n); }
        private void Wake(Note n) { if (n.WakeSound) { var woke = Woke; if (woke != null) woke(n); } if (n.WakeAlarm) RaiseAlarm(n); }
        public void Dispose() { Repository.Dispose(); }
    }
}
