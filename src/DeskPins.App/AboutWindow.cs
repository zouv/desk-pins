using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPins.App
{
    /// <summary>About panel: product identity first, then version/author and a two-line summary.</summary>
    public sealed class AboutWindow : Window
    {
        internal static string VersionText => typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "未知";

        public AboutWindow()
        {
            Title = Ui.Text("关于 DeskPins");
            Width = 480; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Background = Brushes.White;
            Ui.FitDialog(this, 440, 0);

            var grid = new Grid { Margin = new Thickness(32, 26, 32, 22) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var icon = new Image { Source = LoadIcon(), Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(icon, 0); Grid.SetRow(icon, 0); Grid.SetRowSpan(icon, 3); grid.Children.Add(icon);

            var name = new TextBlock
            {
                Text = "DeskPins", FontSize = 22, FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Brush("#1F2937"), VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(name, 2); Grid.SetRow(name, 0); grid.Children.Add(name);

            var meta = new TextBlock
            {
                Name = "aboutVersion", Text = string.Format(Ui.Text("版本 {0} · 作者 @zouv"), VersionText),
                FontSize = 12.5, Foreground = Ui.Brush("#6B7280"), Margin = new Thickness(0, 6, 0, 0)
            };
            Grid.SetColumn(meta, 2); Grid.SetRow(meta, 1); grid.Children.Add(meta);

            var description = new TextBlock
            {
                Text = Ui.Text("轻量桌面便签工具，支持文本与图片便签。\n提供堆叠、提醒、备份恢复与多语言界面。"),
                FontSize = 13, Foreground = Ui.Brush("#374151"), TextWrapping = TextWrapping.Wrap,
                LineHeight = 20, Margin = new Thickness(0, 14, 0, 0)
            };
            Grid.SetColumn(description, 2); Grid.SetRow(description, 2); grid.Children.Add(description);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            var close = Ui.Button("关闭", Close); close.IsDefault = true; close.IsCancel = true; buttons.Children.Add(close);
            Grid.SetColumn(buttons, 2); Grid.SetRow(buttons, 3); grid.Children.Add(buttons);

            Content = grid;
            Icon = LoadIcon();
        }

        private static ImageSource LoadIcon()
        {
            using (var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("DeskPins.Icon.ico"))
                return stream == null ? null : BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
    }
}
