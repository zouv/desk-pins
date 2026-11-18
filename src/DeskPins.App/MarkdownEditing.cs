using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfInline = System.Windows.Documents.Inline;

namespace DeskPins.App
{
    // 行首 Markdown 标记在输入空格时转换；行内标记在回车提交当前段时转换；纯文本粘贴整体转换。
    public static class MarkdownEditing
    {
        public static bool TryAutoFormat(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || !editor.MarkdownAutoFormat || !editor.Selection.IsEmpty) return false;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null || paragraph.Parent is ListItem || !CaretAtEnd(editor, paragraph)) return false;
            string prefix = new TextRange(paragraph.ContentStart, editor.CaretPosition).Text.Replace("\r", "").Replace("\n", "");
            if (prefix.Length == 0) return false;
            int level = 0; bool bullet = false, ordered = false, quote = false; char? task = null;
            if (Regex.IsMatch(prefix, @"^#{1,6}$")) level = prefix.Length;
            else if (prefix == "-" || prefix == "*" || prefix == "+") bullet = true;
            else if (Regex.IsMatch(prefix, @"^\d{1,9}\.$")) ordered = true;
            else if (prefix == ">") quote = true;
            else { var match = Regex.Match(prefix, @"^- \[([ xX])\]$"); if (match.Success) { task = match.Groups[1].Value == " " ? '☐' : '☑'; bullet = true; } }
            if (level == 0 && !bullet && !ordered && !quote) return false;
            editor.BeginChange();
            try
            {
                new TextRange(paragraph.ContentStart, editor.CaretPosition).Text = "";
                if (level > 0 && paragraph.Parent != null) ApplyHeadingStyle(editor, paragraph, level);
                else if (quote && paragraph.Parent != null) ApplyQuoteStyle(editor, paragraph);
                else if (ordered && paragraph.Parent != null) { editor.CaretPosition = paragraph.ContentEnd; EditingCommands.ToggleNumbering.Execute(null, editor); }
                else if (bullet && paragraph.Parent != null)
                {
                    if (task.HasValue) { paragraph.Inlines.Add(new Run(task.Value + " ")); editor.CaretPosition = paragraph.ContentEnd; }
                    EditingCommands.ToggleBullets.Execute(null, editor);
                    if (task.HasValue) { var item = paragraph.Parent as ListItem; var parentList = item == null ? null : item.Parent as List; if (parentList != null && parentList.ListItems.Count == 1) parentList.MarkerStyle = TextMarkerStyle.None; }
                }
                editor.CaretPosition = paragraph.ContentEnd;
            }
            catch (Exception) { return false; }
            finally { editor.EndChange(); }
            return true;
        }

        public static void CommitInline(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || !editor.MarkdownAutoFormat || !editor.Selection.IsEmpty) return;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null || !CaretAtEnd(editor, paragraph)) return;
            string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(text) || !MarkdownRenderer.HasInlineMarkdown(text) || !IsPlain(paragraph)) return;
            List<WpfInline> inlines;
            if (!MarkdownRenderer.TryRenderInline(text, editor.Document.FontSize, out inlines) || inlines.Count == 0) return;
            editor.BeginChange();
            try
            {
                paragraph.Inlines.Clear();
                foreach (var inline in inlines) paragraph.Inlines.Add(inline);
                editor.CaretPosition = paragraph.ContentEnd;
            }
            finally { editor.EndChange(); }
        }

        // 回车时先提交行内 Markdown；标题段之后新建普通段落，避免下一行继续继承标题字号 / 粗体。
        public static bool Enter(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || !editor.MarkdownAutoFormat || !editor.Selection.IsEmpty) return false;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null || !CaretAtEnd(editor, paragraph)) return false;
            bool heading = MarkdownRenderer.HeadingLevel(paragraph, editor.Document.FontSize) > 0;
            CommitInline(editor);
            if (!heading || !(paragraph.Parent is FlowDocument)) return false;
            var next = new Paragraph { FontSize = editor.Document.FontSize, FontWeight = FontWeights.Normal, FontStyle = FontStyles.Normal };
            editor.Document.Blocks.InsertAfter(paragraph, next);
            editor.CaretPosition = next.ContentStart;
            return true;
        }

        public static bool TryPaste(TextEditor editor, DataObjectPastingEventArgs e)
        {
            if (editor == null || editor.IsReadOnly || !editor.MarkdownAutoFormat || e == null || e.DataObject == null) return false;
            if (e.DataObject.GetDataPresent(DataFormats.Rtf) || e.DataObject.GetDataPresent(DataFormats.Xaml) || e.DataObject.GetDataPresent(DataFormats.Html)) return false;
            string text = (e.DataObject.GetData(DataFormats.UnicodeText) as string) ?? (e.DataObject.GetData(DataFormats.Text) as string);
            if (!MarkdownRenderer.LooksLikeMarkdown(text)) return false;
            return Insert(editor, text);
        }

        // 供粘贴与测试使用；返回 false 时调用方应回退到系统默认粘贴。
        public static bool Insert(TextEditor editor, string markdown)
        {
            if (editor == null || editor.IsReadOnly || string.IsNullOrWhiteSpace(markdown)) return false;
            var document = Markdig.Markdown.Parse(markdown, MarkdownRenderer.Pipeline);
            if (document.Count == 0) return false;
            editor.BeginChange();
            try { return InsertParsed(editor, document); }
            finally { editor.EndChange(); }
        }

        private static bool InsertParsed(TextEditor editor, MarkdownDocument document)
        {
            if (!editor.Selection.IsEmpty) editor.Selection.Text = "";
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null) { paragraph = new Paragraph(); editor.Document.Blocks.Add(paragraph); editor.CaretPosition = paragraph.ContentEnd; }
            if (document.Count == 1 && document[0] is ParagraphBlock)
            {
                var rendered = MarkdownRenderer.RenderInlines(((ParagraphBlock)document[0]).Inline, editor.Document.FontSize);
                if (rendered.Count == 0) return false;
                var container = new Span(new Run(""), editor.CaretPosition);
                container.Inlines.Clear();
                foreach (var inline in rendered) container.Inlines.Add(inline);
                editor.CaretPosition = container.ElementEnd;
                return true;
            }
            if (!(paragraph.Parent is FlowDocument) || !CaretAtEnd(editor, paragraph)) return false;
            var blocks = MarkdownRenderer.RenderBlocks(document, editor.Document.FontSize);
            if (blocks.Count == 0) return false;
            bool empty = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim().Length == 0;
            var anchor = (System.Windows.Documents.Block)paragraph;
            foreach (var block in blocks) { editor.Document.Blocks.InsertAfter(anchor, block); anchor = block; }
            var tail = new Paragraph(); editor.Document.Blocks.InsertAfter(anchor, tail);
            if (empty) editor.Document.Blocks.Remove(paragraph);
            editor.CaretPosition = tail.ContentStart;
            return true;
        }

        private static bool CaretAtEnd(TextEditor editor, Paragraph paragraph) { return new TextRange(editor.CaretPosition, paragraph.ContentEnd).Text.Trim('\r', '\n').Length == 0; }

        public static void ApplyHeading(TextEditor editor, int level)
        {
            if (editor == null || editor.IsReadOnly) return;
            var paragraphs = SelectedParagraphs(editor);
            if (paragraphs.Count == 0) return;
            level = Math.Max(0, Math.Min(6, level));
            editor.BeginChange();
            try { foreach (var paragraph in paragraphs) { if (level == 0) ResetHeading(editor, paragraph); else ApplyHeadingStyle(editor, paragraph, level); } }
            finally { editor.EndChange(); }
        }

        public static void ToggleQuote(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly) return;
            var paragraphs = SelectedParagraphs(editor);
            if (paragraphs.Count == 0) return;
            bool remove = paragraphs.All(p => p.BorderThickness.Left > 0);
            editor.BeginChange();
            try { foreach (var paragraph in paragraphs) { if (remove) ResetQuote(editor, paragraph); else ApplyQuoteStyle(editor, paragraph); } }
            finally { editor.EndChange(); }
        }

        public static void ApplyTaskList(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly) return;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null) return;
            editor.BeginChange();
            try
            {
                string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimStart();
                if (!text.StartsWith("☐") && !text.StartsWith("☑")) new Run("☐ ", paragraph.ContentStart);
                editor.CaretPosition = paragraph.ContentEnd;
                if (!(paragraph.Parent is ListItem))
                {
                    EditingCommands.ToggleBullets.Execute(null, editor);
                    var item = paragraph.Parent as ListItem; var parentList = item == null ? null : item.Parent as List;
                    if (parentList != null && parentList.ListItems.Count == 1) parentList.MarkerStyle = TextMarkerStyle.None;
                }
                editor.CaretPosition = paragraph.ContentEnd;
            }
            finally { editor.EndChange(); }
        }

        public static bool ApplyInlineCode(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || editor.Selection.IsEmpty) return false;
            var family = editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
            bool mono = family is FontFamily && MarkdownRenderer.IsMonospace((FontFamily)family);
            editor.BeginChange();
            try
            {
                if (mono)
                {
                    if (editor.FontFamily != null) editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, editor.FontFamily);
                    editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, editor.Background ?? Brushes.Transparent);
                }
                else
                {
                    editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, MarkdownRenderer.MonoFont);
                    editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, MarkdownRenderer.CodeBackground);
                }
            }
            finally { editor.EndChange(); }
            return true;
        }

        public static void ApplyCodeBlock(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly) return;
            var paragraphs = SelectedParagraphs(editor);
            if (paragraphs.Count == 0) return;
            editor.BeginChange();
            try
            {
                foreach (var paragraph in paragraphs)
                {
                    paragraph.FontFamily = MarkdownRenderer.MonoFont; paragraph.FontSize = editor.Document.FontSize; paragraph.FontWeight = FontWeights.Normal;
                    paragraph.Background = MarkdownRenderer.CodeBackground; paragraph.Padding = new Thickness(8, 5, 8, 5);
                    var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
                    range.ApplyPropertyValue(TextElement.FontFamilyProperty, MarkdownRenderer.MonoFont);
                    range.ApplyPropertyValue(TextElement.FontSizeProperty, editor.Document.FontSize);
                    range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                    range.ApplyPropertyValue(TextElement.BackgroundProperty, MarkdownRenderer.CodeBackground);
                }
            }
            finally { editor.EndChange(); }
        }

        public static void InsertHorizontalRule(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly) return;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null || !(paragraph.Parent is FlowDocument)) return;
            editor.BeginChange();
            try
            {
                var rule = new Paragraph(new Run("────"));
                editor.Document.Blocks.InsertAfter(paragraph, rule);
                var tail = new Paragraph();
                editor.Document.Blocks.InsertAfter(rule, tail);
                editor.CaretPosition = tail.ContentStart;
            }
            finally { editor.EndChange(); }
        }

        public static bool ConvertSelection(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || editor.Selection.IsEmpty) return false;
            string text = new TextRange(editor.Selection.Start, editor.Selection.End).Text;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var document = Markdig.Markdown.Parse(text, MarkdownRenderer.Pipeline);
            if (document.Count == 0) return false;
            editor.BeginChange();
            try
            {
                editor.Selection.Text = "";
                if (InsertParsed(editor, document)) return true;
                editor.Selection.Text = text;
                return false;
            }
            finally { editor.EndChange(); }
        }

        public static bool PasteMarkdown(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || !Clipboard.ContainsText()) return false;
            try { return Insert(editor, Clipboard.GetText()); }
            catch (Exception ex) { Ui.Error(ex); return false; }
        }

        public static void CopyMarkdown(TextEditor editor)
        {
            if (editor == null) return;
            try { Clipboard.SetText(MarkdownExporter.ToMarkdown(editor.Document, editor.Document.FontSize)); }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private static List<Paragraph> SelectedParagraphs(TextEditor editor)
        {
            var result = new List<Paragraph>();
            if (editor.Selection.IsEmpty) { var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph; if (paragraph != null) result.Add(paragraph); return result; }
            CollectParagraphs(editor.Document.Blocks, editor.Selection.Start, editor.Selection.End, result);
            return result;
        }

        private static void CollectParagraphs(IEnumerable<System.Windows.Documents.Block> blocks, TextPointer start, TextPointer end, List<Paragraph> result)
        {
            foreach (var block in blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null) { if (paragraph.ContentEnd.CompareTo(start) >= 0 && paragraph.ContentStart.CompareTo(end) <= 0) result.Add(paragraph); continue; }
                var list = block as List;
                if (list != null) { foreach (ListItem item in list.ListItems) CollectParagraphs(item.Blocks, start, end, result); continue; }
                var section = block as Section;
                if (section != null) { CollectParagraphs(section.Blocks, start, end, result); continue; }
                var table = block as Table;
                if (table != null) { foreach (var group in table.RowGroups) foreach (var row in group.Rows) foreach (var cell in row.Cells) CollectParagraphs(cell.Blocks, start, end, result); }
            }
        }

        private static void ResetHeading(TextEditor editor, Paragraph paragraph)
        {
            paragraph.FontSize = editor.Document.FontSize; paragraph.FontWeight = FontWeights.Normal; paragraph.Padding = new Thickness(0);
            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            range.ApplyPropertyValue(TextElement.FontSizeProperty, editor.Document.FontSize);
            range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        }

        private static void ResetQuote(TextEditor editor, Paragraph paragraph)
        {
            paragraph.FontStyle = FontStyles.Normal; paragraph.ClearValue(TextElement.ForegroundProperty);
            paragraph.Padding = new Thickness(0); paragraph.BorderThickness = new Thickness(0); paragraph.BorderBrush = null;
            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            range.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            if (editor.Foreground != null) range.ApplyPropertyValue(TextElement.ForegroundProperty, editor.Foreground);
        }

        private static void ApplyHeadingStyle(TextEditor editor, Paragraph paragraph, int level)
        {
            double size = MarkdownRenderer.HeadingSize(level, editor.Document.FontSize);
            paragraph.FontSize = size; paragraph.FontWeight = FontWeights.Bold; paragraph.Padding = new Thickness(0, level <= 2 ? 6 : 3, 0, 2);
            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            range.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        }

        private static void ApplyQuoteStyle(TextEditor editor, Paragraph paragraph)
        {
            paragraph.FontStyle = FontStyles.Italic; paragraph.Foreground = UiTheme.Muted;
            paragraph.Padding = new Thickness(12, 0, 0, 0); paragraph.BorderThickness = new Thickness(3, 0, 0, 0); paragraph.BorderBrush = UiTheme.Muted;
            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            range.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
            range.ApplyPropertyValue(TextElement.ForegroundProperty, UiTheme.Muted);
        }

        private static bool IsPlain(Paragraph paragraph)
        {
            foreach (var inline in paragraph.Inlines) if (!PlainInline(inline, paragraph)) return false;
            return true;
        }

        private static bool PlainInline(WpfInline inline, Paragraph paragraph)
        {
            if (inline is InlineUIContainer || inline is Hyperlink || inline is Figure || inline is Floater) return false;
            var run = inline as Run;
            if (run != null)
            {
                if (run.FontWeight != paragraph.FontWeight || run.FontStyle != paragraph.FontStyle || Math.Abs(run.FontSize - paragraph.FontSize) > .01) return false;
                if ((run.FontFamily?.Source ?? "") != (paragraph.FontFamily?.Source ?? "")) return false;
                if (run.TextDecorations != null && run.TextDecorations.Count > 0) return false;
                if (!Equals(run.Foreground, paragraph.Foreground) || !Equals(run.Background, paragraph.Background)) return false;
                return true;
            }
            var span = inline as Span;
            if (span != null) { foreach (var child in span.Inlines) if (!PlainInline(child, paragraph)) return false; }
            return true;
        }
    }
}
