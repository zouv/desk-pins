using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public string Title, Class;
        public Rect Bounds;
        public override string ToString() { return Title + "    [" + Class + "]"; }
    }
    public static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr arg);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        public static List<WindowInfo> Windows(bool includeAll = false)
        {
            var result = new List<WindowInfo>();
            EnumWindows((h, p) => { if (!IsWindowVisible(h) || IsIconic(h)) return true; var title = new StringBuilder(2048); GetWindowText(h, title, title.Capacity); if (!includeAll && title.Length == 0) return true; var cls = new StringBuilder(256); GetClassName(h, cls, cls.Capacity); if (cls.ToString().StartsWith("HwndWrapper", StringComparison.Ordinal) && title.ToString().StartsWith("DeskPins", StringComparison.Ordinal)) return true; RECT r; GetWindowRect(h, out r); result.Add(new WindowInfo { Handle = h, Title = title.ToString(), Class = cls.ToString(), Bounds = new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top)) }); return true; }, IntPtr.Zero);
            return result;
        }
        public static void SetGhost(Window window, bool ghost)
        {
            IntPtr h = new WindowInteropHelper(window).Handle; if (h == IntPtr.Zero) return; long style = GetWindowLongPtr(h, -20).ToInt64(); style = ghost ? style | 0x20L | 0x80000L : style & ~0x20L; if (!ghost && !window.AllowsTransparency && window.Opacity >= 1) style &= ~0x80000L; SetWindowLongPtr(h, -20, new IntPtr(style)); if (ghost) SetLayeredWindowAttributes(h, 0, (byte)(Math.Max(0, Math.Min(1, window.Opacity)) * 255), 2);
        }
        public static void ToolWindow(Window window) { var h = new WindowInteropHelper(window).Handle; long s = GetWindowLongPtr(h, -20).ToInt64(); SetWindowLongPtr(h, -20, new IntPtr((s | 0x80L) & ~0x40000L)); }
        public static void Resize(Window window, int direction) { ReleaseCapture(); SendMessage(new WindowInteropHelper(window).Handle, 0xA1, new IntPtr(direction), IntPtr.Zero); }
        public static bool RegisterHotkey(IntPtr h, int id, string gesture)
        {
            if (string.IsNullOrWhiteSpace(gesture)) return true;
            try { var g = (System.Windows.Input.KeyGesture)new System.Windows.Input.KeyGestureConverter().ConvertFromString(gesture); uint mods = (uint)g.Modifiers; uint native = 0x4000; if ((mods & 1) != 0) native |= 1; if ((mods & 2) != 0) native |= 2; if ((mods & 4) != 0) native |= 4; if ((mods & 8) != 0) native |= 8; return RegisterHotKey(h, id, native, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(g.Key)); } catch { return false; }
        }
        public static Rect Clamp(Rect bounds)
        {
            var point = new System.Drawing.Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2)); var screen = System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
            return new Rect(Math.Max(screen.Left, Math.Min(bounds.Left, screen.Right - Math.Min(bounds.Width, screen.Width))), Math.Max(screen.Top, Math.Min(bounds.Top, screen.Bottom - 24)), Math.Max(100, Math.Min(bounds.Width, screen.Width)), Math.Max(24, Math.Min(bounds.Height, screen.Height)));
        }
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder b, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder b, int count);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint color, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    }
}
