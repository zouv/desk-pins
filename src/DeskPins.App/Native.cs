using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
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
        public static void RaiseNoActivate(Window window)
        {
            var h = new WindowInteropHelper(window).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
        }
        public static bool RegisterHotkey(IntPtr h, int id, string gesture)
        {
            if (string.IsNullOrWhiteSpace(gesture)) return true;
            Key key; ModifierKeys modifiers;
            if (!Ui.TryParseGesture(gesture, out key, out modifiers)) return false;
            uint native = 0x4000;
            if ((modifiers & ModifierKeys.Alt) != 0) native |= 1;
            if ((modifiers & ModifierKeys.Control) != 0) native |= 2;
            if ((modifiers & ModifierKeys.Shift) != 0) native |= 4;
            if ((modifiers & ModifierKeys.Windows) != 0) native |= 8;
            return RegisterHotKey(h, id, native, (uint)KeyInterop.VirtualKeyFromKey(key));
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
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    }
}
