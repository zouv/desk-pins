using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPins.App
{
    internal sealed class RasterCanvas : IDisposable
    {
        private readonly Bitmap bitmap;
        private readonly Graphics graphics;
        public RasterCanvas(BitmapSource source, int width = 0, int height = 0)
        {
            width = width == 0 ? source.PixelWidth : width; height = height == 0 ? source.PixelHeight : height; bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb); graphics = Graphics.FromImage(bitmap); graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var src = new Bitmap(source.PixelWidth, source.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                var data = src.LockBits(new System.Drawing.Rectangle(0, 0, src.Width, src.Height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb); try { byte[] pixels = new byte[source.PixelWidth * source.PixelHeight * 4]; var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); converted.CopyPixels(pixels, source.PixelWidth * 4, 0); for (int y = 0; y < src.Height; y++) Marshal.Copy(pixels, y * src.Width * 4, IntPtr.Add(data.Scan0, y * data.Stride), src.Width * 4); } finally { src.UnlockBits(data); }
                graphics.DrawImage(src, new System.Drawing.Rectangle(0, 0, width, height));
            }
        }
        public BitmapSource Result()
        {
            graphics.Flush(); byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4]; var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb); try { for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * bitmap.Width * 4, bitmap.Width * 4); } finally { bitmap.UnlockBits(data); }
            var result = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, pixels, bitmap.Width * 4); result.Freeze(); return result;
        }
        private static System.Drawing.Color Color(System.Windows.Media.Brush brush) { var b = brush as SolidColorBrush; var c = b == null ? Colors.Black : b.Color; return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B); }
        private static System.Drawing.Pen Pen(System.Windows.Media.Pen pen) { var result = new System.Drawing.Pen(Color(pen.Brush), (float)pen.Thickness); result.StartCap = result.EndCap = System.Drawing.Drawing2D.LineCap.Round; return result; }
        public void DrawLine(System.Windows.Media.Pen pen, System.Windows.Point a, System.Windows.Point b) { using (var p = Pen(pen)) graphics.DrawLine(p, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y); }
        public void DrawRectangle(System.Windows.Media.Brush fill, System.Windows.Media.Pen pen, Rect r) { if (fill != null) using (var b = new SolidBrush(Color(fill))) graphics.FillRectangle(b, (float)r.X, (float)r.Y, (float)r.Width, (float)r.Height); if (pen != null) using (var p = Pen(pen)) graphics.DrawRectangle(p, (float)r.X, (float)r.Y, (float)r.Width, (float)r.Height); }
        public void DrawEllipse(System.Windows.Media.Brush fill, System.Windows.Media.Pen pen, System.Windows.Point center, double rx, double ry) { float x = (float)(center.X - rx), y = (float)(center.Y - ry), w = (float)(rx * 2), h = (float)(ry * 2); if (fill != null) using (var b = new SolidBrush(Color(fill))) graphics.FillEllipse(b, x, y, w, h); if (pen != null) using (var p = Pen(pen)) graphics.DrawEllipse(p, x, y, w, h); }
        public void DrawImage(BitmapSource source, System.Windows.Point point) { using (var image = new RasterCanvas(source)) { graphics.DrawImage(image.bitmap, (float)point.X, (float)point.Y); } }
        public void DrawText(string text, double size, System.Windows.Media.Brush brush, System.Windows.Point location) { using (var font = new System.Drawing.Font("Segoe UI", (float)size, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel)) using (var b = new SolidBrush(Color(brush))) graphics.DrawString(text, font, b, (float)location.X, (float)location.Y); }
        public void Dispose() { graphics.Dispose(); bitmap.Dispose(); }
    }
}
