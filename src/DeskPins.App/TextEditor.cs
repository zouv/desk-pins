using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class TextEditor : RichTextBox
    {
        private static readonly DependencyProperty[] formatProperties = { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.FontStyleProperty, TextElement.ForegroundProperty, Inline.TextDecorationsProperty };
        private static object[] copiedFormat;
        public void CopyFormat() { copiedFormat = formatProperties.Select(p => Selection.GetPropertyValue(p)).ToArray(); }
        public void PasteFormat() { if (IsReadOnly || copiedFormat == null) return; for (int i = 0; i < formatProperties.Length; i++) if (copiedFormat[i] != DependencyProperty.UnsetValue) Selection.ApplyPropertyValue(formatProperties[i], copiedFormat[i]); }
        public void Quote() { if (IsReadOnly) return; var range = new TextRange(Document.ContentStart, Document.ContentEnd); range.Text = string.Join("\n", range.Text.TrimEnd('\r', '\n').Replace("\r\n", "\n").Split('\n').Select(line => "> " + line)) + "\n\n"; CaretPosition = Document.ContentEnd; }
        public void DeleteLine() { if (IsReadOnly) return; var start = CaretPosition.GetLineStartPosition(0); if (start == null) return; var end = start.GetLineStartPosition(1) ?? Document.ContentEnd; new TextRange(start, end).Text = ""; }
        public TextEditor()
        {
            BorderThickness = new Thickness(0); Padding = new Thickness(7, 3, 7, 5); AcceptsTab = true; IsDocumentEnabled = true;
            Document = new FlowDocument { PagePadding = new Thickness(0) };
            AddHandler(Hyperlink.RequestNavigateEvent, new System.Windows.Navigation.RequestNavigateEventHandler((s, e) => { OpenLink(e.Uri); e.Handled = true; }));
            PreviewMouseDoubleClick += DoubleClick;
        }
        public void Read(Note n)
        {
            Document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily(n.FontName), FontSize = n.FontSize };
            var range = new TextRange(Document.ContentStart, Document.ContentEnd);
            if (n.Body != null && n.Body.Length > 0) { using (var s = new MemoryStream(n.Body)) { try { range.Load(s, DataFormats.Rtf); } catch (ArgumentException) { range.Text = n.PlainText ?? Encoding.UTF8.GetString(n.Body); } } }
            else range.Text = n.PlainText ?? "";
            Foreground = Ui.Brush(n.Foreground); IsReadOnly = n.Locked;
        }
        public void Write(Note n)
        {
            var range = new TextRange(Document.ContentStart, Document.ContentEnd);
            using (var s = new MemoryStream()) { range.Save(s, DataFormats.Rtf); n.Body = s.ToArray(); }
            n.PlainText = range.Text.TrimEnd('\r', '\n');
        }
        public void InsertText(string text) { if (IsReadOnly) return; Selection.Text = text; CaretPosition = Selection.End; }
        public void InsertImage(BitmapSource image)
        {
            if (IsReadOnly || image == null) return;
            var element = new Image { Source = image, Width = Math.Min(400, image.Width), Stretch = Stretch.Uniform };
            new InlineUIContainer(element, CaretPosition); CaretPosition = CaretPosition.GetNextInsertionPosition(LogicalDirection.Forward) ?? Document.ContentEnd;
        }
        public void Link()
        {
            if (IsReadOnly) return; string label = Ui.Ask("插入链接", "显示文字", Selection.Text); if (label == null) return;
            string address = Ui.Ask("插入链接", "链接地址", "https://"); Uri uri; if (address == null) return; if (!Uri.TryCreate(address, UriKind.Absolute, out uri)) throw new ArgumentException("链接地址无效。");
            Selection.Text = ""; var link = new Hyperlink(new Run(label), CaretPosition) { NavigateUri = uri }; CaretPosition = link.ElementEnd;
        }
        public void Format(string action)
        {
            if (IsReadOnly) return;
            switch (action)
            {
                case "bold": EditingCommands.ToggleBold.Execute(null, this); break;
                case "italic": EditingCommands.ToggleItalic.Execute(null, this); break;
                case "underline": EditingCommands.ToggleUnderline.Execute(null, this); break;
                case "strike": var current = Selection.GetPropertyValue(Inline.TextDecorationsProperty); Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, current is TextDecorationCollection && ((TextDecorationCollection)current).Contains(TextDecorations.Strikethrough[0]) ? null : TextDecorations.Strikethrough); break;
                case "bullets": EditingCommands.ToggleBullets.Execute(null, this); break;
                case "numbers": EditingCommands.ToggleNumbering.Execute(null, this); break;
                case "left": EditingCommands.AlignLeft.Execute(null, this); break;
                case "center": EditingCommands.AlignCenter.Execute(null, this); break;
                case "right": EditingCommands.AlignRight.Execute(null, this); break;
                case "ltr": Selection.ApplyPropertyValue(FlowDocument.FlowDirectionProperty, FlowDirection.LeftToRight); break;
                case "rtl": Selection.ApplyPropertyValue(FlowDocument.FlowDirectionProperty, FlowDirection.RightToLeft); break;
                case "date": InsertText(DateTime.Now.ToString("yyyy-MM-dd HH:mm")); break;
                case "checkbox": InsertText("☐ "); break;
                case "rule": InsertText("\n────────────────────\n"); break;
                case "link": Link(); break;
            }
            Focus();
        }
        public void FontDialog()
        {
            using (var d = new System.Windows.Forms.FontDialog { ShowColor = true, Font = new System.Drawing.Font(FontFamily.Source, (float)(FontSize * .75)) })
            {
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var range = Selection.IsEmpty ? new TextRange(Document.ContentStart, Document.ContentEnd) : Selection;
                range.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(d.Font.Name)); range.ApplyPropertyValue(TextElement.FontSizeProperty, (double)d.Font.Size * 4 / 3);
                range.ApplyPropertyValue(TextElement.FontWeightProperty, d.Font.Bold ? FontWeights.Bold : FontWeights.Normal); range.ApplyPropertyValue(TextElement.FontStyleProperty, d.Font.Italic ? FontStyles.Italic : FontStyles.Normal);
                range.ApplyPropertyValue(TextElement.ForegroundProperty, Ui.Brush(System.Drawing.ColorTranslator.ToHtml(d.Color)));
            }
        }
        public void Scale(double factor)
        {
            if (IsReadOnly) return; var range = Selection.IsEmpty ? new TextRange(Document.ContentStart, Document.ContentEnd) : Selection; object current = range.GetPropertyValue(TextElement.FontSizeProperty); double size = current is double ? (double)current : FontSize; range.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Max(6, Math.Min(200, size * factor)));
        }
        public bool Find(string term)
        {
            if (string.IsNullOrEmpty(term)) return false;
            TextPointer p = Selection.End;
            for (int pass = 0; pass < 2; pass++)
            {
                while (p != null && p.CompareTo(Document.ContentEnd) < 0)
                {
                    if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text) { string text = p.GetTextInRun(LogicalDirection.Forward); int offset = text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase); if (offset >= 0) { var start = p.GetPositionAtOffset(offset); Selection.Select(start, start.GetPositionAtOffset(term.Length)); Focus(); return true; } p = p.GetPositionAtOffset(text.Length); }
                    else p = p.GetNextContextPosition(LogicalDirection.Forward);
                }
                p = Document.ContentStart;
            }
            return false;
        }
        private void DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var p = GetPositionFromPoint(e.GetPosition(this), true); if (p == null) return;
            var next = p.GetPositionAtOffset(1, LogicalDirection.Forward); if (next == null) return; var r = new TextRange(p, next);
            if (!IsReadOnly && (r.Text == "☐" || r.Text == "☑")) { r.Text = r.Text == "☐" ? "☑" : "☐"; e.Handled = true; return; }
            var run = p.Parent as Run; if (run != null) { var match = Regex.Match(run.Text, @"(?:https?://|mailto:|file:///)[^\s<>]+", RegexOptions.IgnoreCase); if (match.Success) { Uri uri; if (Uri.TryCreate(match.Value, UriKind.Absolute, out uri)) { OpenLink(uri); e.Handled = true; } } }
        }
        public static void OpenLink(Uri uri) { if (uri == null || !(new[] { "http", "https", "mailto", "file" }).Contains(uri.Scheme)) return; try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { Ui.Error(ex); } }
    }
}
