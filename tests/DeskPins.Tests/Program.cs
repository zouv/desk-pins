using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using DeskPins.Core;
using DeskPins.App;

namespace DeskPins.Tests
{
    internal static class Program
    {
        private static int checks;
        private static string auditDirectory;
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
        private static void Throws(Action action, string name) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, name); }
        [STAThread]
        private static int Main(string[] args)
        {
            auditDirectory = DeskPins.App.Program.Option(args, "--audit-dir");
            if (args.Length >= 2 && args[0] == "--seed") { string target = args[1]; Directory.CreateDirectory(target); using (var lib = new NoteLibrary(Path.Combine(target, "deskpins.db"))) { for (int i = 0; i < 10; i++) { var n = lib.Create(); n.Title = "Sample " + i; n.PlainText = "Sample desktop note " + i; n.Body = Encoding.ASCII.GetBytes(@"{\rtf1 Sample desktop note}"); lib.Save(n); } } return 0; }
            string dir = Path.Combine(Path.GetTempPath(), "DeskPins-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                Schedules(); Encryption(); Storage(dir); EncryptedEdits(dir); OriginalImport(dir); Packets(); ResizeGeometry(); if (args.Contains("--ui")) UiTests(dir);
                Console.WriteLine(checks + " checks passed."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
        private static void Schedules()
        {
            var rule = new RepeatRule { Kind = RepeatKind.Daily, StartLocal = new DateTime(2026, 1, 1, 9, 0, 0) }; Check(RepeatSchedule.Next(rule, new DateTime(2026, 1, 1, 9, 0, 0)) == new DateTime(2026, 1, 2, 9, 0, 0), "daily next strictly after");
            rule.Kind = RepeatKind.Weekly; rule.WeekDays = 1; Check(RepeatSchedule.Next(rule, new DateTime(2026, 1, 6, 10, 0, 0)).Value.DayOfWeek == DayOfWeek.Monday, "weekly selected day"); rule.WeekDays = 0; Throws(() => RepeatSchedule.Next(rule, new DateTime(2026, 1, 6)), "empty weekdays rejected");
            rule.Kind = RepeatKind.MonthlyDay; rule.Day = 31; Check(RepeatSchedule.Next(rule, new DateTime(2026, 2, 1)).Value == new DateTime(2026, 2, 28, 9, 0, 0), "monthly day clamps February"); Check(RepeatSchedule.Next(rule, new DateTime(2028, 2, 1)).Value.Day == 29, "leap February");
            rule.Kind = RepeatKind.MonthlyPosition; rule.WeekDay = (int)DayOfWeek.Friday; rule.Position = -1; Check(RepeatSchedule.Next(rule, new DateTime(2026, 2, 1)).Value == new DateTime(2026, 2, 27, 9, 0, 0), "last weekday of month");
            rule.Kind = RepeatKind.Yearly; rule.Month = 2; rule.Day = 29; Check(RepeatSchedule.Next(rule, new DateTime(2027, 1, 1)).Value.Day == 28, "yearly leap clamp");
            rule.Kind = RepeatKind.Interval; rule.Interval = 30; rule.IntervalUnit = 0; Check(RepeatSchedule.Next(rule, new DateTime(2026, 1, 1, 10, 0, 0)) == new DateTime(2026, 1, 1, 10, 30, 0), "interval skip missed times"); rule.EndLocal = new DateTime(2026, 1, 1, 10, 15, 0); Check(RepeatSchedule.Next(rule, new DateTime(2026, 1, 1, 10, 0, 0)) == null, "recurrence end date");
            Check(WindowMatch.Wildcard("Document - Editor", "* - Editor"), "wildcard title"); Check(!WindowMatch.Wildcard("Document - Browser", "* - Editor"), "wildcard negative");
            rule = new RepeatRule { Kind = RepeatKind.Weekly, StartLocal = new DateTime(2027, 1, 1), WeekDays = 0 }; Throws(() => RepeatSchedule.Next(rule, new DateTime(2026, 1, 1)), "empty weekdays rejected before start");
            rule = new RepeatRule { Kind = RepeatKind.Daily, StartLocal = DateTime.MinValue }; Check(RepeatSchedule.Next(rule, DateTime.MaxValue) == null, "date overflow terminates recurrence");
            rule = new RepeatRule { Kind = RepeatKind.Weekly, StartLocal = new DateTime(2027, 1, 3, 9, 0, 0), WeekDays = 1 }; Check(RepeatSchedule.Next(rule, new DateTime(2027, 1, 1)) == new DateTime(2027, 1, 4, 9, 0, 0), "future recurrence start respects selected weekday");
            rule.Kind = RepeatKind.MonthlyDay; rule.Day = 15; Check(RepeatSchedule.Next(rule, new DateTime(2027, 1, 1)) == new DateTime(2027, 1, 15, 9, 0, 0), "future recurrence start respects monthly day");
        }
        private static void Encryption()
        {
            byte[] data = Encoding.UTF8.GetBytes("机密便签 · 😀"); var encrypted = Crypto.Protect(data, "correct horse"); Check(Crypto.Unprotect(encrypted, "correct horse").SequenceEqual(data), "authenticated AES round trip"); Check(!encrypted.SequenceEqual(Crypto.Protect(data, "correct horse")), "random salt and IV"); Throws(() => Crypto.Unprotect(encrypted, "wrong"), "wrong password rejected"); encrypted[40] ^= 1; Throws(() => Crypto.Unprotect(encrypted, "correct horse"), "tamper rejected");
        }
        private static void Storage(string dir)
        {
            string file = Path.Combine(dir, "store.db"), backup = Path.Combine(dir, "backup.db"); string id;
            using (var lib = new NoteLibrary(file))
            {
                var n = lib.Create(); id = n.Id; n.Title = "测试便签"; n.Body = Encoding.ASCII.GetBytes(@"{\rtf1 hello}"); n.PlainText = "Unicode 中文_100%"; lib.Save(n); Check(lib.Search("中文_100%").Count() == 1, "Unicode and escaped LIKE search"); Check(lib.Search("' OR 1=1 --").Count() == 0, "parameterized search"); Check(!lib.Search("StateUtc").Any(), "search excludes metadata property names"); Check(lib.Search(id.Substring(0, 8)).Any(x => x.Id == id), "search matches a note id prefix");
                lib.Move(n, NoteState.Closed); Check(lib.Notes[id].State == NoteState.Closed, "close is recoverable"); lib.Move(n, NoteState.Desktop); Check(lib.Notes[id].Body.Length > 0, "restore retains content");
                lib.Repository.Backup(backup); n.PlainText = "changed"; lib.Save(n); lib.Repository.ImportBackup(backup, false); lib.Reload(); lib.Load(lib.Notes[id]); Check(lib.Notes[id].PlainText.Contains("中文"), "transactional restore original content");
                int count = lib.Notes.Count; Throws(() => lib.Repository.Transaction(() => { lib.Repository.Delete(id); throw new IOException("fault injection"); }), "transaction failure"); Check(lib.Repository.LoadMetadata().Count == count, "transaction rollback");
                n = lib.Notes[id]; lib.Sleep(n, DateTime.UtcNow.AddSeconds(-1)); lib.Tick(DateTime.UtcNow); Check(n.State == NoteState.Desktop && n.WakeUtc == null, "missed sleep wakes on start");
                var recurring = lib.Create(); recurring.PlainText = "recurring"; lib.Save(recurring); lib.Sleep(recurring, DateTime.UtcNow.AddHours(-4), new RepeatRule { Kind = RepeatKind.Interval, StartLocal = DateTime.Now.AddHours(-4), Interval = 1, IntervalUnit = 1 }); lib.Tick(DateTime.UtcNow); Check(lib.Notes.Values.Count(x => x.RecurrenceSourceId == recurring.Id) == 1, "one recurrence after missed times"); recurring.WakeUtc = DateTime.UtcNow.AddSeconds(-1); lib.Tick(DateTime.UtcNow); Check(lib.Notes.Values.Count(x => x.RecurrenceSourceId == recurring.Id) == 1, "prevent recurrence duplicates");
                var alarm = lib.Create(); int alarmCount = 0; lib.Alarm += a => alarmCount++; alarm.AlarmUtc = DateTime.UtcNow.AddSeconds(-1); lib.Save(alarm); lib.Tick(DateTime.UtcNow); lib.Tick(DateTime.UtcNow); Check(alarmCount == 1, "alarm delivered once");
                lib.Sleep(alarm, DateTime.UtcNow.AddDays(1)); alarm.AlarmUtc = DateTime.UtcNow.AddSeconds(-1); lib.Save(alarm); lib.Tick(DateTime.UtcNow); Check(alarmCount == 1 && lib.NextDue() > DateTime.UtcNow, "dormant alarm does not fire or spin timer");
                int woke = 0; lib.Woke += x => woke++; var waking = lib.Create(); waking.WakeSound = true; waking.WakeAlarm = false; lib.Sleep(waking, DateTime.UtcNow.AddSeconds(-1)); lib.Tick(DateTime.UtcNow); Check(woke == 1 && alarmCount == 1, "wake sound is independent of alarm");
                int initial = lib.Notes.Count; var originalCatalog = lib.Catalog.Folders.Count; int notifications = 0; lib.Changed += () => notifications++;
                Throws(() => lib.Atomic(() => { lib.Create(); lib.Catalog.Folders.Add(new Folder()); lib.SaveCatalog(); throw new IOException("simulated failed import"); }), "atomic library import failure");
                Check(lib.Notes.Count == initial && lib.Repository.LoadMetadata().Count == initial && lib.Catalog.Folders.Count == originalCatalog, "atomic rollback restores database and in-memory library");
                Check(notifications == 1, "transaction publishes one consistent notification");
                Throws(() => lib.Atomic(() => { lib.Repository.ReceiveOnce("retry-test"); throw new IOException(); }), "transfer receipt rolls back with note"); Check(lib.Repository.ReceiveOnce("retry-test") && !lib.Repository.ReceiveOnce("retry-test"), "transfer retry receipt is idempotent");
                var folder = new Folder { Name = "Encrypted", Encrypted = true }; lib.Catalog.Folders.Add(folder); lib.Unlock("password"); var secure = lib.Create(); secure.Body = Encoding.UTF8.GetBytes("secret bytes"); secure.PlainText = "hidden body"; lib.Save(secure); lib.Move(secure, NoteState.Stored, folder.Id); Check(secure.Encrypted && secure.Body == null, "encrypted note unloaded"); lib.Lock(); Throws(() => lib.Load(secure), "locked content rejected"); lib.Unlock("password"); lib.Load(secure); Check(Encoding.UTF8.GetString(secure.Body) == "secret bytes", "encrypted store round trip"); lib.Lock(); Check(!lib.Search("hidden body").Any(), "encrypted body excluded from search");
                lib.Delete(lib.Notes[id]); Check(!lib.Notes.ContainsKey(id), "delete removes metadata");
            }
            using (var lib = new NoteLibrary(file)) { Check(!lib.Notes.ContainsKey(id), "deleted note does not return"); Check(lib.Notes.Values.All(n => n.Body == null), "lazy content at startup"); }
            string foreign = Path.Combine(dir, "foreign.db"); using (var db = new Sqlite(foreign)) db.Execute("CREATE TABLE unrelated(value TEXT); INSERT INTO unrelated VALUES('keep');"); var original = File.ReadAllBytes(foreign); Throws(() => { using (var repo = new NoteRepository(foreign)) { } }, "foreign database rejected"); Check(File.ReadAllBytes(foreign).SequenceEqual(original), "foreign database remains unchanged");
        }
        private static void OriginalImport(string dir)
        {
            var file = Path.Combine(dir, "stickies.db"); using (var db = new Sqlite(file)) { db.Execute("CREATE TABLE stickies([key] INTEGER,noteclass INTEGER,flags INTEGER,title TEXT,colour INTEGER,category INTEGER);CREATE TABLE stickiesproperties([key] INTEGER,[type] INTEGER,t_b INTEGER,t_i INTEGER,t_d TEXT,t_s TEXT);CREATE TABLE content_rtf([key] INTEGER,rtf BLOB,search TEXT);CREATE TABLE categories([key] INTEGER,name TEXT,parent INTEGER);INSERT INTO categories VALUES(1,'Imported folder',NULL);INSERT INTO stickies VALUES(1,6,0,'Original',11862015,1);INSERT INTO stickies VALUES(2,6,4,'Encrypted',11862015,1);INSERT INTO stickiesproperties VALUES(1,18,NULL,222,NULL,NULL);INSERT INTO stickiesproperties VALUES(1,23,NULL,310,NULL,NULL);"); db.Run("INSERT INTO content_rtf VALUES(?,?,?);", 1, Encoding.ASCII.GetBytes(@"{\rtf1 Original text}"), "Original text"); }
            byte[] before = File.ReadAllBytes(file); var result = StickiesImporter.Read(file); Check(result.Notes.Count == 1 && result.EncryptedSkipped == 1, "v10 encrypted rows explicitly skipped"); Check(result.Notes[0].State == NoteState.Stored && result.Notes[0].Left == 222 && result.Notes[0].Width == 310, "v10 state and position mapping"); Check(result.Catalog.Folders.Single().Id == result.Notes[0].FolderId, "v10 folder mapping"); Check(result.Notes[0].PlainText == "Original text" && result.Notes[0].Body.Length > 0, "v10 content import"); Check(before.SequenceEqual(File.ReadAllBytes(file)), "v10 import never writes source");
        }
        private static void EncryptedEdits(string dir)
        {
            string file = Path.Combine(dir, "encrypted-edits.db"); string id;
            using (var lib = new NoteLibrary(file))
            {
                var folder = new Folder { Name = "Secure", Encrypted = true }; lib.Catalog.Folders.Add(folder); lib.Unlock("test-password");
                var n = lib.Create(); id = n.Id; n.Body = Encoding.UTF8.GetBytes("original"); lib.Save(n); lib.Move(n, NoteState.Stored, folder.Id);
                lib.Load(n); lib.Load(n); Check(n.Encrypted && Encoding.UTF8.GetString(n.Body) == "original", "encrypted repeated load keeps persisted flag");
                n.Body = Encoding.UTF8.GetBytes("edited secure payload"); lib.Save(n); Check(n.Encrypted && n.Body == null, "encrypted edit remains encrypted and unloaded");
                lib.Lock(); lib.Unlock("test-password"); lib.Load(n); Check(Encoding.UTF8.GetString(n.Body) == "edited secure payload", "encrypted edit survives relock");
                lib.Lock(); Throws(() => lib.SetFolderEncrypted(folder, false), "encryption change requires unlock");
                n.Title = "Renamed while locked"; lib.Save(n); Check(n.Body == null && n.Encrypted, "locked metadata save avoids loading body");
                lib.Unlock("test-password"); lib.SetFolderEncrypted(folder, false); Check(!n.Encrypted && Encoding.UTF8.GetString(n.Body) == "edited secure payload", "disabling folder encryption persists plaintext");
                lib.SetFolderEncrypted(folder, true); lib.Reload(); Check(lib.SessionPassword == null && lib.Notes[id].Encrypted && lib.Notes[id].Body == null, "reload relocks encrypted store after restore"); lib.Lock();
            }
            using (var lib = new NoteLibrary(file)) { var n = lib.Notes[id]; lib.Unlock("test-password"); lib.Load(n); Check(Encoding.UTF8.GetString(n.Body) == "edited secure payload", "encrypted edit survives process restart"); lib.Move(n, NoteState.Desktop); Check(!n.Encrypted && !n.ContentDecrypted, "restore from encrypted store clears ciphertext state"); }
        }
        private static void Packets()
        {
            var n = new Note { Title = "Note", PlainText = "line 1\nline 2", Body = Encoding.ASCII.GetBytes(@"{\rtf1 test}"), Secret = true, Color = "#FF1122" }; string encoded = Transfer.EncodeSticky(n); var decoded = Transfer.DecodeSticky(encoded); Check(decoded.PlainText == n.PlainText && decoded.Secret && decoded.Color == n.Color, "Stickies wire properties"); Check(decoded.Body.SequenceEqual(n.Body), "RTF wire round trip");
            n.Left = -300; n.Top = 420; decoded = Transfer.DecodeSticky(Transfer.EncodeSticky(n, 53000)); Check(decoded.Left == n.Left && decoded.Top == n.Top && decoded.SourcePort == 53000, "wire position and reply port");
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); try { var incoming = listener.AcceptTcpClientAsync(); Transfer.SendAsync(n, "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port, 53000).GetAwaiter().GetResult(); using (var client = incoming.GetAwaiter().GetResult()) { client.ReceiveTimeout = 5000; var received = Transfer.DecodeSticky(Transfer.ReadFrame(client.GetStream())); Check(received.PlainText == n.PlainText && received.SourcePort == 53000, "TCP loopback note transfer"); } } finally { listener.Stop(); }
            foreach (int size in new[] { 0, 20, 254, 255, 65534, 65535, 100000 }) { string text = new string('a', size); using (var s = new MemoryStream(Transfer.Frame(text))) Check(Transfer.ReadFrame(s) == text, "CArchive frame size " + size); }
            Throws(() => Transfer.ReadFrame(new MemoryStream(new byte[] { 255, 255, 255, 255, 255, 255, 127 })), "oversized frame rejected"); Throws(() => Transfer.ReadFrame(new MemoryStream(new byte[] { 4, 1 })), "truncated frame rejected");
            var package = Json.Read<NotePackage>(Json.Bytes(NotePackage.From(n))); var imported = package.Import(); Check(imported.Id != n.Id && imported.PlainText == n.PlainText && imported.State == NoteState.Desktop, "portable note import");
        }
        private static void ResizeGeometry()
        {
            var start = new Rect(100, 200, 260, 190);
            var bottomRight = WindowResize.Calculate(start, false, true, false, true, 40, 30, 100, 40);
            Check(bottomRight == new Rect(100, 200, 300, 220), "resize from right and bottom");
            var topLeft = WindowResize.Calculate(start, true, false, true, false, -50, -20, 100, 40);
            Check(topLeft == new Rect(50, 180, 310, 210), "resize from left and top keeps opposite edges fixed");
            var clamped = WindowResize.Calculate(start, false, true, false, true, -1000, -1000, 100, 40);
            Check(clamped.Width == 100 && clamped.Height == 40 && clamped.Left == 100 && clamped.Top == 200, "resize respects minimum size");
            var stack = new NoteStack { Width = 240, Height = 60 };
            var roundTrip = Json.Read<NoteStack>(Json.Bytes(stack));
            Check(roundTrip.Width == 240 && roundTrip.Height == 60, "stack size persists in JSON");
            var catalog = new Catalog(); catalog.Stacks.Add(new NoteStack { Width = 0, Height = 0 }); catalog.Normalize();
            Check(catalog.Stacks[0].Width == 180 && catalog.Stacks[0].Height == 44, "legacy stack sizes receive defaults");
            var settings = new Settings { DisabledShortcuts = null }; settings.Normalize();
            Check(settings.DisabledShortcuts != null, "disabled shortcut memory normalizes");
            var migrated = new Settings { Version = 0, DoubleClickRoll = true }; migrated.Normalize();
            Check(migrated.Version == 3 && migrated.SnapDistance == 10 && migrated.LinksClickable && migrated.ClickMakesVisible && migrated.AlarmSoundEnabled && migrated.HighlightColors.Count == 7 && migrated.DoubleClickAction == "roll" && migrated.SearchDesktop && migrated.SearchContent && migrated.SearchBarExpanded, "settings migration keeps new defaults");
            var nonRolling = new Settings { Version = 0, DoubleClickRoll = false }; nonRolling.Normalize();
            Check(nonRolling.DoubleClickAction == "none", "legacy double-click preference migrates");
            var dataPath = Path.Combine(Path.GetTempPath(), "deskpins-data-location");
            Check(DataLocation.Same(dataPath + "\\", dataPath) && DataLocation.Resolve(dataPath) == Path.GetFullPath(dataPath), "data directory resolution and comparison");
            var pointerFile = Path.Combine(Path.GetTempPath(), "deskpins-pointer-" + Guid.NewGuid().ToString("N") + ".txt");
            var customData = Path.Combine(Path.GetTempPath(), "deskpins-custom-data");
            DataLocation.SetCustom(customData, pointerFile);
            Check(File.Exists(pointerFile) && DataLocation.Resolve(null, pointerFile) == Path.GetFullPath(customData), "custom data directory pointer round trip");
            DataLocation.SetCustom(DataLocation.DefaultDirectory, pointerFile);
            Check(!File.Exists(pointerFile), "returning to default data directory clears pointer");
            var gesture = (System.Windows.Input.KeyGesture)new System.Windows.Input.KeyGestureConverter().ConvertFromString("Ctrl+Alt+Win+Shift+K");
            Check(gesture.Key == System.Windows.Input.Key.K && gesture.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Windows | System.Windows.Input.ModifierKeys.Shift), "Win modifier shortcut parses");
        }
        private static void UiTests(string dir)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var visual = new System.Windows.Media.DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(System.Windows.Media.Brushes.Yellow, null, new Rect(0, 0, 10, 10)); var target = new System.Windows.Media.Imaging.RenderTargetBitmap(10, 10, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); target.Render(visual); byte[] rendered = new byte[400]; target.CopyPixels(rendered, 40, 0); bool rasterAvailable = rendered.Any(b => b != 0); if (rasterAvailable) Check(true, "WPF pixel rendering"); else Console.WriteLine("SKIP WPF visual pixels: graphics capture unavailable in this environment"); using (var host = new AppHost(Path.Combine(dir, "ui"), true))
            {
                var n = host.Library.Create(); n.PlainText = "WPF 编辑器\n☐ item"; n.Body = new byte[0]; host.Library.Save(n); host.Sync(); var w = host.Windows[n.Id]; Check(w.TextEditor != null, "WPF text window created"); w.TextEditor.SelectAll(); w.TextEditor.Format("bold"); w.Flush(); Check(n.Body.Length > 30 && n.PlainText.Contains("WPF"), "WPF RTF serialization"); var editor = new TextEditor(); editor.Read(n); Check(new System.Windows.Documents.TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.Contains("WPF"), "WPF RTF reload");
                w.ToggleRoll(); Check(n.Rolled && w.Height == 24, "rolled window"); w.ToggleRoll(); Check(!n.Rolled, "unrolled window"); w.Toggle("lock"); Check(w.TextEditor.IsReadOnly, "content lock"); w.Toggle("lock");
                var secureFolder = new Folder { Name = "UI secure", Encrypted = true }; host.Library.Catalog.Folders.Add(secureFolder); host.Library.Unlock("ui-password");
                w.TextEditor.InsertText(" pending edit"); host.Library.Move(n, NoteState.Stored, secureFolder.Id); host.Sync();
                Check(!host.Windows.ContainsKey(n.Id) && n.Encrypted && n.Body == null, "moving live editor to encrypted store avoids stale flush");
                host.Library.Lock(); host.Library.Unlock("ui-password"); host.Library.Load(n); var secureEditor = new TextEditor(); secureEditor.Read(n); Check(new System.Windows.Documents.TextRange(secureEditor.Document.ContentStart, secureEditor.Document.ContentEnd).Text.Contains("pending edit"), "pending editor content persisted before encrypted move");
                host.Library.Move(n, NoteState.Desktop); host.Sync(); w = host.Windows[n.Id];
                w.TextEditor.InsertText(" before close"); host.Library.Move(n, NoteState.Closed); host.Sync(); host.Library.Load(n); Check(n.PlainText.Contains("before close"), "state change flushes pending desktop edits");
                host.Library.Move(n, NoteState.Desktop); host.Sync(); w = host.Windows[n.Id];
                var image = new ImageEditor(); image.Read(new byte[0]); var bytes = image.Bytes(); Check(bytes.Length > 0, "blank image PNG"); image.Transform("gray"); image.ResizeImage(40, 30); Check(image.Image.PixelWidth == 40 && image.Image.PixelHeight == 30, "WPF image resize"); byte[] rasterPixels = new byte[40 * 30 * 4]; image.Image.CopyPixels(rasterPixels, 40 * 4, 0); Check(rasterPixels.Any(b => b != 0), "GDI image preserves pixels without WPF rendering"); image.Undo(); Check(image.Image.PixelWidth == 250, "image undo");
                var result = host.Api.Execute("do new sticky API note"); Check(result.StartsWith("000"), "API create"); string id = result.Substring(result.IndexOf(": ") + 2); Check(host.Api.Execute("get desktop " + id + " text").Contains("API note"), "API read"); host.Api.Execute("set desktop " + id + " title Named"); Check(host.Library.Notes[id].Title == "Named", "API update"); host.Api.Execute("do desktop " + id + " delete"); host.Sync(); Check(!host.Library.Repository.LoadMetadata().Any(x => x.Id == id), "UI delete does not reinsert row");
                host.Api.Execute("do hideall"); host.Api.Execute("do hideall"); Check(host.Hidden, "hideall is idempotent"); host.Api.Execute("do showall"); host.Api.Execute("do showall"); Check(!host.Hidden, "showall is idempotent");
                var transfer = Path.Combine(dir, "transfer.dpx"); host.Library.Catalog.Settings.TransferFile = transfer; var outbound = host.Library.Create(); outbound.PlainText = "transfer content"; host.Library.Save(outbound); host.TransferFile(outbound, false); byte[] originalTransfer = File.ReadAllBytes(transfer); int noteCount = host.Library.Notes.Count; host.ImportTransfer(); Check(host.Library.Notes.Count == noteCount + 1 && new FileInfo(transfer).Length == 0, "transfer consumes only after successful import"); AtomicFile.Write(transfer, originalTransfer); host.ImportTransfer(); Check(host.Library.Notes.Count == noteCount + 1, "transfer retry after interrupted consumption avoids duplicates");
                host.Api.Execute("do stickymenuadd test Extension"); var menu = new System.Windows.Controls.ContextMenu(); host.AddExtensionMenus(menu, n); Check(menu.Items.Count == 1, "extension menu registration"); host.Api.Start(); var task = System.Threading.Tasks.Task.Run(() => ApiService.Client(Path.Combine(dir, "ui"), "do ping")); var deadline = DateTime.UtcNow.AddSeconds(5); while (!task.IsCompleted && DateTime.UtcNow < deadline) { var frame = new System.Windows.Threading.DispatcherFrame(); System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false)); System.Windows.Threading.Dispatcher.PushFrame(frame); System.Threading.Thread.Sleep(5); }
                Check(task.IsCompleted && task.Result.Contains("pong"), "named pipe IPC round trip");
                InteractionTests(host);
            }
            app.Shutdown();
        }
        private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(instance); }
        private static void SelectCategory(System.Windows.Controls.ListBox categories, int category)
        {
            categories.SelectedItem = categories.Items.Cast<System.Windows.Controls.ListBoxItem>().First(item => (int)item.Tag == category);
        }
        private static void InteractionTests(AppHost host)
        {
            var first = host.Library.Create(); var second = host.Library.Create();
            host.Focus(first); Check(System.Windows.Input.FocusManager.GetFocusedElement(host.Windows[first.Id]) == host.Windows[first.Id].TextEditor, "focusing note targets text editor for immediate typing");
            var copyTarget = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(host.Library.Repository.FilePath)), "data-copy-target");
            var copyPointer = Path.Combine(copyTarget, "pointer.txt");
            host.ApplyDataDirectory(copyTarget, copyPointer);
            Check(File.Exists(Path.Combine(copyTarget, "deskpins.db")) && File.ReadAllText(copyPointer).Trim() == Path.GetFullPath(copyTarget), "data directory migration copies database and pointer");
            var firstWindow = host.Windows[first.Id];
            Ui.LoadLanguage("en-US");
            Check(Ui.Text("管理便签…") == "Manage notes…", "tray and note manage label translates");
            typeof(AppHost).GetMethod("RefreshLanguageSurfaces", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(host, null);
            Check(Field<System.Windows.Controls.Button>(firstWindow, "closeButton").ToolTip.ToString() == "Close note (recoverable)", "language refresh updates live note chrome");
            Ui.LoadLanguage("zh-CN");
            typeof(AppHost).GetMethod("RefreshLanguageSurfaces", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(host, null);
            Check(Field<System.Windows.Controls.Button>(firstWindow, "closeButton").ToolTip.ToString() == "关闭便签（可恢复）", "language refresh restores live note chrome");
            var manager = new ManagerWindow(host);
            var categories = Field<System.Windows.Controls.ListBox>(manager, "categories"); var listing = Field<System.Windows.Controls.ListBox>(manager, "notes");
            Check(categories.Items.Count == 11, "manager keeps the icon category rail");
            Check(categories.Items.Cast<System.Windows.Controls.ListBoxItem>().Select(i => (int)i.Tag).SequenceEqual(new[] { 0, 1, 2, 9, 8, 3, 4, 5, 6, 7, 10 }), "manager rail places tags below stacks and stored below tags");
            SelectCategory(categories, 1); listing.SelectedItems.Add(first); listing.SelectedItems.Add(second); manager.Refresh();
            Check(listing.SelectedItems.Count == 2, "manager refresh preserves multiple selection");
            var folder = new Folder { Name = "Collection added while manager is open" }; host.Library.Catalog.Folders.Add(folder); SelectCategory(categories, 8);
            var tree = Field<System.Windows.Controls.TreeView>(manager, "tree"); folder.Name = "Renamed collection"; host.Library.SaveCatalog(); manager.Refresh();
            var renamedNode = TreeItems(tree.Items).FirstOrDefault(n => n.Tag is Folder f && f.Id == folder.Id);
            Check(renamedNode != null && ((System.Windows.Controls.Grid)renamedNode.Header).Children.OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == folder.Name), "manager navigation reflects collection changes");
            var closedSample = host.Library.Create(); closedSample.Title = "Closed sample"; host.Library.Save(closedSample); host.Library.Move(closedSample, NoteState.Closed); closedSample.ShowId = true;
            Check(closedSample.ManagerTitle.StartsWith("id " + closedSample.ShortId + " Closed sample"), "closed note title exposes its short id");
            var firstFolder = new Folder { Name = "Order A" }; var secondFolder = new Folder { Name = "Order B" }; var thirdFolder = new Folder { Name = "Order C" };
            host.Library.Catalog.Folders.Add(firstFolder); host.Library.Catalog.Folders.Add(secondFolder); host.Library.Catalog.Folders.Add(thirdFolder);
            var moveCollection = typeof(ManagerWindow).GetMethod("MoveCollectionItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            moveCollection.Invoke(manager, new object[] { "folders", thirdFolder.Id, firstFolder, 2 });
            Check(host.Library.Catalog.Folders.IndexOf(firstFolder) < host.Library.Catalog.Folders.IndexOf(thirdFolder) && host.Library.Catalog.Folders.IndexOf(thirdFolder) < host.Library.Catalog.Folders.IndexOf(secondFolder), "folder drag reorders siblings");
            moveCollection.Invoke(manager, new object[] { "folders", thirdFolder.Id, secondFolder, 1 });
            Check(thirdFolder.ParentId == secondFolder.Id, "folder drop inside sets parent");
            moveCollection.Invoke(manager, new object[] { "folders", secondFolder.Id, thirdFolder, 1 });
            Check(secondFolder.ParentId != thirdFolder.Id, "folder drag rejects cyclic nesting");
            var firstStack = new NoteStack { Name = "Order stack A" }; var secondStack = new NoteStack { Name = "Order stack B" };
            host.Library.Catalog.Stacks.Add(firstStack); host.Library.Catalog.Stacks.Add(secondStack);
            moveCollection.Invoke(manager, new object[] { "stacks", secondStack.Id, firstStack, 0 });
            Check(host.Library.Catalog.Stacks.IndexOf(secondStack) < host.Library.Catalog.Stacks.IndexOf(firstStack), "stack drag reorders collection");
            var firstOrderTag = new Tag { Name = "Order tag A" }; var secondOrderTag = new Tag { Name = "Order tag B" };
            host.Library.Catalog.Tags.Add(firstOrderTag); host.Library.Catalog.Tags.Add(secondOrderTag);
            moveCollection.Invoke(manager, new object[] { "tags", firstOrderTag.Id, secondOrderTag, 2 });
            Check(host.Library.Catalog.Tags.IndexOf(secondOrderTag) < host.Library.Catalog.Tags.IndexOf(firstOrderTag), "tag drag reorders collection");
            var searchTag = new Tag { Name = "search-tag", Color = "#00AA00" }; host.Library.Catalog.Tags.Add(searchTag);
            var storedHit = host.Library.Create(); storedHit.Title = "ScopeHit stored"; host.Library.Save(storedHit); host.Library.Move(storedHit, NoteState.Stored, firstFolder.Id);
            var desktopHit = host.Library.Create(); desktopHit.Title = "ScopeHit desktop"; desktopHit.Tags.Add(searchTag.Id); host.Library.Save(desktopHit); host.Sync();
            SelectCategory(categories, 10); Field<System.Windows.Controls.TextBox>(manager, "search").Text = "ScopeHit"; manager.Refresh();
            Check(listing.Items.Cast<Note>().Any(n => n.Id == storedHit.Id) && listing.Items.Cast<Note>().Any(n => n.Id == desktopHit.Id), "search includes stored and desktop notes by default");
            host.Library.Catalog.Settings.SearchStored = false; manager.Refresh();
            Check(listing.Items.Cast<Note>().All(n => n.Id != storedHit.Id) && listing.Items.Cast<Note>().Any(n => n.Id == desktopHit.Id), "search scope excludes unchecked stored notes");
            host.Library.Catalog.Settings.SearchStored = true; host.Library.Catalog.Settings.SearchTagFilter = new System.Collections.Generic.List<string> { searchTag.Id }; manager.Refresh();
            Check(listing.Items.Cast<Note>().All(n => n.Id != storedHit.Id) && listing.Items.Cast<Note>().Any(n => n.Id == desktopHit.Id), "search tag filter narrows results");
            host.Library.Catalog.Settings.SearchTagFilter.Clear(); manager.Refresh();
            var tagDialogType = typeof(ManagerWindow).Assembly.GetType("DeskPins.App.SearchTagWindow");
            var tagDialog = (Window)Activator.CreateInstance(tagDialogType, new object[] { host, new System.Collections.Generic.List<string>() });
            Check(Descendants((DependencyObject)tagDialog.Content).OfType<System.Windows.Controls.Button>().Any(b => b.Content.ToString() == "设置以上标签"), "search tag filter dialog exposes filter actions");
            SelectCategory(categories, 6); manager.Refresh();
            var calendarHost = Field<System.Windows.Controls.Border>(manager, "calendarHost");
            var dayButtons = Descendants(calendarHost).OfType<System.Windows.Controls.Button>().Where(b => b.Tag is DateTime).ToList();
            Check(dayButtons.Count == DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month), "calendar renders every day of the selected month");
            SelectCategory(categories, 8); manager.Refresh();
            var imageNote = host.Library.Create(NoteKind.Image); host.Sync(); var imageWindow = host.Windows[imageNote.Id];
            host.Library.Catalog.Settings.Styles[1].Color = "#123456"; host.Library.Catalog.Settings.Styles[1].Topmost = true; imageWindow.ApplyStyle(1);
            Check(host.Library.Repository.LoadMetadata().Single(x => x.Id == imageNote.Id).Color == "#123456", "image note style persists immediately");
            Check(imageWindow.Topmost, "style applies topmost to live window");
            imageWindow.Toggle("top"); Check(!imageWindow.Topmost, "topmost toggle updates live window");
            imageNote.Title = "Changed window title"; host.Library.Save(imageNote); host.Sync(); Check(imageWindow.Title.EndsWith("Changed window title"), "window title reflects note rename");
            imageWindow.ImageEditor.Selection = new Rect(200, 100, 20, 20); imageWindow.ImageEditor.ResizeImage(30, 20); imageWindow.ImageEditor.Undo(); imageWindow.ImageEditor.Undo(true);
            Check(imageWindow.ImageEditor.Selection.IsEmpty, "image undo clears stale selection bounds");
            var textNote = host.Library.Create(); host.Sync(); var textWindow = host.Windows[textNote.Id]; textWindow.TextEditor.InsertText("latest unflushed text"); textWindow.TextEditor.Selection.Select(textWindow.TextEditor.Document.ContentEnd, textWindow.TextEditor.Document.ContentEnd);
            Check(textWindow.TextEditor.SelectedPlainText.Contains("latest unflushed text"), "plain copy source uses current editor content");
            host.Library.Catalog.Settings.Shortcuts["copy"] = "";
            Check(host.ShortcutFor("copy") == "" && host.ActionFor(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control) == null, "empty shortcut disables dispatch");
            host.Library.Catalog.Settings.Shortcuts["copy"] = "Q";
            Check(host.ShortcutFor("copy") == "Q" && host.ActionFor(System.Windows.Input.Key.Q, System.Windows.Input.ModifierKeys.None) == "copy", "plain letter shortcut parses");
            host.Library.Catalog.Settings.Shortcuts["copy"] = "Ctrl+Alt+C";
            var textMenu = (System.Windows.Controls.ContextMenu)typeof(NoteWindow).GetMethod("TextMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(textWindow, null);
            Check(((System.Windows.Controls.MenuItem)textMenu.Items[1]).InputGestureText == "Ctrl+Alt+C" && host.ActionFor(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt) == "copy", "menu shortcut matches configured keyboard action"); host.Library.Catalog.Settings.Shortcuts.Remove("copy");
            var noteMenu = (System.Windows.Controls.ContextMenu)typeof(NoteWindow).GetMethod("Menu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(textWindow, null);
            var opacityMenu = noteMenu.Items.OfType<System.Windows.Controls.MenuItem>().First(m => m.Header.ToString() == "不透明度");
            opacityMenu.Items.OfType<System.Windows.Controls.MenuItem>().First(m => m.Header.ToString() == "50%").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(textNote.Opacity == .5 && Field<bool>(textWindow, "showOpacity"), "opacity menu applies immediately during active editing");
            Check(Math.Abs(textWindow.Opacity - .5) < .01, "opacity menu updates the live window opacity");
            opacityMenu.Items.OfType<System.Windows.Controls.MenuItem>().First(m => m.Header.ToString() == "100%").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(textNote.Opacity == 1, "opacity menu can restore full opacity");
            var spacingEditor = new TextEditor(); new System.Windows.Documents.TextRange(spacingEditor.Document.ContentStart, spacingEditor.Document.ContentEnd).Text = "abc\ndef\nabc\ndef";
            Check(spacingEditor.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().All(p => p.Margin == new Thickness(0)), "text editor removes paragraph auto margins");
            var spacingNote = new Note(); spacingEditor.Write(spacingNote); var spacingReloaded = new TextEditor(); spacingReloaded.Read(spacingNote);
            Check(spacingReloaded.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().All(p => p.Margin == new Thickness(0)), "loaded text keeps compact paragraph spacing");
            var rich = new TextEditor(); rich.Document.Blocks.Clear(); var paragraph = new System.Windows.Documents.Paragraph(); paragraph.Inlines.Add(new System.Windows.Documents.Run("mixed ")); paragraph.Inlines.Add(new System.Windows.Documents.Run("format") { FontWeight = FontWeights.Bold }); paragraph.Inlines.Add(new System.Windows.Documents.Run(" search")); rich.Document.Blocks.Add(paragraph);
            Check(rich.Find("mixed format") && rich.Selection.Text == "mixed format", "find spans differently formatted runs");
            var embedded = new TextEditor(); embedded.InsertText("Image inside text: "); embedded.InsertImage(imageWindow.ImageEditor.Image); var saved = new Note(); embedded.Write(saved); var reloaded = new TextEditor(); reloaded.Read(saved);
            Check(Descendants(reloaded.Document).OfType<System.Windows.Controls.Image>().Any(), "embedded text image survives RTF save and reload");
            rich.Selection.Select(rich.Document.ContentEnd, rich.Document.ContentEnd); Check(rich.Find("mixed format"), "find wraps after document end");
            var jpegEditor = new ImageEditor { SaveFormat = "JPEG" }; jpegEditor.Read(new byte[0]); var jpegBytes = jpegEditor.Bytes(); Check(jpegBytes.Length > 2 && jpegBytes[0] == 0xFF && jpegBytes[1] == 0xD8, "image note save format follows settings");
            var canvas = new ImageEditor(); canvas.Read(new byte[0]); byte[] blank = canvas.Bytes(); int changes = 0; canvas.Changed += () => changes++;            canvas.BeginStroke(new Point(10, 10)); canvas.ContinueStroke(new Point(25, 25)); canvas.CancelEdit(); Check(canvas.Bytes().SequenceEqual(blank) && changes == 0, "cancel restores incomplete image stroke");
            canvas.BeginStroke(new Point(10, 10)); canvas.ContinueStroke(new Point(25, 25)); canvas.Locked = true; Check(canvas.Bytes().SequenceEqual(blank), "locking canvas cancels incomplete stroke"); canvas.Locked = false;
            canvas.BeginStroke(new Point(10, 10)); canvas.CommitStroke(new Point(10, 10)); Check(!canvas.Bytes().SequenceEqual(blank) && changes == 1, "single pen click draws a dot"); canvas.Undo(); Check(canvas.Bytes().SequenceEqual(blank), "committed stroke undo restores image"); canvas.Undo(true);
            canvas.Tool = ImageTool.Select; canvas.BeginStroke(new Point(0, 0)); canvas.CommitStroke(new Point(250, 160)); Check(canvas.Selection.Width == 250 && canvas.Selection.Height == 160 && changes == 3, "selection reaches image edges without content change"); canvas.Undo(); Check(canvas.Bytes().SequenceEqual(blank), "selection does not consume drawing undo");
            var settings = new Settings(); settings.Normalize(); settings.Shortcuts["new"] = "Ctrl+C"; Throws(() => host.ValidateSettings(settings), "shortcut conflicts with defaults rejected"); settings.Shortcuts.Clear(); settings.Shortcuts["new"] = "Q"; host.ValidateSettings(settings); settings.Shortcuts.Clear(); settings.GlobalNewKey = "Q"; settings.GlobalToggleKey = settings.GlobalManageKey = ""; Throws(() => host.ValidateSettings(settings), "plain global shortcut rejected"); settings.GlobalNewKey = settings.GlobalToggleKey = "Ctrl+Alt+N"; Throws(() => host.ValidateSettings(settings), "duplicate global shortcuts rejected"); settings.GlobalToggleKey = "invalid shortcut"; Throws(() => host.ValidateSettings(settings), "invalid global shortcut rejected before saving"); settings.GlobalNewKey = settings.GlobalToggleKey = ""; settings.TomorrowHour = 24; Throws(() => host.ValidateSettings(settings), "invalid tomorrow hour rejected before normalization");
            var snapNote = host.Library.Create(); host.Sync(); var snapWindow = host.Windows[snapNote.Id]; host.Select(snapNote, false);
            var workArea = SystemParameters.WorkArea;
            foreach (var other in host.Windows.Values.Where(w => w != snapWindow)) { other.Left = 10000; other.Top = 10000; }
            snapWindow.Left = workArea.Left + 4; snapWindow.Top = workArea.Top + 120;
            host.SnapAfterDrag(snapWindow, false);
            Check(Math.Abs(snapWindow.Left - workArea.Left) < .1 && Math.Abs(snapNote.Left - workArea.Left) < .1, "snap applies on drag release");
            snapWindow.Left = workArea.Left + 4; host.SnapAfterDrag(snapWindow, true);
            Check(Math.Abs(snapWindow.Left - (workArea.Left + 4)) < .1, "Alt suppresses release snap");
            LayoutTests(host, manager, first);
            manager.Close();
        }
        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var descendant in Descendants(child)) yield return descendant;
        }
        private static System.Collections.Generic.IEnumerable<System.Windows.Controls.TreeViewItem> TreeItems(System.Windows.Controls.ItemCollection items)
        {
            foreach (System.Windows.Controls.TreeViewItem item in items)
            {
                yield return item;
                foreach (var child in TreeItems(item.Items)) yield return child;
            }
        }
        private static FrameworkElement Layout(Window window, double width, double height, string name)
        {
            var root = (FrameworkElement)window.Content; window.Content = null;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            if (auditDirectory != null)
            {
                Directory.CreateDirectory(auditDirectory); var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var drawing = new System.Windows.Media.DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height)); bitmap.Render(drawing); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(auditDirectory, name + ".png"))) encoder.Save(file);
            }
            window.Content = root; return root;
        }
        private static void LayoutTests(AppHost host, ManagerWindow manager, Note note)
        {
            var categories = Field<System.Windows.Controls.ListBox>(manager, "categories"); var preview = Field<System.Windows.Controls.ContentControl>(manager, "preview"); var notes = Field<System.Windows.Controls.ListBox>(manager, "notes"); var homeView = Field<System.Windows.Controls.ScrollViewer>(manager, "homeView");
            var outlineColumn = Field<System.Windows.Controls.ColumnDefinition>(manager, "outlineColumn"); var listColumn = Field<System.Windows.Controls.ColumnDefinition>(manager, "listColumn");
            host.Library.Catalog.Settings.ManagerOutlineWidths[2] = 250; host.Library.Catalog.Settings.ManagerListWidths[2] = 350;
            host.Library.Catalog.Settings.ManagerOutlineWidths[8] = 230; host.Library.Catalog.Settings.ManagerListWidths[8] = 320;
            SelectCategory(categories, 2); manager.Refresh();
            Check(Math.Abs(outlineColumn.Width.Value - 250) < .1 && Math.Abs(listColumn.Width.Value - 350) < .1, "manager restores stack pane widths");
            SelectCategory(categories, 8); manager.Refresh();
            Check(Math.Abs(outlineColumn.Width.Value - 230) < .1 && Math.Abs(listColumn.Width.Value - 320) < .1, "manager restores stored pane widths separately");
            host.Library.Catalog.Settings.ManagerOutlineWidths[2] = 220; host.Library.Catalog.Settings.ManagerListWidths[2] = 340;
            host.Library.Catalog.Settings.ManagerOutlineWidths[8] = 220; host.Library.Catalog.Settings.ManagerListWidths[8] = 340; manager.Refresh();
            host.Library.Move(note, NoteState.Stored); SelectCategory(categories, 8); manager.Refresh(); notes.SelectedItem = note;
            Layout(manager, 900, 520, "manager-minimum"); Check(notes.ActualWidth >= 200 && preview.ActualWidth >= 200, "manager minimum size keeps list and content usable");
            Layout(manager, 1180, 720, "manager-default"); Check(notes.ActualWidth >= 200 && preview.ActualWidth > 300, "manager content grows with window");
            var reminder = new ScheduleWindow(host, note, true, true); var controls = Descendants((DependencyObject)reminder.Content).ToArray(); var combos = controls.OfType<System.Windows.Controls.ComboBox>().ToArray();
            combos[0].SelectedIndex = 1; var relativeMinutes = controls.OfType<System.Windows.Controls.TextBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c).StartsWith("分钟数")); var time = controls.OfType<System.Windows.Controls.TextBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "时间"); Check(((FrameworkElement)time.Parent).Visibility == Visibility.Collapsed && ((FrameworkElement)relativeMinutes.Parent).Visibility == Visibility.Visible, "relative reminder uses independent minute input");
            combos[1].SelectedIndex = 5; Layout(reminder, 464, 370, "reminder-interval"); var visibleTextFields = controls.OfType<System.Windows.Controls.TextBox>().Where(c => ((FrameworkElement)c.Parent).Visibility == Visibility.Visible).ToArray(); Check(visibleTextFields.Contains(relativeMinutes), "minute field remains present for interval recurrence");
            var day = controls.OfType<System.Windows.Controls.TextBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c).StartsWith("日（")); var interval = controls.OfType<System.Windows.Controls.TextBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c).StartsWith("间隔数量")); Check(((FrameworkElement)day.Parent).Visibility == Visibility.Collapsed && ((FrameworkElement)interval.Parent).Visibility == Visibility.Visible, "recurrence only displays relevant parameters");
            var reminderButtons = controls.OfType<System.Windows.Controls.Button>().ToArray(); var cancel = reminderButtons.Single(c => c.Content.ToString() == "取消"); Check(cancel.IsCancel && reminderButtons.Single(c => c.Content.ToString() == "确定").IsDefault && cancel.TranslatePoint(new Point(), (UIElement)reminder.Content).Y + cancel.ActualHeight <= 370, "reminder buttons stay reachable in short window");
            var scheduled = host.Library.Create(); var simpleReminder = new ScheduleWindow(host, scheduled, true, false); var simpleControls = Descendants((DependencyObject)simpleReminder.Content).ToArray(); simpleControls.OfType<System.Windows.Controls.ComboBox>().Single().SelectedIndex = 1;
            simpleControls.OfType<System.Windows.Controls.TextBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c).StartsWith("分钟数")).Text = "15";
            var before = DateTime.UtcNow; simpleControls.OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "确定").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Check(scheduled.State == NoteState.Sleeping && scheduled.WakeUtc >= before.AddMinutes(15) && scheduled.WakeUtc <= DateTime.UtcNow.AddMinutes(15), "relative reminder commits correct future wake time");
            var clearSleep = new ScheduleWindow(host, scheduled, true, false); Descendants((DependencyObject)clearSleep.Content).OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "清除").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Check(scheduled.State == NoteState.Desktop && scheduled.WakeUtc == null, "clearing sleep restores desktop state");
            var untouched = new ScheduleWindow(host, scheduled, true, false); Descendants((DependencyObject)untouched.Content).OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "取消").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Check(scheduled.State == NoteState.Desktop && scheduled.WakeUtc == null, "cancel reminder leaves note unchanged");
            var transferFile = host.Library.Catalog.Settings.TransferFile; host.Library.Catalog.Settings.TransferFile = ""; var configuration = new SettingsWindow(host); host.Library.Catalog.Settings.TransferFile = transferFile;
            Check(configuration.ShowInTaskbar, "settings window is independent and visible in the taskbar");
            var navigation = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.ListBox>().Single();
            Check(navigation.Items.Count == 9, "settings uses a nine-category navigation rail");
            for (int i = 0; i < navigation.Items.Count; i++) { navigation.SelectedIndex = i; Layout(configuration, 900, 640, "settings-" + i); }
            Check(Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "取消").IsCancel, "settings supports Escape cancel");
            Layout(configuration, 900, 640, "settings-data");
            var dataButtons = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.Button>().Select(c => c.Content.ToString()).ToArray();
            Check(dataButtons.Contains("备份…") && dataButtons.Contains("恢复全部…") && dataButtons.Contains("导入便签…") && dataButtons.Contains("救回屏幕外便签"), "settings data page owns import and recovery actions");
            navigation.SelectedIndex = 7;
            var hotkeyTabs = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.TabControl>().Single();
            Check(hotkeyTabs.Items.Count == 2, "settings separates global and in-app shortcuts");
            hotkeyTabs.SelectedIndex = 0; Layout(configuration, 900, 640, "settings-hotkeys-global");
            Check(Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.Primitives.ToggleButton>().Any(c => object.Equals(c.Tag, "global-manage")), "global manage shortcut can be configured");
            hotkeyTabs.SelectedIndex = 1; Layout(configuration, 900, 640, "settings-hotkeys-app");
            var draft = Field<Settings>(configuration, "settings");
            var newToggle = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.Primitives.ToggleButton>().FirstOrDefault(c => object.Equals(c.Tag, "new"));
            var newKey = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.TextBox>().FirstOrDefault(c => object.Equals(c.Tag, "new"));
            Check(newToggle != null && newToggle.IsChecked == true && newKey != null && newKey.Text == "N", "in-app shortcut row exposes toggle and captured key");
            newToggle.IsChecked = false;
            Check(draft.Shortcuts["new"] == "" && draft.DisabledShortcuts.ContainsKey("new"), "disabling in-app shortcut preserves its gesture");
            newToggle.IsChecked = true;
            Check(draft.Shortcuts["new"] == "Ctrl+N" && !draft.DisabledShortcuts.ContainsKey("new"), "enabling in-app shortcut restores its gesture");
            using (var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("key-capture-test")))
            {
                newKey.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Q) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            }
            Check(newKey.Text == "Q" && draft.Shortcuts["new"] == "Q", "hotkey key input captures a new key");
            navigation.SelectedIndex = 0;
            var language = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "界面语言");
            language.SelectedIndex = 1;
            Check(Ui.Text("保存") == "Save" && configuration.Title == "DeskPins Settings", "language dropdown previews English");
            Layout(configuration, 900, 640, "settings-en");
            navigation = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.ListBox>().Single(); navigation.SelectedIndex = 7;
            hotkeyTabs = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.TabControl>().Single();
            hotkeyTabs.SelectedIndex = 1; Layout(configuration, 900, 640, "settings-hotkeys-en");
            Check(((System.Windows.Controls.TabItem)hotkeyTabs.Items[0]).Header.ToString() == "Global shortcuts", "hotkey tabs follow the selected language");
            navigation.SelectedIndex = 0;
            language = Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Language");
            language.SelectedIndex = 0;
            Check(Ui.Text("保存") == "保存", "language dropdown returns to Chinese");
            Descendants((DependencyObject)configuration.Content).OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "保存").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(host.Library.Catalog.Settings.Language == "zh-CN", "settings save commits the selected language");
            foreach (var kind in new[] { "folders", "friends", "tags" }) { var collection = new CollectionsWindow(host, kind); Layout(collection, 444, 280, "collection-" + kind); Check(Descendants((DependencyObject)collection.Content).OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "关闭").IsCancel, "collection keyboard close " + kind); collection.Close(); }
            var attachment = new AttachWindow(host, note); Descendants((DependencyObject)attachment.Content).OfType<System.Windows.Controls.ListBox>().Single().ItemsSource = new[] { new WindowInfo { Title = "Sample document — Editor", Class = "SampleEditor" } }; Layout(attachment, 524, 360, "attachment"); Check(Descendants((DependencyObject)attachment.Content).OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "确定").IsDefault, "attachment supports Enter confirm");
            for (int category = 0; category <= 10; category++) { SelectCategory(categories, category); Layout(manager, 900, 520, "manager-category-" + category); Check(category == 0 ? homeView.ActualWidth >= 500 : notes.ActualWidth >= 200 && preview.ActualWidth >= 200, "minimum manager layout in category " + category); }
            var noteWindow = host.Windows.Values.First(w => w.TextEditor != null); noteWindow.TextEditor.InsertText("Sample note\n☐ Task one\n☑ Task two"); Layout(noteWindow, 260, 190, "note-text"); Check(noteWindow.TextEditor.ActualHeight > 100, "text note keeps editable body below title"); noteWindow.ToggleRoll(); Layout(noteWindow, 260, 24, "note-rolled"); noteWindow.ToggleRoll();
            var imageWindow = host.Windows.Values.First(w => w.ImageEditor != null); Layout(imageWindow, 260, 190, "note-image"); Check(imageWindow.ImageEditor.ActualWidth >= 250, "image note keeps canvas scrollable");
            var stack = new StackWindow(host, new NoteStack { Name = "Sample stack with a long title that must be trimmed", Width = 240, Height = 60 }); stack.Refresh(); Check(Math.Abs(stack.Width - 240) < .1 && Math.Abs(stack.Height - 60) < .1, "stack window restores its saved size"); Layout(stack, 240, 60, "stack"); stack.Close();
            var topStack = new StackWindow(host, new NoteStack { Name = "Topmost stack", Topmost = true }); Check(topStack.Topmost, "stack window reads saved topmost state"); topStack.Close();
            var backupFile = Path.Combine(Path.GetDirectoryName(host.Library.Repository.FilePath), "layout-backup.db"); host.Library.Repository.Backup(backupFile); var backup = new BackupBrowserWindow(host, backupFile); Layout(backup, 444, 280, "backup"); Check(Descendants((DependencyObject)backup.Content).OfType<System.Windows.Controls.Button>().Single(c => c.Content.ToString() == "取消").IsCancel, "backup browser supports Escape cancel"); backup.Close();
            reminder.Close(); configuration.Close(); attachment.Close();
        }
    }
}
