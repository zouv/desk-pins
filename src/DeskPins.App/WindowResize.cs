using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPins.App
{
    /// <summary>
    /// Manual border resizing for borderless WPF windows (NoteWindow and StackWindow).
    /// ResizeMode.NoResize deliberately stays in place: the frame is drawn by the app,
    /// so native non-client hit testing is not available.
    /// </summary>
    public static class WindowResize
    {
        private sealed class Session
        {
            public Window Window;
            public bool Active, Left, Right, Top, Bottom;
            public Rect Start;
            public Point StartScreen;
            public double ScaleX = 1, ScaleY = 1;
            public Func<bool> VerticalEnabled;
            public Func<bool> ResizeEnabled;
            public Action Started, Completed;
            public Cursor HoverCursor;
        }

        public static void Attach(Window window, double minWidth, double minHeight, Func<bool> verticalEnabled = null, Action started = null, Action completed = null, Func<bool> resizeEnabled = null)
        {
            var session = new Session { Window = window, VerticalEnabled = verticalEnabled, Started = started, Completed = completed, ResizeEnabled = resizeEnabled };
            window.PreviewMouseLeftButtonDown += (s, e) => Start(session, e, minWidth, minHeight);
            window.PreviewMouseMove += (s, e) => Move(session, e, minWidth, minHeight);
            window.PreviewMouseLeftButtonUp += (s, e) => Finish(session, e);
            window.LostMouseCapture += (s, e) => Finish(session, null);
            window.MouseLeave += (s, e) => SetHoverCursor(session, null);
            window.Closed += (s, e) => SetHoverCursor(session, null);
        }

        private static void Start(Session session, MouseButtonEventArgs e, double minWidth, double minHeight)
        {
            if (session.Active || e.ChangedButton != MouseButton.Left) return;
            if (session.ResizeEnabled != null && !session.ResizeEnabled()) return;
            var window = session.Window;
            var point = e.GetPosition(window);
            double width = Math.Max(1, window.ActualWidth), height = Math.Max(1, window.ActualHeight);
            double zone = 6;
            bool left = point.X <= zone, right = point.X >= width - zone;
            bool top = point.Y <= zone, bottom = point.Y >= height - zone;
            if (left && right) { if (point.X < width / 2) right = false; else left = false; }
            if (top && bottom) { if (point.Y < height / 2) bottom = false; else top = false; }
            if (session.VerticalEnabled != null && !session.VerticalEnabled()) top = bottom = false;
            if (!left && !right && !top && !bottom) return;

            session.Left = left; session.Right = right; session.Top = top; session.Bottom = bottom;
            session.Start = new Rect(window.Left, window.Top, window.Width, window.Height);
            session.StartScreen = window.PointToScreen(point);
            var source = PresentationSource.FromVisual(window);
            if (source != null && source.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformToDevice;
                session.ScaleX = transform.M11 > 0 ? transform.M11 : 1;
                session.ScaleY = transform.M22 > 0 ? transform.M22 : 1;
            }
            if (!window.CaptureMouse()) return;
            session.Active = true;
            e.Handled = true;
            if (session.Started != null) session.Started();
            SetHoverCursor(session, CursorFor(left, right, top, bottom));
        }

        private static void Move(Session session, MouseEventArgs e, double minWidth, double minHeight)
        {
            var window = session.Window;
            if (!session.Active)
            {
                var point = e.GetPosition(window);
                double width = Math.Max(1, window.ActualWidth), height = Math.Max(1, window.ActualHeight);
                bool left = point.X <= 6, right = point.X >= width - 6, top = point.Y <= 6, bottom = point.Y >= height - 6;
                if (left && right) { if (point.X < width / 2) right = false; else left = false; }
                if (top && bottom) { if (point.Y < height / 2) bottom = false; else top = false; }
                if (session.VerticalEnabled != null && !session.VerticalEnabled()) top = bottom = false;
                SetHoverCursor(session, CursorFor(left, right, top, bottom));
                return;
            }
            var current = window.PointToScreen(e.GetPosition(window));
            double dx = (current.X - session.StartScreen.X) / session.ScaleX;
            double dy = (current.Y - session.StartScreen.Y) / session.ScaleY;
            var bounds = Calculate(session.Start, session.Left, session.Right, session.Top, session.Bottom, dx, dy, minWidth, minHeight);
            window.Left = bounds.Left; window.Top = bounds.Top; window.Width = bounds.Width; window.Height = bounds.Height;
            e.Handled = true;
        }

        private static void Finish(Session session, MouseButtonEventArgs e)
        {
            if (!session.Active) return;
            session.Active = false;
            session.Window.ReleaseMouseCapture();
            if (e != null) e.Handled = true;
            SetHoverCursor(session, null);
            if (session.Completed != null) session.Completed();
        }

        private static void SetHoverCursor(Session session, Cursor cursor)
        {
            if (cursor == null)
            {
                if (session.HoverCursor != null) Mouse.OverrideCursor = null;
                session.HoverCursor = null;
                return;
            }
            if (session.HoverCursor == cursor) return;
            session.HoverCursor = cursor;
            Mouse.OverrideCursor = cursor;
        }

        private static Cursor CursorFor(bool left, bool right, bool top, bool bottom)
        {
            bool horizontal = left || right, vertical = top || bottom;
            if (!horizontal && !vertical) return null;
            if (horizontal && vertical) return (left && top) || (right && bottom) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            return horizontal ? Cursors.SizeWE : Cursors.SizeNS;
        }

        public static Rect Calculate(Rect start, bool left, bool right, bool top, bool bottom, double dx, double dy, double minWidth, double minHeight)
        {
            double x = start.Left, y = start.Top, width = start.Width, height = start.Height;
            if (left) { width = Math.Max(minWidth, start.Width - dx); x = start.Right - width; }
            else if (right) width = Math.Max(minWidth, start.Width + dx);
            if (top) { height = Math.Max(minHeight, start.Height - dy); y = start.Bottom - height; }
            else if (bottom) height = Math.Max(minHeight, start.Height + dy);
            return new Rect(x, y, width, height);
        }
    }
}
