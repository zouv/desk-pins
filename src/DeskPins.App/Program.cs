using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskPins.Core;

namespace DeskPins.App
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var clock = Stopwatch.StartNew(); string data = DataLocation.Resolve(Option(args, "--data-dir")); string command = Option(args, "--api");
            if (command != null) { try { string result = ApiService.Client(data, command); string output = Option(args, "--output"); if (output != null) File.WriteAllText(output, result); return result.StartsWith("997") ? 2 : 0; } catch (Exception ex) { if (Option(args, "--output") != null) File.WriteAllText(Option(args, "--output"), ex.Message); return 2; } }
            bool test = args.Contains("--render") || args.Contains("--benchmark"); string identity = "Local\\" + ApiService.Identity(data); bool owner;
            if (test && Option(args, "--data-dir") == null) return 2;
            using (var mutex = new Mutex(true, identity, out owner))
            {
                if (!owner) { try { var files = args.Where(File.Exists).ToArray(); foreach (var file in files) ApiService.Client(data, "import " + Path.GetFullPath(file)); if (files.Length == 0) ApiService.Client(data, args.Contains("-") ? "new" : args.Contains("-showmanage") ? "manage" : args.Contains("-showsettings") ? "settings" : args.Contains("-showhideall") ? "toggle" : args.Contains("-newstickyfromscreen") ? "capture" : args.Contains("-rescue") ? "rescue" : args.Contains("-close") ? "quit" : "show"); return 0; } catch { return 2; } }
                if (args.Contains("-close")) { mutex.ReleaseMutex(); return 0; }
                if (args.Contains("--render")) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; AppHost host = null;
                application.DispatcherUnhandledException += (s, e) => { Ui.Error(e.Exception); e.Handled = true; };
                try
                {
                    long beforeHost = clock.ElapsedMilliseconds; host = new AppHost(data, test); long afterHost = clock.ElapsedMilliseconds; application.Exit += (s, e) => { if (host != null) host.Dispose(); };
                    if (test)
                    {
                        var n = args.Contains("--benchmark") ? host.Library.Notes.Values.FirstOrDefault(x => x.State == NoteState.Desktop) ?? host.Library.Create() : host.Library.Create(); n.Title = "DeskPins · 桌钉"; n.PlainText = "像纸质便签一样留在桌面。\n\n☐ 整理今天的工作\n☑ 保留熟悉的操作\n\n右键标题栏：提醒、附着、收藏与样式。"; n.Body = new byte[0]; host.Library.Save(n); host.Sync(); var window = host.Windows[n.Id]; window.Width = 300; window.Height = 230;
                        if (args.Contains("--render")) { window.Show(); Native.ShowWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle, 5); window.UpdateLayout(); application.Dispatcher.BeginInvoke(new Action(() => { string output = Option(args, "--output") ?? Path.Combine(data, "note.png"); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); var element = (FrameworkElement)window.Content; window.Content = null; element.Measure(new Size(window.Width, window.Height)); element.Arrange(new Rect(0, 0, window.Width, window.Height)); element.UpdateLayout(); var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, window.Width, window.Height)); image.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(output)) encoder.Save(stream); host.Exit(); }), DispatcherPriority.ApplicationIdle); }
                        else { int count; if (!int.TryParse(Option(args, "--notes"), out count)) count = 10; count = Math.Max(1, Math.Min(100, count)); for (int i = host.Library.Notes.Count; i < count; i++) { var note = host.Library.Create(); note.PlainText = "便签 " + i; note.Body = new byte[0]; host.Library.Save(note); } host.Sync(); foreach (var w in host.Windows.Values) w.Show(); application.Dispatcher.BeginInvoke(new Action(() => { var process = Process.GetCurrentProcess(); var result = new Benchmark { ReadyMilliseconds = clock.ElapsedMilliseconds, PrivateBytes = process.PrivateMemorySize64, WorkingSet = process.WorkingSet64, VisibleNotes = host.Windows.Count, HostMilliseconds = afterHost - beforeHost, WindowCreationMilliseconds = host.WindowCreationMilliseconds }; File.WriteAllText(Option(args, "--output") ?? Path.Combine(data, "benchmark.json"), Json.Text(result)); host.Exit(); }), DispatcherPriority.ApplicationIdle); }
                    }
                    else { bool existing = host.Library.Notes.Count > 0; host.Start(); foreach (var file in args.Where(File.Exists)) host.Import(file); if (existing && args.Contains("-")) host.New(); if (args.Contains("-showmanage")) host.Manage(); if (args.Contains("-showsettings")) host.Settings(); if (args.Contains("-showhideall")) host.ToggleAll(); if (args.Contains("-rescue")) host.Rescue(); if (args.Contains("-newstickyfromscreen")) application.Dispatcher.BeginInvoke(new Action(host.Capture)); }
                    application.Run(); return 0;
                }
                catch (Exception ex) { if (host != null) host.Dispose(); MessageBox.Show(Ui.Text(ex.Message) + "\n\n" + Ui.Text("数据文件未被重置。"), Ui.Text("DeskPins 启动失败"), MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
                finally { if (owner) mutex.ReleaseMutex(); }
            }
        }
        public static string Option(string[] args, string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        [System.Runtime.Serialization.DataContract] private sealed class Benchmark { [System.Runtime.Serialization.DataMember] public long ReadyMilliseconds, PrivateBytes, WorkingSet, HostMilliseconds, WindowCreationMilliseconds; [System.Runtime.Serialization.DataMember] public int VisibleNotes; }
    }
}
