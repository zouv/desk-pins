using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DeskPins.App
{
    public static class Ui
    {
        private static Dictionary<string, string> language = new Dictionary<string, string>();
        public static string CurrentLanguage { get; private set; } = Localization.Chinese;
        public static void LoadLanguage(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || file == Localization.Chinese) { language = new Dictionary<string, string>(); CurrentLanguage = Localization.Chinese; }
            else if (file == Localization.English) { language = Localization.Dictionary(Localization.English); CurrentLanguage = Localization.English; }
            else { language = LoadLanguageDictionary(file); CurrentLanguage = file; }
        }
        private static Dictionary<string, string> LoadLanguageDictionary(string file) { using (var stream = System.IO.File.OpenRead(file)) return (Dictionary<string, string>)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Dictionary<string, string>), new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream); }
        public static string Text(string text) { if (string.IsNullOrEmpty(text)) return text; string translated; return language.TryGetValue(text, out translated) ? translated : text; }
        private static readonly Dictionary<string, Brush> brushCache = new Dictionary<string, Brush>();
        private static readonly DropShadowEffect noteShadow = CreateNoteShadow();
        private static DropShadowEffect CreateNoteShadow() { var effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Direction = 270, Opacity = .32, Color = Colors.Black }; effect.Freeze(); return effect; }
        public static Effect NoteShadow(bool enabled) { return enabled ? noteShadow : null; }
        public static Brush Brush(string color) { Brush cached; if (color != null && brushCache.TryGetValue(color, out cached)) return cached; try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); brushCache[color] = b; return b; } catch { return Brushes.LightYellow; } }
        public static Brush TextOn(string color) { try { var c = (Color)ColorConverter.ConvertFromString(color); return .299 * c.R + .587 * c.G + .114 * c.B > 150 ? Brushes.Black : Brushes.White; } catch { return Brushes.Black; } }
        public static Button Button(string text, Action click, double width = double.NaN) { var b = new Button { Content = Text(text), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4), MinHeight = 28, Width = width }; b.Click += (s, e) => { try { click(); } catch (Exception ex) { Error(ex); } }; return b; }
        public static void FitDialog(Window window, double minWidth = 360, double minHeight = 240) { var area = SystemParameters.WorkArea; window.MaxWidth = area.Width; window.MaxHeight = area.Height; window.MinWidth = Math.Min(minWidth, area.Width); window.MinHeight = Math.Min(minHeight, area.Height); window.UseLayoutRounding = true; window.SnapsToDevicePixels = true; if (!window.ShowInTaskbar && Application.Current != null) foreach (Window candidate in Application.Current.Windows) if (candidate != window && candidate.IsActive && candidate.IsVisible) { window.Owner = candidate; window.WindowStartupLocation = WindowStartupLocation.CenterOwner; break; } }
        public static MenuItem Item(string text, Action click, string shortcut = "", bool? check = null)
        {
            var m = new MenuItem { Header = Text(text), InputGestureText = shortcut, IsCheckable = check.HasValue, IsChecked = check ?? false };
            m.Click += (s, e) => { e.Handled = true; try { click(); } catch (Exception ex) { Error(ex); } }; return m;
        }
        public static MenuItem Sub(string text, params object[] items) { var m = new MenuItem { Header = Text(text) }; foreach (var i in items) m.Items.Add(i); return m; }
        public static void Error(Exception ex) { MessageBox.Show(Text(ex.Message), "DeskPins", MessageBoxButton.OK, MessageBoxImage.Warning); }
        public static string Ask(string title, string label, string initial = "", bool password = false)
        {
            var window = new Window { Title = Text(title), Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
            FitDialog(window, 360, 0);
            var panel = new StackPanel { Margin = new Thickness(16) }; panel.Children.Add(new TextBlock { Text = Text(label), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            TextBox text = null; PasswordBox secret = null; if (password) { secret = new PasswordBox { MinWidth = 300, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(secret); } else { text = new TextBox { Text = initial, MinWidth = 300, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(text); }
            string result = null; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = Button("确定", () => { result = password ? secret.Password : text.Text; window.DialogResult = true; }); ok.IsDefault = true; buttons.Children.Add(ok); var cancel = Button("取消", () => window.Close()); cancel.IsCancel = true; buttons.Children.Add(cancel); panel.Children.Add(buttons); window.Content = panel;
            window.Loaded += (s, e) => window.Dispatcher.BeginInvoke(new Action(() => { if (password) { secret.Focus(); secret.SelectAll(); } else { text.Focus(); text.SelectAll(); } }), System.Windows.Threading.DispatcherPriority.Input); window.ShowDialog(); return result;
        }
        public static bool Confirm(string message) { return MessageBox.Show(Text(message), "DeskPins", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes; }
        private static string TranslateFilter(string filter) { if (string.IsNullOrEmpty(filter)) return filter; var parts = filter.Split('|'); for (int i = 0; i < parts.Length; i += 2) parts[i] = Text(parts[i]); return string.Join("|", parts); }
        public static string OpenFile(string filter) { var d = new Microsoft.Win32.OpenFileDialog { Filter = TranslateFilter(filter) }; return d.ShowDialog() == true ? d.FileName : null; }
        public static string SaveFile(string filter, string name = "") { var d = new Microsoft.Win32.SaveFileDialog { Filter = TranslateFilter(filter), FileName = name }; return d.ShowDialog() == true ? d.FileName : null; }
        public static string Color(string initial)
        {
            using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(initial) }) return d.ShowDialog() == System.Windows.Forms.DialogResult.OK ? "#" + d.Color.R.ToString("X2") + d.Color.G.ToString("X2") + d.Color.B.ToString("X2") : null;
        }
        public static void Field(Panel panel, string label, FrameworkElement control) { panel.Children.Add(new TextBlock { Text = Text(label), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 2, 4) }); System.Windows.Automation.AutomationProperties.SetName(control, Text(label)); control.Margin = new Thickness(2); if (control is TextBox) ((TextBox)control).Padding = new Thickness(5); panel.Children.Add(control); }
        public static void ValidateLanguage(string file) { if (string.IsNullOrWhiteSpace(file) || file == Localization.Chinese || file == Localization.English) return; LoadLanguageDictionary(file); }
        public static bool TryParseGesture(string text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None; modifiers = ModifierKeys.None; if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Trim().Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModifierKeys.Control; continue; }
                if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModifierKeys.Alt; continue; }
                if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModifierKeys.Shift; continue; }
                if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "Windows", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModifierKeys.Windows; continue; }
                if (i != parts.Length - 1) return false;
                if (part.Length == 1 && part[0] >= '0' && part[0] <= '9') key = Key.D0 + (part[0] - '0');
                else if (string.Equals(part, "Enter", StringComparison.OrdinalIgnoreCase)) key = Key.Return;
                else if (string.Equals(part, "Esc", StringComparison.OrdinalIgnoreCase)) key = Key.Escape;
                else if (string.Equals(part, "Backspace", StringComparison.OrdinalIgnoreCase)) key = Key.Back;
                else
                {
                    try { key = (Key)new KeyConverter().ConvertFromString(part); }
                    catch { return false; }
                }
            }
            return key != Key.None;
        }
        public static string FormatGesture(Key key, ModifierKeys modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            string name = key == Key.Return ? "Enter" : key == Key.Escape ? "Escape" : key == Key.Back ? "Backspace" : key >= Key.D0 && key <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : new KeyConverter().ConvertToString(key);
            if (string.IsNullOrWhiteSpace(name)) return null;
            parts.Add(name);
            return string.Join("+", parts);
        }
        public static double Number(string s, double fallback) { double n; return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback; }
    }
}
