using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskPins.Core;

namespace DeskPins.App
{
    [DataContract]
    public sealed class Skin
    {
        [DataMember] public string TitleColor = "#527DA5", TitleTextColor = "#FFFFFF", BorderColor = "#666666";
        [DataMember] public List<SkinButton> Buttons = new List<SkinButton>();
    }
    [DataContract] public sealed class SkinButton { [DataMember] public string Text = "", Action = ""; }
    public sealed class AppHost : IDisposable
    {
        public readonly NoteLibrary Library;
        public readonly Dictionary<string, NoteWindow> Windows = new Dictionary<string, NoteWindow>();
        public readonly HashSet<string> Selected = new HashSet<string>();
        private readonly Dictionary<string, StackWindow> stacks = new Dictionary<string, StackWindow>();
        private readonly Dictionary<string, Skin> skins = new Dictionary<string, Skin>();
        private readonly bool testMode;
        private readonly HwndSource source;
        private System.Windows.Forms.NotifyIcon tray;
        private Timer timer;
        private ManagerWindow manager;
        private SettingsWindow settingsWindow;
        private string appliedLanguage;
        private bool syncPending, hidden, disposed, startupRestoring, revealingNotes;
        private string solo = "";
        private TcpListener listener;
        private SoundPlayer sound;
        private Native.WinEventProc winEvent;
        private IntPtr winHook;
        private readonly HashSet<string> attachedVisible = new HashSet<string>();
        private readonly DispatcherTimer attachmentDebounce;
        public bool Exiting { get; private set; }
        public string SoloId { get { return solo; } }
        public string DataDirectory { get { return Path.GetFullPath(Path.GetDirectoryName(Library.Repository.FilePath)); } }
        public readonly ApiService Api;
        public long WindowCreationMilliseconds;
        public static readonly Dictionary<string, string> DefaultShortcuts = new Dictionary<string, string>
        {
            {"paste","Ctrl+V"},{"copy","Ctrl+C"},{"cut","Ctrl+X"},{"cycle","Ctrl+Tab"},{"cycle-back","Ctrl+Shift+Tab"},{"new","Ctrl+N"},{"new-color","Ctrl+Shift+N"},{"close","Ctrl+D"},{"delete","Ctrl+Shift+D"},{"top","Ctrl+T"},{"title","Ctrl+Shift+T"},{"lock","Ctrl+Shift+L"},{"roll","Alt+R"},
            {"alarm","Ctrl+Shift+A"},{"sleep","Ctrl+L"},{"repeat","Ctrl+Shift+O"},{"tomorrow","Ctrl+G"},{"manage","Ctrl+M"},{"settings","Ctrl+O"},{"friends","Ctrl+Shift+F"},
            {"save","Ctrl+S"},{"save-all","Ctrl+Shift+S"},{"print","Ctrl+P"},{"find","Ctrl+F"},{"find-next","F3"},{"paste-plain","Ctrl+Shift+V"},{"copy-plain","Ctrl+Shift+C"},
            {"convert-image","Ctrl+Shift+K"},{"bold","Ctrl+B"},{"italic","Ctrl+I"},{"underline","Ctrl+U"},{"strike","Ctrl+H"},{"bullets","Ctrl+Shift+B"},{"checkbox","Alt+C"},{"link","Ctrl+Shift+H"},{"date","Ctrl+K"},{"rule","Ctrl+Enter"},{"undo","Ctrl+Z"},{"redo","Ctrl+Shift+Z"},{"size","Ctrl+Shift+X"},{"width","Ctrl+W"},{"skin","Alt+S"},
            {"transfer","Ctrl+E"},{"source","Ctrl+Shift+E"},{"log","Ctrl+Shift+G"},{"add-friend","Ctrl+J"},{"send-many","Ctrl+Shift+M"},{"reply","Ctrl+R"},{"reply-secret","Ctrl+Shift+R"},{"quote","Ctrl+Shift+U"},{"width-left","Ctrl+Shift+W"},{"sync-friends","Ctrl+Y"},{"edit-friends","Ctrl+Shift+Y"},{"format-copy","Alt+F"},{"format-paste","Alt+V"},{"skin-reset","Alt+Shift+S"}
        };
        public static readonly string[] ShortcutOrder =
        {
            "paste", "copy", "cut", "cycle", "cycle-back", "new", "new-color", "close", "delete", "top", "title", "lock", "roll",
            "alarm", "sleep", "repeat", "tomorrow", "manage", "settings", "friends", "save", "save-all", "print", "find", "find-next",
            "paste-plain", "copy-plain", "convert-image", "bold", "italic", "underline", "strike", "bullets", "checkbox", "link", "date",
            "rule", "undo", "redo", "size", "width", "skin", "transfer", "source", "log", "add-friend", "send-many", "reply",
            "reply-secret", "quote", "width-left", "sync-friends", "edit-friends", "format-copy", "format-paste", "skin-reset"
        };
        public AppHost(string dataDirectory, bool testMode = false)
        {
            this.testMode = testMode; Directory.CreateDirectory(dataDirectory); Library = new NoteLibrary(Path.Combine(dataDirectory, "deskpins.db")); Ui.LoadLanguage(Library.Catalog.Settings.Language); appliedLanguage = Ui.CurrentLanguage;
            source = new HwndSource(new HwndSourceParameters("DeskPinsMain") { Width = 0, Height = 0, WindowStyle = 0 }); source.AddHook(Messages);
            Api = new ApiService(this, dataDirectory);
            attachmentDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) }; attachmentDebounce.Tick += (s, e) => { attachmentDebounce.Stop(); UpdateAttachments(); };
            Library.Changed += QueueSync; Library.PreparingChange += FlushNote; Library.Alarm += n => { Sync(); NoteWindow w; if (Windows.TryGetValue(n.Id, out w)) w.StartAlarm(); };
            Library.Woke += n => PlayWakeSound();
            if (!testMode) { BuildTray(); Api.Start(); ApplySettings(); Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged; Schedule(); }
        }
        public void Start() { startupRestoring = true; var s = Library.Catalog.Settings; hidden = s.StartHidden; UpdateAttachments(); if (Library.Notes.Count == 0) { if (s.StartHidden) Library.Create(); else New(); } Sync(); Library.Tick(DateTime.UtcNow); }
        private IntPtr Messages(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x312) { int id = w.ToInt32(); if (id == 1) New(); else if (id == 2) ToggleAll(); else if (id == 3) Manage(); handled = true; }
            if (msg == 0x4A) { try { var cds = (CopyData)System.Runtime.InteropServices.Marshal.PtrToStructure(l, typeof(CopyData)); if (cds.Size > 0 && cds.Size < Transfer.MaxPacket) { var bytes = new byte[cds.Size]; System.Runtime.InteropServices.Marshal.Copy(cds.Data, bytes, 0, bytes.Length); string command = Encoding.UTF8.GetString(bytes).TrimEnd('\0'); if (command == "api do register" || command == "do register") Api.Register(w); if (command == "api do deregister" || command == "do deregister") Api.Deregister(w); var response = Api.Execute(command); Api.ReplyCopyData(w, cds.Tag, response); if (response == "000 exiting") Application.Current.Dispatcher.BeginInvoke(new Action(Exit)); handled = true; return new IntPtr(1); } } catch (Exception ex) { Log(ex.Message); } }
            return IntPtr.Zero;
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct CopyData { public IntPtr Tag; public int Size; public IntPtr Data; }
        public void QueueSync() { if (syncPending || Exiting) return; syncPending = true; Application.Current.Dispatcher.BeginInvoke(new Action(() => { syncPending = false; Sync(); if (!testMode) Schedule(); }), DispatcherPriority.Background); }
        public void Sync()
        {
            if (Exiting) return; int created = 0; bool remaining = false;
            foreach (var n in Library.Notes.Values.ToArray())
            {
                bool visible = n.State == NoteState.Desktop && !hidden && (solo == "" || solo == n.Id) && (n.Attachment == null || attachedVisible.Contains(n.Id)) && (n.StackId == "" || Library.Catalog.Stacks.Any(s => s.Id == n.StackId && s.Expanded));
                NoteWindow window; if (visible) { if (!Windows.TryGetValue(n.Id, out window)) { if (startupRestoring && created >= 1) { remaining = true; continue; } created++; var measure = Stopwatch.StartNew(); window = new NoteWindow(this, n); WindowCreationMilliseconds += measure.ElapsedMilliseconds; Windows.Add(n.Id, window); if (!testMode) window.Show(); } else window.RefreshAppearance(); }
                else if (Windows.TryGetValue(n.Id, out window)) { Windows.Remove(n.Id); window.CloseInternal(n.State == NoteState.Desktop); }
            }
            foreach (var id in Windows.Keys.Where(id => !Library.Notes.ContainsKey(id)).ToArray()) { var w = Windows[id]; Windows.Remove(id); w.CloseInternal(false); }
            foreach (var s in Library.Catalog.Stacks) { StackWindow w; if (!stacks.TryGetValue(s.Id, out w)) { w = new StackWindow(this, s); stacks.Add(s.Id, w); if (!testMode && !hidden) w.Show(); } w.Refresh(); }
            foreach (var id in stacks.Keys.Where(id => !Library.Catalog.Stacks.Any(s => s.Id == id)).ToArray()) { stacks[id].Close(); stacks.Remove(id); }
            if (startupRestoring) { if (remaining) QueueSync(); else startupRestoring = false; }
            if (tray != null) tray.Text = hidden ? "DeskPins · " + Ui.Text("便签已隐藏") : "DeskPins · " + Library.Notes.Count + " " + Ui.Text("张便签");
        }
        public Note New(NoteKind? kind = null, bool paste = false) { hidden = false; solo = ""; var n = Library.Create(kind ?? Library.Catalog.Settings.DefaultKind); Sync(); Focus(n); if (paste) { NoteWindow w; if (Windows.TryGetValue(n.Id, out w)) Paste(w); } return n; }
        public void Focus(Note n) { hidden = false; solo = ""; if (n.State != NoteState.Desktop) Library.Move(n, NoteState.Desktop); var stack = Library.Catalog.Stacks.FirstOrDefault(s => s.Id == n.StackId); if (stack != null) stack.Expanded = true; Sync(); NoteWindow w; if (Windows.TryGetValue(n.Id, out w)) { w.Show(); Native.ShowWindow(new WindowInteropHelper(w).Handle, 5); w.Activate(); if (w.TextEditor != null) w.TextEditor.Focus(); else if (w.ImageEditor != null) w.ImageEditor.Focus(); } BringStacksForward(); }
        public void Cycle(Note n, int step) { var notes = Windows.Values.Select(w => w.Note).ToList(); int i = notes.FindIndex(x => x.Id == n.Id); if (notes.Count > 0) Focus(notes[(i + step + notes.Count) % notes.Count]); }
        public void ReloadWindow(Note n) { NoteWindow w; if (Windows.TryGetValue(n.Id, out w)) w.ReloadContent(); }
        public void ReloadPosition(Note n) { NoteWindow w; if (Windows.TryGetValue(n.Id, out w)) w.ApplyModel(); }
        public void CloseNotes(Note n) { if (Library.Catalog.Settings.ConfirmClose && !Ui.Confirm(Ui.Text("关闭便签？"))) return; foreach (var target in Targets(n)) Library.Move(target, NoteState.Closed); }
        public void DeleteNotes(Note n) { var targets = Targets(n).ToArray(); if (Ui.Confirm(string.Format(Ui.Text("永久删除 {0} 张便签？此操作无法恢复。"), targets.Length))) foreach (var target in targets) Library.Delete(target); }
        public IEnumerable<Note> Targets(Note n) { return Selected.Contains(n.Id) ? Selected.Where(Library.Notes.ContainsKey).Select(id => Library.Notes[id]).ToArray() : new[] { n }; }
        public void Select(Note n, bool touching)
        {
            Selected.Clear(); if (n != null) { Selected.Add(n.Id); if (touching) { bool changed; do { changed = false; foreach (var candidate in Library.Notes.Values.Where(x => x.State == NoteState.Desktop && x.Topmost == n.Topmost && !Selected.Contains(x.Id))) { var rect = new Rect(candidate.Left - 3, candidate.Top - 3, candidate.Width + 6, candidate.Height + 6); if (Selected.Select(id => Library.Notes[id]).Any(x => rect.IntersectsWith(new Rect(x.Left, x.Top, x.Width, x.Height)))) { Selected.Add(candidate.Id); changed = true; } } } while (changed); } }
            foreach (var w in Windows.Values) w.RefreshAppearance();
            if (n != null) BringStacksForward();
        }
        public void MoveSelected(NoteWindow active, double dx, double dy) { foreach (var id in Selected.Where(id => id != active.Note.Id).ToArray()) { NoteWindow w; if (Windows.TryGetValue(id, out w)) w.MoveBy(dx, dy); } }
        public void SnapAfterDrag(NoteWindow active, bool alt)
        {
            if (!(Library.Catalog.Settings.Snap ^ alt)) return;
            double x = active.Left, y = active.Top; double distance = Math.Max(0, Library.Catalog.Settings.SnapDistance), gap = Math.Max(0, Library.Catalog.Settings.SnapGap);
            var edges = Windows.Values.Where(v => v != active && !Selected.Contains(v.Note.Id)).Select(v => new Rect(v.Left, v.Top, v.Width, v.Height)).ToList();
            foreach (var r in edges)
            {
                foreach (var value in new[] { r.Left, r.Right, r.Left - active.Width, r.Right - active.Width, r.Right + gap, r.Left - active.Width - gap }) if (Math.Abs(x - value) < distance) x = value;
                foreach (var value in new[] { r.Top, r.Bottom, r.Top - active.Height, r.Bottom - active.Height, r.Bottom + gap, r.Top - active.Height - gap }) if (Math.Abs(y - value) < distance) y = value;
            }
            var workArea = SystemParameters.WorkArea;
            foreach (var value in new[] { workArea.Left, workArea.Right, workArea.Left - active.Width, workArea.Right - active.Width }) if (Math.Abs(x - value) < distance) x = value;
            foreach (var value in new[] { workArea.Top, workArea.Bottom, workArea.Top - active.Height, workArea.Bottom - active.Height }) if (Math.Abs(y - value) < distance) y = value;
            double dx = x - active.Left, dy = y - active.Top;
            if (Math.Abs(dx) < .01 && Math.Abs(dy) < .01) return;
            var targets = Selected.Where(Windows.ContainsKey).Select(id => Windows[id]).ToList();
            if (!targets.Contains(active)) targets.Add(active);
            foreach (var w in targets) w.MoveBy(dx, dy);
        }
        public void BringStacksForward()
        {
            foreach (var w in stacks.Values) w.BringForward();
        }
        public void ShowAllNotes()
        {
            if (revealingNotes) return;
            revealingNotes = true;
            try
            {
                hidden = false; solo = ""; Sync();
                foreach (var w in Windows.Values) Native.RaiseNoActivate(w);
                BringStacksForward();
            }
            finally { revealingNotes = false; }
        }
        public void SaveSelected() { foreach (var id in Selected.ToArray()) { Note n; if (Library.Notes.TryGetValue(id, out n)) Library.Save(n); } }
        public void SetColor(Note n) { var c = Ui.Color(n.Color); if (c == null) return; foreach (var target in Targets(n)) { target.Color = c; Library.Save(target); } foreach (var w in Windows.Values) w.RefreshAppearance(); }
        public void Solo(Note n) { FlushAll(); solo = solo == n.Id ? "" : n.Id; Sync(); }
        public void ToggleAll() { FlushAll(); hidden = !hidden; Sync(); foreach (var w in stacks.Values) { if (hidden) w.Hide(); else w.Show(); } }
        public void SetHidden(bool value) { if (hidden != value) ToggleAll(); }
        public bool Hidden { get { return hidden; } }
        public void Rescue() { foreach (var n in Library.Notes.Values.Where(n => n.State == NoteState.Desktop)) { FlushNote(n); var b = Native.Clamp(new Rect(n.Left, n.Top, n.Width, n.Height)); n.Left = b.Left; n.Top = b.Top; Library.Save(n); ReloadPosition(n); } }
        public void BringForward() { hidden = false; Sync(); foreach (var w in Windows.Values) { w.Show(); Native.ShowWindow(new WindowInteropHelper(w).Handle, 5); w.Activate(); } BringStacksForward(); }
        public void FlushNote(Note n) { NoteWindow w; if (Windows.TryGetValue(n.Id, out w)) w.Flush(); if (manager != null) manager.FlushPending(n); }
        public void FlushAll() { foreach (var w in Windows.Values.ToArray()) w.Flush(); if (manager != null) manager.FlushPending(); }
        public void Manage() { if (manager == null) { manager = new ManagerWindow(this); manager.Closed += (s, e) => manager = null; } manager.Show(); manager.Activate(); }
        public void Settings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsWindow(this);
                settingsWindow.Closed += (s, e) => settingsWindow = null;
                settingsWindow.Show();
            }
            else
            {
                if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
                settingsWindow.Activate();
            }
        }
        public void ApplyDataDirectory(string target, string pointerFile = null)
        {
            var full = Path.GetFullPath(target); var current = DataDirectory;
            if (DataLocation.Same(full, current)) return;
            if (full.TrimEnd(Path.DirectorySeparatorChar).StartsWith(current.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(Ui.Text("不能把数据目录设置到当前数据目录的子目录。"));
            Directory.CreateDirectory(full);
            var targetDatabase = Path.Combine(full, "deskpins.db");
            if (File.Exists(targetDatabase)) { using (new NoteRepository(targetDatabase)) { } }
            else { FlushAll(); Library.Repository.Backup(targetDatabase); }
            DataLocation.SetCustom(full, pointerFile);
        }
        public void Friends() { Collections("friends"); }
        public void Collections(string type) { new CollectionsWindow(this, type).ShowDialog(); }
        public void Tags(Note n) { AssignTags(Targets(n).ToArray()); }
        public void AssignTags(Note[] notes)
        {
            if (notes.Length == 0) return; var window = new Window { Title = Ui.Text("设置标签"), Width = 380, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false }; var panel = new DockPanel { Margin = new Thickness(12) }; var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); var list = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = list }); var checks = new Dictionary<string, CheckBox>(); Action refresh = () => { list.Children.Clear(); checks.Clear(); foreach (var t in Library.Catalog.Tags) { var c = new CheckBox { Content = t.Name, IsChecked = notes.All(n => n.Tags.Contains(t.Id)), Foreground = Ui.Brush(t.Color), Margin = new Thickness(4, 7, 4, 7) }; checks[t.Id] = c; list.Children.Add(c); } };
            Action<bool> apply = replace => { foreach (var n in notes) { if (replace) n.Tags.Clear(); foreach (var pair in checks.Where(p => p.Value.IsChecked == true)) if (!n.Tags.Contains(pair.Key)) n.Tags.Add(pair.Key); Library.Save(n); } window.Close(); }; buttons.Children.Add(Ui.Button("新增标签", () => { Collections("tags"); refresh(); })); buttons.Children.Add(Ui.Button("添加", () => apply(false))); buttons.Children.Add(Ui.Button("替换", () => apply(true))); buttons.Children.Add(Ui.Button("清空", () => { foreach (var n in notes) { n.Tags.Clear(); Library.Save(n); } window.Close(); })); refresh(); window.Content = panel; window.ShowDialog();
        }
        public void NewStack(Note n) { var name = Ui.Ask("新建堆叠", "名称", "新堆叠"); if (name == null) return; var stack = new NoteStack { Name = name, Left = n.Left, Top = n.Top, Topmost = n.Topmost }; Library.Catalog.Stacks.Add(stack); n.StackId = stack.Id; Library.Save(n); Library.SaveCatalog(); }
        public string FolderPath(Folder f) { var result = f.Name; var visited = new HashSet<string> { f.Id }; while (f.ParentId != "") { f = Library.Catalog.Folders.FirstOrDefault(x => x.Id == f.ParentId); if (f == null || !visited.Add(f.Id)) break; result = f.Name + " / " + result; } return result; }
        public void EnsureUnlocked(Folder f) { if (f.Encrypted && Library.SessionPassword == null) Unlock(); if (f.Encrypted && Library.SessionPassword == null) throw new OperationCanceledException("未解锁加密存储。"); }
        public void Unlock() { var password = Ui.Ask("解锁加密存储", Library.Catalog.Settings.PasswordVerifier == "" ? "设置密码（请妥善保管）" : "输入密码", "", true); if (password != null) Library.Unlock(password); }
        public void Paste(NoteWindow w)
        {
            if (w.Note.Locked) return;
            if (Clipboard.ContainsImage()) { var image = Clipboard.GetImage(); if (w.ImageEditor != null) w.ImageEditor.Replace(image); else if (string.IsNullOrWhiteSpace(new TextRange(w.TextEditor.Document.ContentStart, w.TextEditor.Document.ContentEnd).Text)) { w.Note.Kind = NoteKind.Image; var editor = new ImageEditor { MaxUndo = Library.Catalog.Settings.ImageUndoLevels, SaveFormat = Library.Catalog.Settings.ImageSaveType }; editor.Read(new byte[0]); editor.Replace(image); w.Note.Body = editor.Bytes(); Library.Save(w.Note); w.ReloadContent(); } else w.TextEditor.InsertImage(image); }
            else if (w.TextEditor != null) w.TextEditor.Paste(); w.QueueSave();
        }
        public bool IsImage(string path) { return new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        public void LoadImage(Note n, string file) { var bytes = File.ReadAllBytes(file); if (bytes.Length > Transfer.MaxPacket) throw new IOException("图片文件过大。"); var editor = new ImageEditor { MaxUndo = Library.Catalog.Settings.ImageUndoLevels, SaveFormat = Library.Catalog.Settings.ImageSaveType }; editor.Read(bytes); n.Kind = NoteKind.Image; n.Body = editor.Bytes(); n.PlainText = ""; Library.Save(n); }
        public void Export(Note n)
        {
            Library.Load(n); var file = Ui.SaveFile("DeskPins 便签|*.dpn|Stickies 传输文本|*.sti|RTF|*.rtf|纯文本|*.txt|PNG 图片|*.png", n.DisplayTitle.Replace('/', '_').Replace('\\', '_')); if (file == null) return; ExportTo(n, file);
        }
        public void ExportTo(Note n, string file) { FlushNote(n); Library.Load(n); var extension = Path.GetExtension(file).ToLowerInvariant(); byte[] bytes; if (extension == ".dpn") bytes = Json.Bytes(NotePackage.From(n)); else if (extension == ".sti") bytes = new UTF8Encoding(false).GetBytes(Transfer.EncodeSticky(n)); else if (extension == ".txt") bytes = new UTF8Encoding(false).GetBytes(n.PlainText ?? ""); else if (extension == ".rtf" && n.Kind == NoteKind.Text) bytes = n.Body; else if (extension == ".png" && n.Kind == NoteKind.Image) bytes = n.Body; else throw new InvalidDataException("内容类型与导出格式不匹配。"); AtomicFile.Write(file, bytes); }
        public void Capture() { var capture = new CaptureWindow(Library.Catalog.Settings.ScreenGrabUi); capture.ShowDialog(); if (capture.Result == null) return; var n = Library.Create(NoteKind.Image); var editor = new ImageEditor { MaxUndo = Library.Catalog.Settings.ImageUndoLevels, SaveFormat = Library.Catalog.Settings.ImageSaveType }; editor.Read(new byte[0]); editor.Replace(capture.Result); n.Body = editor.Bytes(); Library.Save(n); Focus(n); }
        public void AddTrayMenu(string label, Action action) { if (tray == null) return; var item = tray.ContextMenuStrip.Items.Add(label); item.Click += (s, e) => action(); }
        public void Import(string file)
        {
            Note n; string ext = Path.GetExtension(file).ToLowerInvariant(); if (ext == ".dpn") n = Json.Read<NotePackage>(File.ReadAllBytes(file)).Import(); else if (ext == ".sti") n = Transfer.DecodeSticky(File.ReadAllText(file)); else { n = new Note { Body = new byte[0], PlainText = "" }; if (IsImage(file)) { n.Kind = NoteKind.Image; var img = new ImageEditor { MaxUndo = Library.Catalog.Settings.ImageUndoLevels, SaveFormat = Library.Catalog.Settings.ImageSaveType }; img.Read(File.ReadAllBytes(file)); n.Body = img.Bytes(); } else if (ext == ".rtf") { n.Body = File.ReadAllBytes(file); var e = new TextEditor(); e.Read(n); e.Write(n); } else n.PlainText = File.ReadAllText(file); n.Title = Path.GetFileNameWithoutExtension(file); }
            Library.Add(n); Focus(n);
        }
        public void TransferFile(Note n, bool move)
        {
            string file = Library.Catalog.Settings.TransferFile; if (string.IsNullOrWhiteSpace(file)) { file = Ui.SaveFile("DeskPins 传输文件|*.dpx", "transfer.dpx"); if (file == null) return; Library.Catalog.Settings.TransferFile = file; Library.SaveCatalog(); }
            Library.Load(n);
            using (var transferLock = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { byte[] data = File.Exists(file) ? File.ReadAllBytes(file) : new byte[0]; var notes = data.Length == 0 ? new List<NotePackage>() : Json.Read<List<NotePackage>>(data); notes.Add(NotePackage.From(n)); AtomicFile.Write(file, Json.Bytes(notes)); }
            if (move) Library.Move(n, NoteState.Closed);
        }
        public void ImportTransfer()
        {
            string file = Library.Catalog.Settings.TransferFile; if (!File.Exists(file)) throw new FileNotFoundException("传输文件不存在。");
            using (var transferLock = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var data = File.ReadAllBytes(file); if (data.Length == 0) return; var packages = Json.Read<List<NotePackage>>(data); var notes = packages.Select(p => p.Import()).ToArray();
                FlushAll(); Library.Atomic(() => { for (int i = 0; i < packages.Count; i++) { string id = packages[i].PackageId; if (string.IsNullOrEmpty(id)) { using (var hash = SHA256.Create()) id = BitConverter.ToString(hash.ComputeHash(Json.Bytes(packages[i]))) + ":" + i; } if (Library.Repository.ReceiveOnce(id)) Library.Add(notes[i]); } });
                AtomicFile.Write(file, new byte[0]);
            }
            Sync();
        }
        public void ImportStickies() { var file = Ui.OpenFile("Stickies v10 数据库|*.db"); if (file == null) return; var result = StickiesImporter.Read(file); FlushAll(); Library.Atomic(() => { Library.Catalog.Folders.AddRange(result.Catalog.Folders); Library.Catalog.Stacks.AddRange(result.Catalog.Stacks); Library.Catalog.Friends.AddRange(result.Catalog.Friends); foreach (var n in result.Notes) { if (n.Kind == NoteKind.Text) { var editor = new TextEditor(); editor.Read(n); editor.Write(n); } Library.Add(n); } Library.SaveCatalog(); }); UpdateAttachments(); Sync(); MessageBox.Show(string.Format(Ui.Text("导入 {0} 张便签。"), result.Notes.Count) + (result.EncryptedSkipped > 0 ? " " + string.Format(Ui.Text("原版加密便签 {0} 张未导入，请在原版解密后再导入。"), result.EncryptedSkipped) : ""), "DeskPins"); }
        public void Backup() { FlushAll(); var file = Ui.SaveFile("DeskPins 数据库备份|*.dpbackup", "DeskPins-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".dpbackup"); if (file != null) Library.Repository.Backup(file); }
        public bool Restore(bool merge)
        {
            var file = Ui.OpenFile("DeskPins 数据库 / 备份|*.dpbackup;*.db"); if (file == null) return false; if (!merge && !Ui.Confirm("用备份替换当前便签？将先保存当前数据备份。")) return false; FlushAll(); Library.Repository.Backup(Path.Combine(Path.GetDirectoryName(Library.Repository.FilePath), "before-restore-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".db")); Library.Repository.ImportBackup(file, merge); foreach (var w in Windows.Values.ToArray()) w.CloseInternal(false); Windows.Clear(); Library.Reload(); UpdateAttachments(); Sync(); ApplySettings(); return true;
        }
        public void Print(NoteWindow w) { w.Flush(); var d = new PrintDialog(); if (d.ShowDialog() != true) return; if (w.TextEditor != null) { var clone = new FlowDocument(); var range = new TextRange(clone.ContentStart, clone.ContentEnd); using (var stream = new MemoryStream(w.Note.Body)) range.Load(stream, DataFormats.Rtf); clone.PageWidth = d.PrintableAreaWidth; clone.PageHeight = d.PrintableAreaHeight; clone.PagePadding = new Thickness(30); d.PrintDocument(((IDocumentPaginatorSource)clone).DocumentPaginator, "DeskPins"); } else d.PrintVisual(w.ImageEditor, "DeskPins 图片便签"); }
        public async void Send(Note n, Friend friend)
        {
            try { Library.Load(n); if (friend.Address != "") { if (!Library.Catalog.Settings.Networking) throw new InvalidOperationException("请在设置中启用网络功能。"); await Transfer.SendAsync(n, friend.Address, Library.Catalog.Settings.NetworkPort); } else if (friend.Email != "") { if (Library.Catalog.Settings.EmailViaClient) { ComposeMail(n, friend.Email); Log("已打开邮件草稿。"); return; } await SendMail(n, friend.Email); } else throw new InvalidOperationException("联系人没有地址。"); Log("便签发送成功。"); } catch (Exception ex) { Ui.Error(ex); }
        }
        public void SendCustom(Note n) { var address = Ui.Ask("发送便签", "多个地址用分号分隔（主机:端口或邮箱）"); if (address == null) return; foreach (var item in address.Split(';').Where(s => !string.IsNullOrWhiteSpace(s))) Send(n, new Friend { Address = item.Contains("@") ? "" : item.Trim(), Email = item.Contains("@") ? item.Trim() : "" }); }
        private async Task SendMail(Note n, string destination)
        {
            var s = Library.Catalog.Settings; if (!s.EmailEnabled) throw new InvalidOperationException("请在设置中启用 SMTP 发送。");
            using (var message = new MailMessage(s.SmtpFrom, destination) { Subject = n.DisplayTitle, Body = n.PlainText ?? "图片便签", BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 }) using (var smtp = new SmtpClient(s.SmtpHost, s.SmtpPort) { EnableSsl = s.SmtpSsl })
            { if (s.SmtpUser != "") smtp.Credentials = new NetworkCredential(s.SmtpUser, UnprotectSecret(s.SmtpSecret)); if (n.Body != null && n.Body.Length > 0) message.Attachments.Add(new System.Net.Mail.Attachment(new MemoryStream(n.Body), n.Kind == NoteKind.Image ? "note.png" : "note.rtf")); await smtp.SendMailAsync(message); }
        }
        public void ComposeMail(Note n, string destination) { FlushNote(n); Library.Load(n); var address = new MailAddress(destination).Address; var body = n.PlainText ?? ""; if (body.Length > 30000) throw new InvalidOperationException("邮件客户端草稿正文过长，请使用 SMTP 附件发送。"); var uri = "mailto:" + Uri.EscapeDataString(address) + "?subject=" + Uri.EscapeDataString(n.DisplayTitle) + "&body=" + Uri.EscapeDataString(body); Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        public async void SyncFriends() { try { string source = Library.Catalog.Settings.FriendsServer; if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("请配置联系人列表文件或 URL。"); string text; if (source.StartsWith("https://") || source.StartsWith("http://")) { using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) }) text = await client.GetStringAsync(source); } else text = File.ReadAllText(source); var friends = Json.Read<List<Friend>>(text); Library.Catalog.Friends = friends; Library.SaveCatalog(); } catch (Exception ex) { Ui.Error(ex); } }
        public string ProtectSecret(string secret) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)); }
        private string UnprotectSecret(string secret) { return string.IsNullOrEmpty(secret) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser)); }
        public void PlayAlarm() { StopSound(); if (!Library.Catalog.Settings.AlarmSoundEnabled) return; var path = Library.Catalog.Settings.AlarmSound; if (File.Exists(path)) { sound = new SoundPlayer(path); try { if (Library.Catalog.Settings.AlarmLoop) sound.PlayLooping(); else sound.Play(); } catch (Exception ex) { Log(ex.Message); } } else SystemSounds.Exclamation.Play(); }
        public void StopSound() { if (sound != null) { sound.Stop(); sound.Dispose(); sound = null; } }
        public void PlayWakeSound() { var path = Library.Catalog.Settings.WakeSound; if (File.Exists(path)) Task.Run(() => { try { using (var wake = new SoundPlayer(path)) wake.PlaySync(); } catch (Exception ex) { Log(ex.Message); } }); else if (!testMode) SystemSounds.Asterisk.Play(); }
        public bool IsAttachmentVisible(Note n) { return n.Attachment != null && attachedVisible.Contains(n.Id); }
        public void EditFriendsSource() { var file = Library.Catalog.Settings.FriendsServer; if (!File.Exists(file)) throw new FileNotFoundException("联系人来源不是本地文件。"); Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        public void OpenLog() { var file = Path.Combine(Path.GetDirectoryName(Library.Repository.FilePath), "deskpins.log"); if (!File.Exists(file)) AtomicFile.Write(file, new byte[0]); Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        public void AddFriend(Note n) { FlushNote(n); Library.Load(n); var name = Ui.Ask("新联系人", "名称", n.PlainText ?? n.DisplayTitle); if (name == null) return; var address = Ui.Ask("新联系人", "地址", n.Source); if (address == null) return; Library.Catalog.Friends.Add(new Friend { Name = name, Address = address }); Library.SaveCatalog(); Friends(); }
        public void EditSource(Note n) { var source = Ui.Ask("编辑发送者", "地址", n.Source); if (source != null) { n.Source = source; Library.Save(n); } }
        public void Reply(Note n, bool secret) { FlushNote(n); Library.Load(n); if (string.IsNullOrWhiteSpace(n.Source)) throw new InvalidOperationException("这张便签没有发送者。"); var copy = n.Clone(); copy.Secret = secret || n.Secret; Send(copy, new Friend { Address = n.Source.Contains(":") || n.SourcePort == 0 || n.SourcePort == 52673 ? n.Source : n.Source + ":" + n.SourcePort }); }
        private void Schedule()
        {
            if (disposed || testMode) return; var due = Library.NextDue(); long delay = due.HasValue ? Math.Max(50, (long)(due.Value - DateTime.UtcNow).TotalMilliseconds) : int.MaxValue; delay = Math.Min(int.MaxValue, delay); if (Library.Catalog.Settings.BackupHours > 0) delay = Math.Min(delay, Math.Max(1000, (long)(Library.Catalog.Settings.LastBackupUtc.AddHours(Library.Catalog.Settings.BackupHours) - DateTime.UtcNow).TotalMilliseconds));
            if (timer == null) timer = new Timer(o => Application.Current.Dispatcher.BeginInvoke(new Action(Tick))); timer.Change((int)delay, Timeout.Infinite);
        }
        private void Tick() { if (disposed) return; try { Library.Tick(DateTime.UtcNow); var s = Library.Catalog.Settings; if (s.BackupHours > 0 && DateTime.UtcNow - s.LastBackupUtc > TimeSpan.FromHours(s.BackupHours)) { FlushAll(); string dir = Path.Combine(Path.GetDirectoryName(Library.Repository.FilePath), "backups"); Directory.CreateDirectory(dir); Library.Repository.Backup(Path.Combine(dir, "DeskPins-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db")); s.LastBackupUtc = DateTime.UtcNow; Library.SaveCatalog(); } if (s.ClosedRetentionDays > 0) foreach (var n in Library.Notes.Values.Where(n => n.State == NoteState.Closed && DateTime.UtcNow - n.StateUtc > TimeSpan.FromDays(s.ClosedRetentionDays)).ToArray()) Library.Delete(n); Schedule(); } catch (Exception ex) { Log(ex.Message); } }
        private void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Resume) Application.Current.Dispatcher.BeginInvoke(new Action(Tick)); }
        public void ApplySettings()
        {
            if (testMode) return;
            var s = Library.Catalog.Settings;
            bool languageChanged = !string.Equals(appliedLanguage, s.Language ?? "", StringComparison.OrdinalIgnoreCase);
            Ui.LoadLanguage(s.Language); appliedLanguage = Ui.CurrentLanguage;
            Native.UnregisterHotKey(source.Handle, 1); Native.UnregisterHotKey(source.Handle, 2); Native.UnregisterHotKey(source.Handle, 3);
            if (!Native.RegisterHotkey(source.Handle, 1, s.GlobalNewKey) || !Native.RegisterHotkey(source.Handle, 2, s.GlobalToggleKey) || !Native.RegisterHotkey(source.Handle, 3, s.GlobalManageKey)) Ui.Error(new InvalidOperationException(Ui.Text("全局快捷键被其他应用占用，请在设置中修改。")));
            if (listener != null) { listener.Stop(); listener = null; }
            if (s.Networking) { try { listener = new TcpListener(IPAddress.Any, s.NetworkPort); listener.Start(); var current = listener; Task.Run(() => ReceiveLoop(current)); } catch (Exception ex) { Ui.Error(ex); } }
            if (winHook != IntPtr.Zero) { Native.UnhookWinEvent(winHook); winHook = IntPtr.Zero; }
            if (Library.Notes.Values.Any(n => n.Attachment != null)) EnsureAttachmentHook();
            if (languageChanged) RefreshLanguageSurfaces(); else { foreach (var w in Windows.Values) w.RefreshAppearance(); foreach (var w in stacks.Values) w.Refresh(); }
        }
        private void RefreshLanguageSurfaces()
        {
            FlushAll();
            var managerWasOpen = manager != null;
            if (managerWasOpen) { var oldManager = manager; manager = null; oldManager.Close(); }
            foreach (var w in stacks.Values.ToArray()) w.Close();
            stacks.Clear();
            RebuildTray();
            foreach (var w in Windows.Values) w.RefreshAppearance();
            if (managerWasOpen) Manage();
            Sync();
        }
        private void RebuildTray() { if (tray == null) return; tray.Visible = false; tray.Dispose(); tray = null; BuildTray(); }
        public void SetAutoStart(bool enabled) { string startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DeskPins.lnk"); if (enabled) { Type type = Type.GetTypeFromProgID("WScript.Shell"); dynamic shell = Activator.CreateInstance(type); dynamic shortcut = shell.CreateShortcut(startup); shortcut.TargetPath = Process.GetCurrentProcess().MainModule.FileName; shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory; shortcut.Save(); } else if (File.Exists(startup)) File.Delete(startup); }
        public void ValidateSettings(Settings s)
        {
            if (s.NetworkPort < 1 || s.NetworkPort > 65535 || s.SmtpPort < 1 || s.SmtpPort > 65535) throw new ArgumentException(Ui.Text("端口需在 1–65535 范围内。"));
            if (s.TomorrowHour < 0 || s.TomorrowHour > 23 || s.SnoozeMinutes < 1) throw new ArgumentException(Ui.Text("明天唤醒小时需为 0–23，稍后提醒分钟数需大于零。"));
            var gestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in DefaultShortcuts.Keys) { string custom; string key = s.Shortcuts.TryGetValue(action, out custom) ? custom : DefaultShortcuts[action]; if (string.IsNullOrWhiteSpace(key)) continue; Key parsedKey; ModifierKeys parsedMods; if (!Ui.TryParseGesture(key, out parsedKey, out parsedMods)) throw new ArgumentException(Ui.Text("快捷键格式无效。") + key); var signature = parsedKey + ":" + parsedMods; if (!gestures.Add(signature)) throw new ArgumentException(Ui.Text("便签快捷键重复：") + key); }
            gestures.Clear(); foreach (var key in new[] { s.GlobalNewKey, s.GlobalToggleKey, s.GlobalManageKey }.Where(k => !string.IsNullOrWhiteSpace(k))) { Key parsedKey; ModifierKeys parsedMods; if (!Ui.TryParseGesture(key, out parsedKey, out parsedMods)) throw new ArgumentException(Ui.Text("快捷键格式无效。") + key); if (parsedMods == ModifierKeys.None && !(parsedKey >= Key.F1 && parsedKey <= Key.F24)) throw new ArgumentException(Ui.Text("全局快捷键必须包含修饰键或功能键。")); var signature = parsedKey + ":" + parsedMods; if (!gestures.Add(signature)) throw new ArgumentException(Ui.Text("全局快捷键不能重复。")); }
            Ui.ValidateLanguage(s.Language);
            foreach (var style in s.Styles) { System.Windows.Media.ColorConverter.ConvertFromString(style.Color); System.Windows.Media.ColorConverter.ConvertFromString(style.Foreground); if (string.IsNullOrWhiteSpace(style.FontName)) throw new ArgumentException(Ui.Text("字体名称不能为空。")); }
            if (s.SkinPath != "") ValidateSkin(s.SkinPath);
        }
        public string ShortcutFor(string action) { string custom; return Library.Catalog.Settings.Shortcuts.TryGetValue(action, out custom) ? custom : DefaultShortcuts[action]; }
        public string ActionFor(Key key, ModifierKeys mods) { foreach (var pair in DefaultShortcuts) { string text = ShortcutFor(pair.Key); if (string.IsNullOrWhiteSpace(text)) continue; Key parsedKey; ModifierKeys parsedMods; if (Ui.TryParseGesture(text, out parsedKey, out parsedMods) && parsedKey == key && parsedMods == mods) return pair.Key; } if (key == Key.F2 && mods == ModifierKeys.None && !string.IsNullOrWhiteSpace(ShortcutFor("title"))) return "title"; return null; }
        public void UpdateAttachments()
        {
            if (disposed) return; var attached = Library.Notes.Values.Where(n => n.Attachment != null && n.State == NoteState.Desktop).ToArray(); attachedVisible.Clear(); if (attached.Length > 0) { var windows = Native.Windows(); foreach (var n in attached) { var window = windows.FirstOrDefault(w => !OwnsWindow(w.Handle) && WindowMatch.Wildcard(w.Title, n.Attachment.TitlePattern) && WindowMatch.Wildcard(w.Class, n.Attachment.ClassPattern)); if (window != null) { attachedVisible.Add(n.Id); if (n.Attachment.Relative) { n.Left = window.Bounds.Left + n.Attachment.OffsetX; n.Top = window.Bounds.Top + n.Attachment.OffsetY; ReloadPosition(n); } } } if (!testMode) EnsureAttachmentHook(); }
            Sync();
        }
        private void EnsureAttachmentHook() { if (winHook != IntPtr.Zero) return; winEvent = (h, ev, w, obj, child, thread, time) => { if (obj != 0 || w == IntPtr.Zero || disposed || OwnsWindow(w)) return; Application.Current.Dispatcher.BeginInvoke(new Action(() => { attachmentDebounce.Stop(); attachmentDebounce.Start(); })); }; winHook = Native.SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, winEvent, 0, 0, 2); }
        public bool OwnsWindow(IntPtr h) { return h == source.Handle || Windows.Values.Any(w => new WindowInteropHelper(w).Handle == h) || stacks.Values.Any(w => new WindowInteropHelper(w).Handle == h) || (manager != null && new WindowInteropHelper(manager).Handle == h) || (settingsWindow != null && new WindowInteropHelper(settingsWindow).Handle == h); }
        private async Task ReceiveLoop(TcpListener current)
        {
            while (!disposed && listener == current) { TcpClient client; try { client = await current.AcceptTcpClientAsync(); } catch (ObjectDisposedException) { break; } catch (SocketException) { break; } _ = Task.Run(() => { using (client) { try { client.ReceiveTimeout = 5000; var text = Transfer.ReadFrame(client.GetStream()); if (text.StartsWith("#getfriends")) { var data = Transfer.Frame(Json.Text(Library.Catalog.Friends)); client.GetStream().Write(data, 0, data.Length); return; } var n = Transfer.DecodeSticky(text); var sourceAddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address; n.Source = sourceAddress.ToString(); if (Library.Catalog.Settings.UseDns) { try { n.Source = Dns.GetHostEntry(sourceAddress).HostName; } catch (SocketException) { } } Application.Current.Dispatcher.BeginInvoke(new Action(() => { Library.Add(n); BringForward(); })); } catch (Exception ex) { Log(ex.Message); } } }); }
        }
        public Skin GetSkin(string file) { if (string.IsNullOrWhiteSpace(file)) file = Library.Catalog.Settings.SkinPath; if (string.IsNullOrWhiteSpace(file)) return null; Skin skin; if (skins.TryGetValue(file, out skin)) return skin; try { skin = Json.Read<Skin>(File.ReadAllText(file)); skins[file] = skin; return skin; } catch (Exception ex) { Log(ex.Message); return null; } }
        public void ValidateSkin(string file) { var skin = Json.Read<Skin>(File.ReadAllText(file)); new BrushConverter().ConvertFromString(skin.TitleColor); new BrushConverter().ConvertFromString(skin.TitleTextColor); new BrushConverter().ConvertFromString(skin.BorderColor); skins[file] = skin; }
        public void AddExtensionMenus(ContextMenu menu, Note n) { Api.AddMenus(menu, n); }
        private static System.Drawing.Icon LoadIcon() { using (var stream = typeof(AppHost).Assembly.GetManifestResourceStream("DeskPins.Icon.ico")) return new System.Drawing.Icon(stream); }
        private void BuildTray()
        {
            tray = new System.Windows.Forms.NotifyIcon { Text = hidden ? "DeskPins · " + Ui.Text("便签已隐藏") : "DeskPins · " + Library.Notes.Count + " " + Ui.Text("张便签"), Icon = LoadIcon(), Visible = true }; tray.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) BringForward(); }; tray.MouseDoubleClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) New(); }; var menu = new System.Windows.Forms.ContextMenuStrip();
            Action<string, Action> add = (text, action) => { var item = menu.Items.Add(Ui.Text(text)); item.Click += (s, e) => { try { action(); } catch (Exception ex) { Ui.Error(ex); } }; }; add("新建便签", () => New()); add("从剪贴板新建", () => New(null, true)); add("新建图片便签", () => New(NoteKind.Image)); add("从屏幕截取新建…", Capture); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); add("显示 / 隐藏全部", ToggleAll); add("前置所有便签", BringForward); add("管理便签…", Manage); add("联系人…", Friends); add("设置…", Settings); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); add("关于 DeskPins", () => new AboutWindow().ShowDialog()); add("退出", Exit); tray.ContextMenuStrip = menu;
        }
        public void Log(string message) { try { lock (logLock) File.AppendAllText(Path.Combine(Path.GetDirectoryName(Library.Repository.FilePath), "deskpins.log"), DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); } catch (IOException) { } }
        private readonly object logLock = new object();
        public void Exit() { if (Exiting) return; FlushAll(); Library.SaveCatalog(); Exiting = true; Dispose(); Application.Current.Shutdown(); }
        public void Dispose() { if (disposed) return; disposed = true; Exiting = true; if (timer != null) timer.Dispose(); attachmentDebounce.Stop(); if (winHook != IntPtr.Zero) Native.UnhookWinEvent(winHook); Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged; if (listener != null) listener.Stop(); StopSound(); Api.Dispose(); if (manager != null) manager.Close(); if (settingsWindow != null) { settingsWindow.Close(); settingsWindow = null; } foreach (var w in Windows.Values.ToArray()) w.CloseInternal(); Windows.Clear(); foreach (var w in stacks.Values) w.Close(); stacks.Clear(); Native.UnregisterHotKey(source.Handle, 1); Native.UnregisterHotKey(source.Handle, 2); Native.UnregisterHotKey(source.Handle, 3); source.Dispose(); if (tray != null) { tray.Visible = false; tray.Dispose(); } Library.Dispose(); }
    }
}
