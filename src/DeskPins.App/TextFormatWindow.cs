using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DeskPins.App
{
    public sealed class TextFormatWindow : Window
    {
        private sealed class FontChoice
        {
            public string Source { get; private set; }
            public FontFamily Family { get; private set; }
            public FontChoice(string source, FontFamily family) { Source = source; Family = family; }
        }

        private readonly TextEditor editor;
        private readonly TextRange target;
        private readonly bool hadSelection;
        private readonly ComboBox fontBox;
        private readonly TextBox sizeBox;
        private readonly CheckBox boldBox, italicBox, underlineBox, strikeBox;
        private readonly Button foregroundButton, highlightButton;
        private readonly TextBlock preview, errorText;
        private bool fontChanged, sizeChanged, foregroundChanged, highlightChanged;
        private bool boldChanged, italicChanged, underlineChanged, strikeChanged;
        private string foreground = "#000000", highlight;

        public TextFormatWindow(TextEditor editor)
        {
            this.editor = editor;
            hadSelection = !editor.Selection.IsEmpty;
            target = hadSelection ? new TextRange(editor.Selection.Start, editor.Selection.End) : new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
            Title = Ui.Text(hadSelection ? "设置所选文字格式" : "设置全文格式");
            Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Ui.FitDialog(this, 480, 0);

            object fontValue = Value(TextElement.FontFamilyProperty);
            object sizeValue = Value(TextElement.FontSizeProperty);
            var fonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source).Select(f => new FontChoice(f.Source, f)).ToList();
            if (fontValue == DependencyProperty.UnsetValue) fonts.Insert(0, new FontChoice(Ui.Text("多个值"), null));
            fontBox = new ComboBox { ItemsSource = fonts, DisplayMemberPath = "Source", MinHeight = 30, MinWidth = 260 };
            fontBox.SelectedItem = fontValue is FontFamily ? fonts.FirstOrDefault(f => f.Source == ((FontFamily)fontValue).Source) ?? fonts.FirstOrDefault() : fonts.FirstOrDefault();
            AutomationProperties.SetName(fontBox, Ui.Text("字体"));

            sizeBox = new TextBox { Width = 90, MinHeight = 30, Padding = new Thickness(6, 3, 6, 3), TextAlignment = TextAlignment.Right, Text = sizeValue is double ? Math.Max(1, Math.Round((double)sizeValue, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) : "" };
            AutomationProperties.SetName(sizeBox, Ui.Text("字号（px）"));
            var sizePanel = new StackPanel { Orientation = Orientation.Horizontal };
            sizePanel.Children.Add(sizeBox);
            sizePanel.Children.Add(new TextBlock { Text = sizeValue == DependencyProperty.UnsetValue ? Ui.Text("多个值；输入整数可统一") : "1–200 px", Foreground = UiTheme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });

            boldBox = Toggle("粗体", FontWeightValue(TextElement.FontWeightProperty));
            italicBox = Toggle("斜体", Value(TextElement.FontStyleProperty) is FontStyle ? (bool?)(((FontStyle)Value(TextElement.FontStyleProperty)) == FontStyles.Italic) : null);
            underlineBox = Toggle("下划线", DecorationValue(TextDecorations.Underline[0]));
            strikeBox = Toggle("删除线", DecorationValue(TextDecorations.Strikethrough[0]));
            var styles = new WrapPanel();
            styles.Children.Add(boldBox); styles.Children.Add(italicBox); styles.Children.Add(underlineBox); styles.Children.Add(strikeBox);

            foregroundButton = ColorButton("文字颜色", Value(TextElement.ForegroundProperty) as SolidColorBrush);
            highlightButton = ColorButton("高亮颜色", Value(TextElement.BackgroundProperty) as SolidColorBrush);
            var clearHighlight = Ui.Button("清除高亮", () => { highlight = null; highlightChanged = true; SetButtonColor(highlightButton, null, Ui.Text("已清除")); UpdatePreview(); });
            var highlightPanel = new StackPanel { Orientation = Orientation.Horizontal };
            highlightPanel.Children.Add(highlightButton); highlightPanel.Children.Add(clearHighlight);

            preview = new TextBlock { Text = "DeskPins · Quick brown fox · 文字格式 123", Padding = new Thickness(12), TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
            errorText = new TextBlock { Foreground = UiTheme.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(grid, "字体", fontBox);
            AddRow(grid, "字号（px）", sizePanel);
            AddRow(grid, "文字样式", styles);
            AddRow(grid, "文字颜色", foregroundButton);
            AddRow(grid, "高亮颜色", highlightPanel);
            AddRow(grid, "预览", new Border { Child = preview, Background = UiTheme.Background, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1) });
            AddRow(grid, "", new TextBlock { Text = Ui.Text("混合格式保持原样；复选框第三态也表示不修改。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap });
            AddRow(grid, "", errorText);

            var ok = Ui.PrimaryButton("确定", () => { if (ApplyChanges()) DialogResult = true; }); ok.IsDefault = true;
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true;
            Ui.DialogContent(this, grid, ok, cancel);

            fontBox.SelectionChanged += (s, e) => { fontChanged = true; UpdatePreview(); };
            sizeBox.TextChanged += (s, e) => { sizeChanged = true; errorText.Text = ""; UpdatePreview(); };
            boldBox.Click += (s, e) => { boldChanged = true; UpdatePreview(); };
            italicBox.Click += (s, e) => { italicChanged = true; UpdatePreview(); };
            underlineBox.Click += (s, e) => { underlineChanged = true; UpdatePreview(); };
            strikeBox.Click += (s, e) => { strikeChanged = true; UpdatePreview(); };
            foregroundButton.Click += (s, e) => ChooseColor(true);
            highlightButton.Click += (s, e) => ChooseColor(false);
            UpdatePreview();
        }

        private object Value(DependencyProperty property) { return target.GetPropertyValue(property); }
        private bool? FontWeightValue(DependencyProperty property) { object value = Value(property); return value is FontWeight ? (bool?)(((FontWeight)value) == FontWeights.Bold) : null; }
        private bool? DecorationValue(TextDecoration decoration) { var value = Value(Inline.TextDecorationsProperty) as TextDecorationCollection; return value == null && Value(Inline.TextDecorationsProperty) == DependencyProperty.UnsetValue ? null : (bool?)(value != null && value.Any(d => d.Location == decoration.Location)); }
        private static CheckBox Toggle(string label, bool? value) { var box = new CheckBox { Content = Ui.Text(label), IsThreeState = true, IsChecked = value, Margin = new Thickness(0, 4, 16, 4), VerticalAlignment = VerticalAlignment.Center }; AutomationProperties.SetName(box, Ui.Text(label)); return box; }

        private static Button ColorButton(string label, SolidColorBrush brush)
        {
            var button = new Button { Style = UiTheme.ButtonStyle(), Width = 100, Height = 30, Margin = new Thickness(0, 2, 8, 2), Padding = new Thickness(6, 2, 6, 2) };
            AutomationProperties.SetName(button, Ui.Text(label));
            SetButtonColor(button, brush, brush == null ? Ui.Text("多个值 / 无") : null);
            return button;
        }

        private static void SetButtonColor(Button button, SolidColorBrush brush, string text)
        {
            button.Background = brush ?? UiTheme.Surface;
            button.Foreground = brush == null ? UiTheme.Text : Ui.TextOn("#" + brush.Color.R.ToString("X2") + brush.Color.G.ToString("X2") + brush.Color.B.ToString("X2"));
            button.Content = text ?? "";
        }

        private static string Hex(SolidColorBrush brush) { return brush == null ? "#000000" : "#" + brush.Color.R.ToString("X2") + brush.Color.G.ToString("X2") + brush.Color.B.ToString("X2"); }

        private void ChooseColor(bool textColor)
        {
            string selected = Ui.Color(textColor ? foreground : highlight ?? "#FFFF00");
            if (selected == null) return;
            if (textColor) { foreground = selected; foregroundChanged = true; SetButtonColor(foregroundButton, Ui.Brush(selected) as SolidColorBrush, null); }
            else { highlight = selected; highlightChanged = true; SetButtonColor(highlightButton, Ui.Brush(selected) as SolidColorBrush, null); }
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var choice = fontBox.SelectedItem as FontChoice;
            if (choice != null && choice.Family != null) preview.FontFamily = choice.Family;
            else preview.FontFamily = Value(TextElement.FontFamilyProperty) as FontFamily ?? editor.Document.FontFamily;
            int size;
            if (int.TryParse(sizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out size) && size > 0) preview.FontSize = size;
            else preview.FontSize = Value(TextElement.FontSizeProperty) is double ? (double)Value(TextElement.FontSizeProperty) : editor.Document.FontSize;
            preview.FontWeight = boldBox.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            preview.FontStyle = italicBox.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            preview.TextDecorations = underlineBox.IsChecked == true ? TextDecorations.Underline : strikeBox.IsChecked == true ? TextDecorations.Strikethrough : null;
            preview.Foreground = foregroundChanged ? Ui.Brush(foreground) : Value(TextElement.ForegroundProperty) as Brush ?? editor.Foreground;
            preview.Background = highlightChanged ? (highlight == null ? Brushes.Transparent : Ui.Brush(highlight)) : Value(TextElement.BackgroundProperty) as Brush ?? Brushes.Transparent;
        }

        public bool ApplyChanges()
        {
            int size = 0;
            if (sizeChanged && (!int.TryParse(sizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 1 || size > 200))
            {
                errorText.Text = Ui.Text("字号必须是 1–200 之间的整数。");
                sizeBox.Focus(); sizeBox.SelectAll(); return false;
            }
            if (fontChanged)
            {
                var choice = fontBox.SelectedItem as FontChoice;
                if (choice != null && choice.Family != null) target.ApplyPropertyValue(TextElement.FontFamilyProperty, choice.Family);
            }
            if (sizeChanged) target.ApplyPropertyValue(TextElement.FontSizeProperty, (double)size);
            if (boldChanged && boldBox.IsChecked.HasValue) target.ApplyPropertyValue(TextElement.FontWeightProperty, boldBox.IsChecked.Value ? FontWeights.Bold : FontWeights.Normal);
            if (italicChanged && italicBox.IsChecked.HasValue) target.ApplyPropertyValue(TextElement.FontStyleProperty, italicBox.IsChecked.Value ? FontStyles.Italic : FontStyles.Normal);
            if (underlineChanged && underlineBox.IsChecked.HasValue) ApplyDecoration(underlineBox.IsChecked.Value, TextDecorations.Underline);
            if (strikeChanged && strikeBox.IsChecked.HasValue) ApplyDecoration(strikeBox.IsChecked.Value, TextDecorations.Strikethrough);
            if (foregroundChanged) target.ApplyPropertyValue(TextElement.ForegroundProperty, Ui.Brush(foreground));
            if (highlightChanged) target.ApplyPropertyValue(TextElement.BackgroundProperty, highlight == null ? null : Ui.Brush(highlight));
            if (hadSelection) editor.Selection.Select(target.Start, target.End);
            return true;
        }

        private void ApplyDecoration(bool enabled, TextDecorationCollection decoration)
        {
            var segments = new System.Collections.Generic.List<Tuple<TextPointer, TextPointer>>();
            var position = target.Start;
            while (position.CompareTo(target.End) < 0)
            {
                TextPointer next;
                if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    next = position.GetPositionAtOffset(position.GetTextInRun(LogicalDirection.Forward).Length) ?? target.End;
                    if (next.CompareTo(target.End) > 0) next = target.End;
                    if (next.CompareTo(position) > 0) segments.Add(Tuple.Create(position, next));
                }
                else next = position.GetNextContextPosition(LogicalDirection.Forward) ?? target.End;
                if (next.CompareTo(position) <= 0) break;
                position = next;
            }
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                var current = segments[i].Item1.Parent.GetValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
                var updated = current == null ? new TextDecorationCollection() : new TextDecorationCollection(current.Where(d => d.Location != decoration[0].Location));
                if (enabled) updated.Add(decoration[0]);
                new TextRange(segments[i].Item1, segments[i].Item2).ApplyPropertyValue(Inline.TextDecorationsProperty, updated.Count == 0 ? null : updated);
            }
        }

        private static void AddRow(Grid grid, string label, FrameworkElement control)
        {
            int row = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = Ui.Text(label), Foreground = UiTheme.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 7, 12, 7) };
            control.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(text, row); Grid.SetColumn(text, 0); grid.Children.Add(text);
            Grid.SetRow(control, row); Grid.SetColumn(control, 1); grid.Children.Add(control);
        }
    }
}
