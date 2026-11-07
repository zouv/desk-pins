using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskPins.App
{
    public static class Ui
    {
        private static Dictionary<string, string> language = new Dictionary<string, string>();
        public static void LoadLanguage(string file) { language = string.IsNullOrWhiteSpace(file) || file == "zh-CN" ? new Dictionary<string, string>() : LoadLanguageDictionary(file); }
        private static Dictionary<string, string> LoadLanguageDictionary(string file) { using (var stream = System.IO.File.OpenRead(file)) return (Dictionary<string, string>)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Dictionary<string, string>), new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream); }
        public static string Text(string text) { string translated; return language.TryGetValue(text, out translated) ? translated : text; }
        private static readonly Dictionary<string, Brush> brushCache = new Dictionary<string, Brush>();
        public static Brush Brush(string color) { Brush cached; if (color != null && brushCache.TryGetValue(color, out cached)) return cached; try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); brushCache[color] = b; return b; } catch { return Brushes.LightYellow; } }
        public static Button Button(string text, Action click, double width = double.NaN) { var b = new Button { Content = Text(text), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4), MinHeight = 28, Width = width }; b.Click += (s, e) => { try { click(); } catch (Exception ex) { Error(ex); } }; return b; }
        public static void FitDialog(Window window, double minWidth = 360, double minHeight = 240) { var area = SystemParameters.WorkArea; window.MaxWidth = area.Width; window.MaxHeight = area.Height; window.MinWidth = Math.Min(minWidth, area.Width); window.MinHeight = Math.Min(minHeight, area.Height); window.UseLayoutRounding = true; window.SnapsToDevicePixels = true; if (!window.ShowInTaskbar && Application.Current != null) foreach (Window candidate in Application.Current.Windows) if (candidate != window && candidate.IsActive && candidate.IsVisible) { window.Owner = candidate; window.WindowStartupLocation = WindowStartupLocation.CenterOwner; break; } }
        public static MenuItem Item(string text, Action click, string shortcut = "", bool? check = null)
        {
            var m = new MenuItem { Header = Text(text), InputGestureText = shortcut, IsCheckable = check.HasValue, IsChecked = check ?? false };
            m.Click += (s, e) => { e.Handled = true; try { click(); } catch (Exception ex) { Error(ex); } }; return m;
        }
        public static MenuItem Sub(string text, params object[] items) { var m = new MenuItem { Header = Text(text) }; foreach (var i in items) m.Items.Add(i); return m; }
        public static void Error(Exception ex) { MessageBox.Show(ex.Message, "DeskPins", MessageBoxButton.OK, MessageBoxImage.Warning); }
        public static string Ask(string title, string label, string initial = "", bool password = false)
        {
            var window = new Window { Title = title, Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
            FitDialog(window, 360, 0);
            var panel = new StackPanel { Margin = new Thickness(16) }; panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            TextBox text = null; PasswordBox secret = null; if (password) { secret = new PasswordBox { MinWidth = 300, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(secret); } else { text = new TextBox { Text = initial, MinWidth = 300, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(text); }
            string result = null; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = Button("确定", () => { result = password ? secret.Password : text.Text; window.DialogResult = true; }); ok.IsDefault = true; buttons.Children.Add(ok); var cancel = Button("取消", () => window.Close()); cancel.IsCancel = true; buttons.Children.Add(cancel); panel.Children.Add(buttons); window.Content = panel;
            window.Loaded += (s, e) => { if (password) secret.Focus(); else { text.Focus(); text.SelectAll(); } }; window.ShowDialog(); return result;
        }
        public static bool Confirm(string message) { return MessageBox.Show(message, "DeskPins", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes; }
        public static string OpenFile(string filter) { var d = new Microsoft.Win32.OpenFileDialog { Filter = filter }; return d.ShowDialog() == true ? d.FileName : null; }
        public static string SaveFile(string filter, string name = "") { var d = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = name }; return d.ShowDialog() == true ? d.FileName : null; }
        public static string Color(string initial)
        {
            using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(initial) }) return d.ShowDialog() == System.Windows.Forms.DialogResult.OK ? "#" + d.Color.R.ToString("X2") + d.Color.G.ToString("X2") + d.Color.B.ToString("X2") : null;
        }
        public static void Field(Panel panel, string label, Control control) { panel.Children.Add(new TextBlock { Text = Text(label), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 2, 4) }); System.Windows.Automation.AutomationProperties.SetName(control, Text(label)); control.Margin = new Thickness(2); if (control is TextBox) ((TextBox)control).Padding = new Thickness(5); panel.Children.Add(control); }
        public static void ValidateLanguage(string file) { if (!string.IsNullOrWhiteSpace(file) && file != "zh-CN") LoadLanguageDictionary(file); }
        public static double Number(string s, double fallback) { double n; return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback; }
    }
}
