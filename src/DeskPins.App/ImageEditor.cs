using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskPins.Core;

namespace DeskPins.App
{
    public enum ImageTool { Select, Pen, Text, Number, Erase, Line, Arrow, Rectangle, Fill, Ellipse, Eyedropper }
    public sealed class ImageEditor : FrameworkElement
    {
        private BitmapSource image;
        private readonly Stack<BitmapSource> undo = new Stack<BitmapSource>(), redo = new Stack<BitmapSource>();
        private Point start, last;
        private bool drawing;
        private BitmapSource pasted;
        public bool Locked;
        public ImageTool Tool = ImageTool.Pen;
        public Color PenColor = Colors.Red, BackgroundColor = Color.FromRgb(255, 255, 180);
        public double PenWidth = 3, Scale = 1;
        public int Number = 1;
        public Rect Selection;
        public event Action Changed;
        public BitmapSource Image { get { return image; } }
        public ImageEditor()
        {
            Focusable = true; Cursor = Cursors.Cross;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { drawing = false; pasted = null; ReleaseMouseCapture(); InvalidateVisual(); e.Handled = true; } };
            MouseLeftButtonDown += Down; MouseMove += Move; MouseLeftButtonUp += Up;
            MouseWheel += (s, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Scale = Math.Max(.1, Math.Min(4, Scale * (e.Delta > 0 ? 1.1 : .9))); else PenWidth = Math.Max(1, Math.Min(80, PenWidth + (e.Delta > 0 ? 1 : -1))); InvalidateMeasure(); InvalidateVisual(); e.Handled = true; };
        }
        public void Read(byte[] bytes) { if (bytes != null && bytes.Length > 0) { using (var s = new MemoryStream(bytes)) { var decoder = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); image = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0); image.Freeze(); } } else Blank(250, 160); InvalidateMeasure(); InvalidateVisual(); }
        public void Blank(int width, int height) { var pixels = new byte[width * height * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = BackgroundColor.B; pixels[i + 1] = BackgroundColor.G; pixels[i + 2] = BackgroundColor.R; pixels[i + 3] = 255; } image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); }
        public byte[] Bytes() { using (var s = new MemoryStream()) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(s); return s.ToArray(); } }
        public void Replace(BitmapSource source) { if (Locked || source == null) return; Remember(); image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); image.Freeze(); Selection = Rect.Empty; Notify(); }
        public void PasteInto(BitmapSource source) { if (Locked || source == null) return; pasted = source; Focus(); InvalidateVisual(); }
        public void AddBorder(int width = 3) { if (Locked) return; Remember(); var original = image; Blank(image.PixelWidth + width * 2, image.PixelHeight + width * 2); Draw(d => d.DrawImage(original, new Point(width, width))); }
        public void Undo(bool forward = false) { if (Locked) return; var from = forward ? redo : undo; var to = forward ? undo : redo; if (from.Count == 0) return; to.Push(image); image = from.Pop(); Notify(); }
        public void Crop() { if (Locked || Selection.IsEmpty || Selection.Width < 1 || Selection.Height < 1) return; Remember(); image = new CroppedBitmap(image, new Int32Rect((int)Selection.X, (int)Selection.Y, Math.Min((int)Selection.Width, image.PixelWidth - (int)Selection.X), Math.Min((int)Selection.Height, image.PixelHeight - (int)Selection.Y))); image.Freeze(); Selection = Rect.Empty; Notify(); }
        public void Transform(string action)
        {
            if (Locked) return; Remember();
            if (action == "gray" || action == "blur" || action == "jitter")
            {
                int w = image.PixelWidth, h = image.PixelHeight; byte[] pixels = new byte[w * h * 4]; image.CopyPixels(pixels, w * 4, 0); var original = (byte[])pixels.Clone(); var random = new Random();
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { if (!Selection.IsEmpty && (Selection.Contains(new Point(x, y)) == ((Keyboard.Modifiers & ModifierKeys.Shift) != 0))) continue; int i = (y * w + x) * 4; if (action == "gray") { byte value = (byte)(pixels[i] * .114 + pixels[i + 1] * .587 + pixels[i + 2] * .299); pixels[i] = pixels[i + 1] = pixels[i + 2] = value; } else if (action == "jitter") { int sx = Math.Max(0, Math.Min(w - 1, x + random.Next(-2, 3))), sy = Math.Max(0, Math.Min(h - 1, y + random.Next(-2, 3))); Buffer.BlockCopy(original, (sy * w + sx) * 4, pixels, i, 4); } else for (int channel = 0; channel < 3; channel++) { int sum = 0, count = 0; for (int yy = Math.Max(0, y - 1); yy <= Math.Min(h - 1, y + 1); yy++) for (int xx = Math.Max(0, x - 1); xx <= Math.Min(w - 1, x + 1); xx++) { sum += original[(yy * w + xx) * 4 + channel]; count++; } pixels[i + channel] = (byte)(sum / count); } }
                image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            }
            else { Transform transform = action == "left" ? (Transform)new RotateTransform(-90) : action == "right" ? new RotateTransform(90) : action == "mirror" ? new ScaleTransform(-1, 1) : new ScaleTransform(1, -1); image = new TransformedBitmap(image, transform); }
            image.Freeze(); Selection = Rect.Empty; Notify();
        }
        public void ResizeImage(int width, int height)
        {
            if (Locked) return; if (width < 1 || height < 1 || width > 8192 || height > 8192) throw new ArgumentException("图片尺寸需在 1–8192 范围内。"); Remember(); using (var raster = new RasterCanvas(image, width, height)) image = raster.Result(); image.Freeze(); Selection = Rect.Empty; Notify();
        }
        public void FillSelection(bool pen) { if (Locked) return; Remember(); Draw(d => d.DrawRectangle(new SolidColorBrush(pen ? PenColor : BackgroundColor), null, Selection.IsEmpty ? new Rect(0, 0, image.PixelWidth, image.PixelHeight) : Selection)); }
        public void CopySelection() { Clipboard.SetImage(Selection.IsEmpty ? image : new CroppedBitmap(image, new Int32Rect((int)Selection.X, (int)Selection.Y, Math.Max(1, (int)Selection.Width), Math.Max(1, (int)Selection.Height)))); }
        protected override Size MeasureOverride(Size availableSize) { return image == null ? new Size(250, 160) : new Size(image.PixelWidth * Scale, image.PixelHeight * Scale); }
        protected override void OnRender(DrawingContext d)
        {
            if (image == null) return; d.DrawImage(image, new Rect(0, 0, image.PixelWidth * Scale, image.PixelHeight * Scale));
            if (!Selection.IsEmpty) d.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash }, new Rect(Selection.X * Scale, Selection.Y * Scale, Selection.Width * Scale, Selection.Height * Scale));
            if (pasted != null) d.DrawImage(pasted, new Rect(last.X * Scale, last.Y * Scale, pasted.PixelWidth * Scale, pasted.PixelHeight * Scale));
            if (drawing && Tool != ImageTool.Pen && Tool != ImageTool.Erase) { d.PushTransform(new ScaleTransform(Scale, Scale)); Shape(d, start, last); d.Pop(); }
        }
        private Point Position(MouseEventArgs e) { var p = e.GetPosition(this); return new Point(Math.Max(0, Math.Min(image.PixelWidth - 1, p.X / Scale)), Math.Max(0, Math.Min(image.PixelHeight - 1, p.Y / Scale))); }
        private void Down(object s, MouseButtonEventArgs e)
        {
            Focus(); if (Locked) return; start = last = Position(e); if (pasted != null) { Remember(); var source = pasted; pasted = null; Draw(d => d.DrawImage(source, start)); return; }
            if (Tool == ImageTool.Eyedropper) { byte[] p = new byte[4]; image.CopyPixels(new Int32Rect((int)start.X, (int)start.Y, 1, 1), p, 4, 0); PenColor = Color.FromArgb(p[3], p[2], p[1], p[0]); return; }
            Remember(); if (Tool == ImageTool.Fill) { Flood(start); return; }
            if (Tool == ImageTool.Text) { var text = Ui.Ask("图片文字", "输入文字"); if (text != null) Draw(d => d.DrawText(text, Math.Max(10, PenWidth * 5), new SolidColorBrush(PenColor), start)); return; }
            if (Tool == ImageTool.Number) { Draw(d => { d.DrawEllipse(new SolidColorBrush(PenColor), null, start, Math.Max(10, PenWidth * 4), Math.Max(10, PenWidth * 4)); d.DrawText((Number++).ToString(), Math.Max(10, PenWidth * 5), Brushes.White, new Point(start.X - PenWidth * 2, start.Y - PenWidth * 3)); }); return; }
            drawing = true; CaptureMouse(); if (Tool == ImageTool.Select) Selection = Rect.Empty;
        }
        private void Move(object s, MouseEventArgs e) { if (pasted != null) { last = Position(e); InvalidateVisual(); return; } if (!drawing) return; var p = Position(e); if (Tool == ImageTool.Pen || Tool == ImageTool.Erase) { var a = last; Draw(d => d.DrawLine(new Pen(new SolidColorBrush(Tool == ImageTool.Erase ? BackgroundColor : PenColor), PenWidth), a, p), false); } last = p; if (Tool == ImageTool.Select) Selection = new Rect(start, last); InvalidateVisual(); }
        private void Up(object s, MouseButtonEventArgs e) { if (!drawing) return; last = Position(e); if (Tool != ImageTool.Select && Tool != ImageTool.Pen && Tool != ImageTool.Erase) Draw(d => Shape(d, start, last), false); drawing = false; ReleaseMouseCapture(); Notify(); }
        private void Shape(DrawingContext d, Point a, Point b)
        {
            var pen = new Pen(new SolidColorBrush(PenColor), PenWidth); bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0; bool fill = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (Tool == ImageTool.Line || Tool == ImageTool.Arrow) { if (shift) { if (Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y)) b.Y = a.Y; else b.X = a.X; } d.DrawLine(pen, a, b); if (Tool == ImageTool.Arrow) { var v = a - b; if (v.Length > 0) { v.Normalize(); var normal = new Vector(-v.Y, v.X); d.DrawLine(pen, b, b + v * 15 + normal * 7); d.DrawLine(pen, b, b + v * 15 - normal * 7); } } }
            else { if (shift) { double size = Math.Min(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)); b = new Point(a.X + Math.Sign(b.X - a.X) * size, a.Y + Math.Sign(b.Y - a.Y) * size); } var r = new Rect(a, b); if (Tool == ImageTool.Rectangle) d.DrawRectangle(fill ? pen.Brush : null, pen, r); else if (Tool == ImageTool.Ellipse) d.DrawEllipse(fill ? pen.Brush : null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); }
        }
        private void Shape(RasterCanvas d, Point a, Point b)
        {
            var pen = new Pen(new SolidColorBrush(PenColor), PenWidth); bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0; bool fill = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (Tool == ImageTool.Line || Tool == ImageTool.Arrow) { if (shift) { if (Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y)) b.Y = a.Y; else b.X = a.X; } d.DrawLine(pen, a, b); if (Tool == ImageTool.Arrow) { var v = a - b; if (v.Length > 0) { v.Normalize(); var normal = new Vector(-v.Y, v.X); d.DrawLine(pen, b, b + v * 15 + normal * 7); d.DrawLine(pen, b, b + v * 15 - normal * 7); } } }
            else { if (shift) { double size = Math.Min(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)); b = new Point(a.X + Math.Sign(b.X - a.X) * size, a.Y + Math.Sign(b.Y - a.Y) * size); } var r = new Rect(a, b); if (Tool == ImageTool.Rectangle) d.DrawRectangle(fill ? pen.Brush : null, pen, r); else if (Tool == ImageTool.Ellipse) d.DrawEllipse(fill ? pen.Brush : null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); }
        }
        private void Flood(Point p)
        {
            int w = image.PixelWidth, h = image.PixelHeight; byte[] pixels = new byte[w * h * 4]; image.CopyPixels(pixels, w * 4, 0); int origin = ((int)p.Y * w + (int)p.X) * 4; byte b = pixels[origin], g = pixels[origin + 1], r = pixels[origin + 2], a = pixels[origin + 3]; if (b == PenColor.B && g == PenColor.G && r == PenColor.R && a == PenColor.A) return;
            var queue = new Queue<int>(); queue.Enqueue(origin / 4); while (queue.Count > 0) { int point = queue.Dequeue(), i = point * 4; if (pixels[i] != b || pixels[i + 1] != g || pixels[i + 2] != r || pixels[i + 3] != a) continue; pixels[i] = PenColor.B; pixels[i + 1] = PenColor.G; pixels[i + 2] = PenColor.R; pixels[i + 3] = PenColor.A; int x = point % w, y = point / w; if (x > 0) queue.Enqueue(point - 1); if (x < w - 1) queue.Enqueue(point + 1); if (y > 0) queue.Enqueue(point - w); if (y < h - 1) queue.Enqueue(point + w); }
            image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4); image.Freeze(); Notify();
        }
        private void Draw(Action<RasterCanvas> action, bool notify = true) { using (var raster = new RasterCanvas(image)) { action(raster); image = raster.Result(); } if (notify) Notify(); else InvalidateVisual(); }
        private void Remember() { undo.Push(image); redo.Clear(); var all = undo.ToArray(); long bytes = 0; int keep = 0; for (int i = 0; i < all.Length && i < 40; i++) { bytes += (long)all[i].PixelWidth * all[i].PixelHeight * 4; if (i > 0 && bytes > 64L * 1024 * 1024) break; keep++; } undo.Clear(); for (int i = keep - 1; i >= 0; i--) undo.Push(all[i]); }
        private void Notify() { InvalidateMeasure(); InvalidateVisual(); var changed = Changed; if (changed != null) changed(); }
    }
}
