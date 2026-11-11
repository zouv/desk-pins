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
        private readonly System.Windows.Shapes.Rectangle[] shades;
        private System.Windows.Point start;
        private bool dragging;
        public BitmapSource Result { get; private set; }
        public CaptureWindow(string mode = "Hole in white overlay")
        {
            Title = Ui.Text("DeskPins 屏幕截取"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop; Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight; AllowsTransparency = true; Cursor = Cursors.Cross;
            var hole = mode == "Hole in white overlay";
            Background = hole || mode == "No overlay" ? System.Windows.Media.Brushes.Transparent : new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 0, 0));
            canvas = new Canvas(); shades = new System.Windows.Shapes.Rectangle[hole ? 4 : 0];
            var shadeBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 255, 255, 255));
            for (int i = 0; i < shades.Length; i++)
            {
                shades[i] = new System.Windows.Shapes.Rectangle { Fill = shadeBrush, IsHitTestVisible = false };
                canvas.Children.Add(shades[i]);
            }
            selection = new System.Windows.Shapes.Rectangle { Stroke = System.Windows.Media.Brushes.DodgerBlue, StrokeThickness = 2, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255)), IsHitTestVisible = false }; Canvas.SetLeft(selection, 0); Canvas.SetTop(selection, 0); canvas.Children.Add(selection); Content = canvas;
            Loaded += (s, e) => UpdateShades(); SizeChanged += (s, e) => UpdateShades();
            MouseLeftButtonDown += (s, e) => { start = e.GetPosition(canvas); dragging = true; CaptureMouse(); UpdateShades(); }; MouseMove += (s, e) => { if (!dragging) return; var p = e.GetPosition(canvas); Canvas.SetLeft(selection, Math.Min(start.X, p.X)); Canvas.SetTop(selection, Math.Min(start.Y, p.Y)); selection.Width = Math.Abs(p.X - start.X); selection.Height = Math.Abs(p.Y - start.Y); UpdateShades(); };
            MouseLeftButtonUp += (s, e) => { if (!dragging) return; dragging = false; ReleaseMouseCapture(); var end = e.GetPosition(canvas); int x = (int)(Left + Math.Min(start.X, end.X)), y = (int)(Top + Math.Min(start.Y, end.Y)), width = (int)Math.Abs(end.X - start.X), height = (int)Math.Abs(end.Y - start.Y); Hide(); if (width < 1 || height < 1) { Close(); return; } using (var bitmap = new Bitmap(width, height)) using (var graphics = Graphics.FromImage(bitmap)) { graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height)); using (var stream = new MemoryStream()) { bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0; var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); Result = decoder.Frames[0]; Result.Freeze(); } } Close(); }; KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }
        private void UpdateShades()
        {
            if (shades.Length == 0) return;
            double width = Math.Max(0, ActualWidth), height = Math.Max(0, ActualHeight);
            double left = Math.Min(Canvas.GetLeft(selection), Canvas.GetLeft(selection) + selection.Width), top = Math.Min(Canvas.GetTop(selection), Canvas.GetTop(selection) + selection.Height);
            double right = Math.Min(width, left + selection.Width), bottom = Math.Min(height, top + selection.Height);
            left = Math.Max(0, left); top = Math.Max(0, top);
            shades[0].Width = width; shades[0].Height = top;
            shades[1].Width = width; shades[1].Height = Math.Max(0, height - bottom); Canvas.SetTop(shades[1], bottom);
            shades[2].Width = left; shades[2].Height = Math.Max(0, bottom - top); Canvas.SetTop(shades[2], top);
            shades[3].Width = Math.Max(0, width - right); shades[3].Height = Math.Max(0, bottom - top); Canvas.SetLeft(shades[3], right); Canvas.SetTop(shades[3], top);
        }
    }
}
