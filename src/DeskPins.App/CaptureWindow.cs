using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPins.App
{
    public sealed class CaptureWindow : Window
    {
        private readonly Canvas canvas;
        private readonly System.Windows.Shapes.Rectangle selection;
        private System.Windows.Point start;
        private bool dragging;
        public BitmapSource Result { get; private set; }
        public CaptureWindow()
        {
            Title = "DeskPins 屏幕截取"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop; Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight; Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 0, 0)); AllowsTransparency = true; Cursor = Cursors.Cross;
            canvas = new Canvas(); selection = new System.Windows.Shapes.Rectangle { Stroke = System.Windows.Media.Brushes.DodgerBlue, StrokeThickness = 2, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255)) }; canvas.Children.Add(selection); Content = canvas;
            MouseLeftButtonDown += (s, e) => { start = e.GetPosition(canvas); dragging = true; CaptureMouse(); }; MouseMove += (s, e) => { if (!dragging) return; var p = e.GetPosition(canvas); Canvas.SetLeft(selection, Math.Min(start.X, p.X)); Canvas.SetTop(selection, Math.Min(start.Y, p.Y)); selection.Width = Math.Abs(p.X - start.X); selection.Height = Math.Abs(p.Y - start.Y); };
            MouseLeftButtonUp += (s, e) => { if (!dragging) return; dragging = false; ReleaseMouseCapture(); var end = e.GetPosition(canvas); int x = (int)(Left + Math.Min(start.X, end.X)), y = (int)(Top + Math.Min(start.Y, end.Y)), width = (int)Math.Abs(end.X - start.X), height = (int)Math.Abs(end.Y - start.Y); Hide(); if (width < 1 || height < 1) { Close(); return; } using (var bitmap = new Bitmap(width, height)) using (var graphics = Graphics.FromImage(bitmap)) { graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height)); using (var stream = new MemoryStream()) { bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0; var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); Result = decoder.Frames[0]; Result.Freeze(); } } Close(); }; KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }
    }
}
