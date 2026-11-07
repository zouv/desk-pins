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
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
        private static void Throws(Action action, string name) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, name); }
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--seed") { string target = args[1]; Directory.CreateDirectory(target); using (var lib = new NoteLibrary(Path.Combine(target, "deskpins.db"))) { for (int i = 0; i < 10; i++) { var n = lib.Create(); n.Title = "Sample " + i; n.PlainText = "Sample desktop note " + i; n.Body = Encoding.ASCII.GetBytes(@"{\rtf1 Sample desktop note}"); lib.Save(n); } } return 0; }
            string dir = Path.Combine(Path.GetTempPath(), "DeskPins-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                Schedules(); Encryption(); Storage(dir); EncryptedEdits(dir); OriginalImport(dir); Packets(); if (args.Contains("--ui")) UiTests(dir);
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
                var n = lib.Create(); id = n.Id; n.Title = "测试便签"; n.Body = Encoding.ASCII.GetBytes(@"{\rtf1 hello}"); n.PlainText = "Unicode 中文_100%"; lib.Save(n); Check(lib.Search("中文_100%").Count() == 1, "Unicode and escaped LIKE search"); Check(lib.Search("' OR 1=1 --").Count() == 0, "parameterized search"); Check(!lib.Search("StateUtc").Any(), "search excludes metadata property names");
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
            }
            app.Shutdown();
        }
    }
}
