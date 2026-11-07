using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class ApiService : IDisposable
    {
        private readonly AppHost host;
        public readonly string PipeName;
        private NamedPipeServerStream waiting;
        private bool disposed;
        private readonly Dictionary<string, string> menus = new Dictionary<string, string>();
        private readonly List<string> events = new List<string>();
        private readonly HashSet<IntPtr> subscribers = new HashSet<IntPtr>();
        public ApiService(AppHost host, string directory) { this.host = host; PipeName = Identity(directory); host.Library.Changed += Changed; }
        public static string Identity(string directory) { using (var sha = System.Security.Cryptography.SHA256.Create()) return "DeskPins-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\', '/').ToLowerInvariant() + WindowsIdentity.GetCurrent().User.Value))).Replace("-", "").Substring(0, 24); }
        public void Start() { _ = Task.Run(Listen); }
        private async Task Listen()
        {
            while (!disposed)
            {
                var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false); security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                {
                    waiting = pipe; try { await pipe.WaitForConnectionAsync(); var read = Task.Run(() => Transfer.ReadFrame(pipe)); if (await Task.WhenAny(read, Task.Delay(5000)) != read) { pipe.Dispose(); throw new TimeoutException("API 请求读取超时。"); } var command = await read; var result = await Application.Current.Dispatcher.InvokeAsync(() => Execute(command)); byte[] bytes = Transfer.Frame(result); var write = pipe.WriteAsync(bytes, 0, bytes.Length); if (await Task.WhenAny(write, Task.Delay(5000)) != write) throw new TimeoutException("API 响应写入超时。"); await write; if (result == "000 exiting") _ = Application.Current.Dispatcher.BeginInvoke(new Action(host.Exit)); } catch (ObjectDisposedException) { break; } catch (Exception ex) { if (!disposed) host.Log(ex.Message); } finally { waiting = null; }
                }
            }
        }
        public static string Client(string directory, string command, int timeout = 10000)
        {
            using (var pipe = new NamedPipeClientStream(".", Identity(directory), PipeDirection.InOut, PipeOptions.None)) { pipe.Connect(timeout); var bytes = Transfer.Frame(command); pipe.Write(bytes, 0, bytes.Length); var read = Task.Run(() => Transfer.ReadFrame(pipe)); if (!read.Wait(timeout)) throw new TimeoutException("API 响应超时。"); return read.GetAwaiter().GetResult(); }
        }
        private void Changed() { var entry = DateTime.UtcNow.ToString("o") + " changed"; events.Add(entry); if (events.Count > 200) events.RemoveAt(0); foreach (var h in subscribers.ToArray()) ReplyCopyData(h, IntPtr.Zero, "event changed"); }
        public string Execute(string command)
        {
            try
            {
                command = (command ?? "").Trim(); if (command.StartsWith("api ")) command = command.Substring(4); if (command == "" || command == "show") { host.BringForward(); return "000 shown"; }
                if (command == "-" || command == "new") return "000 created: " + host.New().Id;
                if (command == "-close" || command == "quit") return "000 exiting";
                if (command == "-showmanage" || command == "manage") { host.Manage(); return "000 opened"; }
                if (command == "-showsettings" || command == "settings") { Application.Current.Dispatcher.BeginInvoke(new Action(host.Settings)); return "000 opened"; }
                if (command == "-showhideall" || command == "toggle") { host.ToggleAll(); return "000 toggled"; }
                if (command == "capture") { Application.Current.Dispatcher.BeginInvoke(new Action(host.Capture)); return "000 opened"; }
                if (command == "rescue") { host.Rescue(); return "000 rescued"; }
                var p = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (command == "do ping" || command == "ping") return "000 OK pong";
                if (command == "do register") return "000 registered"; if (command == "do deregister") return "000 deregistered";
                if (command == "get desktop list") return "001 " + string.Join(",", host.Library.Notes.Values.Where(n => n.State == NoteState.Desktop).Select(n => n.Id));
                if (command == "list" || command == "get notes") return Json.Text(host.Library.Notes.Values.ToList());
                if (command == "get events") return Json.Text(events);
                if (command.StartsWith("import ")) { host.Import(command.Substring(7).Trim('"')); return "000 imported"; }
                if (command.StartsWith("do new sticky ") || command.StartsWith("create ")) { string text = command.StartsWith("create ") ? command.Substring(7) : command.Substring(14); var n = host.Library.Create(); n.PlainText = text.Replace("^M", "\n"); n.Body = new byte[0]; host.Library.Save(n); host.Sync(); host.ReloadWindow(n); return "000 OK created: " + n.Id; }
                if (command.StartsWith("do new imagesticky")) { var n = host.Library.Create(NoteKind.Image); if (p.Length > 3) host.LoadImage(n, string.Join(" ", p.Skip(3))); host.Sync(); return "000 OK created: " + n.Id; }
                if (command == "do new sticky_paste" || command == "do new sticky_paste_plain") return "000 OK created: " + host.New(null, true).Id;
                if (command == "do hideall" || command == "do showall" || command == "do bringforward") { if (command == "do hideall") host.SetHidden(true); else if (command == "do showall") host.SetHidden(false); else host.BringForward(); return "000 done"; }
                if (command == "do saveini") { host.FlushAll(); return "000 saved"; }
                if (command == "do reindex") { host.Library.Repository.Vacuum(); return "000 reindexed"; }
                if (command == "do manage open") { host.Manage(); return "000 opened"; }
                if (command.StartsWith("do backup ")) { host.FlushAll(); host.Library.Repository.Backup(command.Substring(10).Trim('"')); return "000 backup taken"; }
                if (command.StartsWith("do stickymenuadd ")) { if (p.Length < 4) throw new ArgumentException("缺少菜单名称。"); menus[p[2]] = string.Join(" ", p.Skip(3)); return "000 added"; }
                if (command.StartsWith("do stickiesmenuadd ")) { var name = p[2]; host.AddTrayMenu(string.Join(" ", p.Skip(3)), () => events.Add("event tray " + name)); return "000 added"; }
                if (command.StartsWith("do stickymenudel ")) { menus.Remove(p[2]); return "000 removed"; }
                if (command.StartsWith("do stack ") && p.Length == 4) { var stack = host.Library.Catalog.Stacks.First(s => s.Id == p[2]); stack.Expanded = p[3] == "unstacked" || p[3] == "togglestacked" && !stack.Expanded; host.Library.SaveCatalog(); return "000 OK"; }
                if (p.Length >= 4 && (p[0] == "get" || p[0] == "set" || p[0] == "do") && p[1] == "desktop")
                {
                    Note n; if (!host.Library.Notes.TryGetValue(p[2], out n)) return "995 not found"; string prop = p[3];
                    host.FlushNote(n);
                    if (p[0] == "get") { if (prop == "text" || prop == "rtf" || prop == "encoded") { host.Library.Load(n); return "001 " + (prop == "text" ? n.PlainText : prop == "rtf" ? Encoding.ASCII.GetString(n.Body) : Transfer.EncodeSticky(n)); } if (prop == "title") return "001 " + n.Title; return "001 " + Json.Text(n); }
                    if (p[0] == "do") { switch (prop) { case "close": host.Library.Move(n, NoteState.Closed); break; case "delete": host.Library.Delete(n); break; case "rolled": n.Rolled = true; host.Library.Save(n); host.ReloadPosition(n); break; case "unrolled": n.Rolled = false; host.Library.Save(n); host.ReloadPosition(n); break; case "store": host.Library.Move(n, NoteState.Stored, p.Length > 4 ? p[4] : ""); break; case "copycontentplain": host.Library.Load(n); Clipboard.SetText(n.PlainText ?? ""); break; default: throw new ArgumentException("不支持的操作。"); } return "000 OK"; }
                    string value = string.Join(" ", p.Skip(4)); switch (prop) { case "title": n.Title = value; break; case "text": host.Library.Load(n); n.PlainText = value.Replace("^M", "\n"); n.Body = new byte[0]; break; case "rtf": n.Body = Encoding.ASCII.GetBytes(value.Replace("^M", "\n")); var editor = new TextEditor(); editor.Read(n); editor.Write(n); break; case "ontop": n.Topmost = value == "1" || value == "true"; break; case "ghost": n.Ghost = value == "1" || value == "true"; break; case "locked": n.Locked = value == "1" || value == "true"; break; case "opacity": n.Opacity = Math.Max(.1, Math.Min(1, Ui.Number(value, 100) / 100)); break; case "colour": case "color": n.Color = value; new System.Windows.Media.BrushConverter().ConvertFromString(value); break; default: throw new ArgumentException("不支持的属性。"); }
                    host.Library.Save(n); host.ReloadWindow(n); host.ReloadPosition(n); return "000 set";
                }
                if (p.Length >= 3 && p[0] == "do" && (p[1] == "wake" || p[1] == "sleep")) { Note n; if (!host.Library.Notes.TryGetValue(p[2], out n)) return "995 not found"; if (p[1] == "wake") host.Library.Move(n, NoteState.Desktop); else { if (p.Length < 4) throw new ArgumentException("缺少 Unix 秒时间戳。"); long seconds; if (!long.TryParse(p[3], out seconds)) throw new ArgumentException("时间戳无效。"); host.Library.Sleep(n, DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime); } return "000 OK"; }
                if (p.Length >= 3 && p[0] == "do" && (p[1] == "alarmstart" || p[1] == "alarmstop")) { var n = host.Library.Notes[p[2]]; n.AlarmUtc = p[1] == "alarmstart" ? DateTime.UtcNow : (DateTime?)null; host.Library.Save(n); host.Library.Tick(DateTime.UtcNow); return "000 done"; }
                return "997 unknown command";
            }
            catch (Exception ex) { return "997 " + ex.Message; }
        }
        public void Register(IntPtr handle) { subscribers.Add(handle); }
        public void Deregister(IntPtr handle) { subscribers.Remove(handle); }
        public void AddMenus(ContextMenu menu, Note n) { foreach (var pair in menus) { var command = pair.Key; menu.Items.Add(Ui.Item(pair.Value, () => { string message = "event menu " + command + " " + n.Id; events.Add(message); foreach (var h in subscribers.ToArray()) ReplyCopyData(h, IntPtr.Zero, message); })); } }
        public void ReplyCopyData(IntPtr target, IntPtr tag, string reply)
        {
            if (target == IntPtr.Zero) return; byte[] bytes = Encoding.UTF8.GetBytes(reply + "\0"); IntPtr ptr = Marshal.AllocHGlobal(bytes.Length); try { Marshal.Copy(bytes, 0, ptr, bytes.Length); var data = new AppHost.CopyData { Tag = tag, Size = bytes.Length, Data = ptr }; IntPtr result; SendMessageTimeout(target, 0x4A, IntPtr.Zero, ref data, 2, 200, out result); } finally { Marshal.FreeHGlobal(ptr); }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr h, int message, IntPtr w, ref AppHost.CopyData data, uint flags, uint timeout, out IntPtr result);
        public void Dispose() { disposed = true; host.Library.Changed -= Changed; if (waiting != null) waiting.Dispose(); }
    }
}
