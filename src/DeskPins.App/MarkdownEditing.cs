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
        private static readonly Regex rawListPattern = new Regex(@"^(?<lead>[ \t　]*)(?<marker>•|☐|☑|\d{1,9}\.|[-*+]|\[[ xX]?\])(?<sep>[ \t　]+)(?<rest>.*)$", RegexOptions.Compiled);
        public static bool TryAutoFormat(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || !editor.MarkdownAutoFormat || !editor.Selection.IsEmpty) return false;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null || !CaretAtEnd(editor, paragraph)) return false;
            if (TryFenceBlock(editor, paragraph)) return true;
            if (TryInlineCode(editor, paragraph)) return true;
            if (paragraph.Parent is ListItem) return false;
            string prefix = new TextRange(paragraph.ContentStart, editor.CaretPosition).Text.Replace("\r", "").Replace("\n", "");
            if (prefix.Length == 0) return false;
            var indentMatch = Regex.Match(prefix, "^(?<lead>[ \\t\\u3000]*)(?<body>.*)$");
            int indentLevel = indentMatch.Groups["lead"].Value.Length / 4;
            string body = indentMatch.Groups["body"].Value;
            int level = 0; bool bullet = false, ordered = false, quote = false; char? task = null;
            if (Regex.IsMatch(body, @"^#{1,6}$")) level = body.Length;
            else if (body == "-" || body == "*" || body == "+") bullet = true;
            else if (Regex.IsMatch(body, @"^\d{1,9}\.$")) ordered = true;
            else if (body == ">" || body == "|" || body == "｜") quote = true;
            else { var match = Regex.Match(body, "^(?:-\\s*)?[\\[［]([ xX\\u3000]?)[\\]］]$"); if (match.Success) { task = match.Groups[1].Value == "x" || match.Groups[1].Value == "X" ? '☑' : '☐'; bullet = true; } }
            if (level == 0 && !bullet && !ordered && !quote) return false;
            editor.BeginChange();
            try
            {
                new TextRange(paragraph.ContentStart, editor.CaretPosition).Text = "";
                if (level > 0 && paragraph.Parent != null) ApplyHeadingStyle(editor, paragraph, level);
                else if (quote && paragraph.Parent != null) ApplyQuoteStyle(editor, paragraph);
                else if (ordered && paragraph.Parent != null) ApplyListMarker(editor, paragraph, "1.", indentLevel);
                else if (bullet && paragraph.Parent != null) ApplyListMarker(editor, paragraph, task.HasValue ? task.Value.ToString() : MarkdownList.BulletMarker, indentLevel);
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

        // 回车时先提交行内 Markdown；任务项续开新任务，空任务 / 空引用 / 空代码块回到正文，标题段之后新建普通段落。
        public static bool Enter(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly || !editor.MarkdownAutoFormat || !editor.Selection.IsEmpty) return false;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            if (paragraph == null || !CaretAtEnd(editor, paragraph)) return false;
            string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            string rawText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n');
            if (IsQuoteParagraph(paragraph)) return ExitQuote(editor, paragraph);
            if (IsCodeBlockParagraph(paragraph)) return ExitCodeBlock(editor, paragraph);
            string listMarker;
            if (MarkdownList.TryMarker(paragraph, out listMarker))
            {
                CommitInline(editor);
                return EnterList(editor, paragraph, listMarker);
            }
            if (!(paragraph.Parent is ListItem) && TryRawList(editor, paragraph, rawText)) return true;
            var item = paragraph.Parent as ListItem;
            var list = item == null ? null : item.Parent as List;
            if (list != null && (text.StartsWith("☐") || text.StartsWith("☑")))
            {
                CommitInline(editor);
                return EnterTask(editor, paragraph, item, list);
            }
            bool heading = MarkdownRenderer.HeadingLevel(paragraph, editor.Document.FontSize) > 0;
            CommitInline(editor);
            if (!heading || !(paragraph.Parent is FlowDocument)) return false;
            var next = new Paragraph { FontSize = editor.Document.FontSize, FontWeight = FontWeights.Normal, FontStyle = FontStyles.Normal };
            editor.Document.Blocks.InsertAfter(paragraph, next);
            editor.CaretPosition = next.ContentStart;
            return true;
        }

        private static bool EnterList(TextEditor editor, Paragraph paragraph, string marker)
        {
            string raw = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimStart('\r', '\n');
            int zeros = 0; while (zeros < raw.Length && raw[zeros] == '​') zeros++;
            int cut = zeros + Math.Min(marker.Length, Math.Max(0, raw.Length - zeros));
            if (cut < raw.Length && (raw[cut] == ' ' || raw[cut] == '\t')) cut++;
            string content = raw.Substring(Math.Min(cut, raw.Length)).Trim();
            if (content.Length == 0)
            {
                var pointers = CharPointers(paragraph);
                var start = paragraph.ContentStart;
                var end = cut > 0 && cut <= pointers.Count ? pointers[cut - 1].GetPositionAtOffset(1) : paragraph.ContentEnd;
                new TextRange(start, end).Text = "";
                paragraph.Padding = new Thickness(0); paragraph.TextIndent = 0;
                editor.CaretPosition = paragraph.ContentStart;
                return true;
            }
            if (!(paragraph.Parent is FlowDocument document)) return false;
            int level = MarkdownList.Level(paragraph, marker, editor.Document.FontSize);
            string next = MarkdownList.NextMarker(marker);
            var added = new Paragraph();
            if (level > 0) added.Inlines.Add(new Run(MarkdownList.Prefix(level)));
            if (next == "☐" || next == "☑") { added.Inlines.Add(MarkdownRenderer.CheckboxGlyph(next == "☑")); added.Inlines.Add(new Run(" ")); }
            else added.Inlines.Add(new Run(next + " "));
            MarkdownList.Apply(added, level, next, editor.Document.FontSize);
            document.Blocks.InsertAfter(paragraph, added);
            editor.CaretPosition = added.ContentEnd;
            return true;
        }

        private static bool TryRawList(TextEditor editor, Paragraph paragraph, string rawText)
        {
            var match = rawListPattern.Match(rawText);
            if (!match.Success) return false;
            string lead = match.Groups["lead"].Value;
            string marker = match.Groups["marker"].Value;
            string rest = match.Groups["rest"].Value.Trim();
            if (rest.Length == 0)
            {
                int cut = match.Groups["rest"].Index;
                var pointers = CharPointers(paragraph);
                var end = cut > 0 && cut <= pointers.Count ? pointers[cut - 1].GetPositionAtOffset(1) : paragraph.ContentEnd;
                new TextRange(paragraph.ContentStart, end).Text = "";
                paragraph.Padding = new Thickness(0); paragraph.TextIndent = 0;
                editor.CaretPosition = paragraph.ContentStart;
                return true;
            }
            if (!(paragraph.Parent is FlowDocument document)) return false;
            var added = new Paragraph();
            if (marker == "☐" || marker == "☑")
            {
                if (lead.Length > 0) added.Inlines.Add(new Run(lead));
                added.Inlines.Add(MarkdownRenderer.CheckboxGlyph(false));
                added.Inlines.Add(new Run(" "));
            }
            else
            {
                string next = marker.StartsWith("[") ? "[ ]" : (marker == "-" || marker == "*" || marker == "+" ? marker : MarkdownList.NextMarker(marker));
                added.Inlines.Add(new Run(lead + next + " "));
            }
            document.Blocks.InsertAfter(paragraph, added);
            editor.CaretPosition = added.ContentEnd;
            return true;
        }

        private static bool EnterTask(TextEditor editor, Paragraph paragraph, ListItem item, List list)
        {
            string content = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            bool empty = content.Trim('☐', '☑', ' ', '\t').Length == 0;
            if (empty)
            {
                if (!(list.Parent is FlowDocument document)) return false;
                var body = new Paragraph { FontSize = editor.Document.FontSize, FontWeight = FontWeights.Normal, FontStyle = FontStyles.Normal, FontFamily = editor.FontFamily ?? SystemFonts.MessageFontFamily };
                document.Blocks.InsertAfter(list, body);
                list.ListItems.Remove(item);
                if (list.ListItems.Count == 0) document.Blocks.Remove(list);
                editor.CaretPosition = body.ContentStart;
                return true;
            }
            var newItem = new ListItem();
            var newParagraph = new Paragraph(new Run("☐ ")) { Margin = new Thickness(0), TextIndent = -2.54 * Math.Max(1, editor.Document.FontSize) };
            newItem.Blocks.Add(newParagraph);
            list.ListItems.Add(newItem);
            MarkdownRenderer.ApplyListLayout(list, editor.Document.FontSize);
            editor.CaretPosition = newParagraph.ContentEnd;
            return true;
        }

        private static bool IsQuoteParagraph(Paragraph paragraph) { return paragraph.BorderThickness.Left > 0 || paragraph.FontStyle == FontStyles.Italic; }

        private static bool IsCodeBlockParagraph(Paragraph paragraph)
        {
            if (MarkdownRenderer.IsMonospace(paragraph.FontFamily)) return true;
            bool any = false;
            foreach (var inline in paragraph.Inlines)
            {
                var run = inline as Run;
                if (run == null || string.IsNullOrWhiteSpace(run.Text)) continue;
                if (!MarkdownRenderer.IsMonospace(run.FontFamily)) return false;
                any = true;
            }
            return any && paragraph.Padding.Left > 0;
        }

        private static bool ExitQuote(TextEditor editor, Paragraph paragraph)
        {
            if (!(paragraph.Parent is FlowDocument document)) return false;
            bool empty = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim().Length == 0;
            var body = new Paragraph { FontSize = editor.Document.FontSize, FontWeight = FontWeights.Normal, FontStyle = FontStyles.Normal, FontFamily = editor.FontFamily ?? SystemFonts.MessageFontFamily };
            document.Blocks.InsertAfter(paragraph, body);
            if (empty) document.Blocks.Remove(paragraph);
            editor.CaretPosition = body.ContentStart;
            return true;
        }

        private static bool ExitCodeBlock(TextEditor editor, Paragraph paragraph)
        {
            if (!(paragraph.Parent is FlowDocument document)) return false;
            bool empty = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim().Length == 0;
            var body = new Paragraph { FontSize = editor.Document.FontSize, FontWeight = FontWeights.Normal, FontStyle = FontStyles.Normal, FontFamily = editor.FontFamily ?? SystemFonts.MessageFontFamily };
            document.Blocks.InsertAfter(paragraph, body);
            if (empty) document.Blocks.Remove(paragraph);
            editor.CaretPosition = body.ContentStart;
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

        private static bool TryFenceBlock(TextEditor editor, Paragraph paragraph)
        {
            if (!(paragraph.Parent is FlowDocument document)) return false;
            if (new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim() != "```") return false;
            var blocks = document.Blocks.ToArray();
            int close = Array.IndexOf(blocks, paragraph); if (close <= 0) return false;
            int open = -1;
            for (int i = close - 1; i >= 0; i--)
            {
                var candidate = blocks[i] as Paragraph; if (candidate == null) return false;
                if (new TextRange(candidate.ContentStart, candidate.ContentEnd).Text.Trim() == "```") { open = i; break; }
            }
            if (open < 0) return false;
            var lines = new List<string>();
            for (int i = open + 1; i < close; i++)
            {
                var content = blocks[i] as Paragraph; if (content == null) return false;
                lines.Add(new TextRange(content.ContentStart, content.ContentEnd).Text.TrimEnd('\r', '\n'));
            }
            var code = new Paragraph { FontFamily = MarkdownRenderer.MonoFont, FontSize = editor.Document.FontSize, FontWeight = FontWeights.Normal, Background = MarkdownRenderer.CodeBackground, Padding = new Thickness(8, 5, 8, 5) };
            for (int i = 0; i < lines.Count; i++) { if (i > 0) code.Inlines.Add(new LineBreak()); code.Inlines.Add(new Run(lines[i])); }
            editor.BeginChange();
            try
            {
                document.Blocks.InsertAfter(blocks[open], code);
                for (int i = close; i > open; i--) document.Blocks.Remove(blocks[i]);
                document.Blocks.Remove(blocks[open]);
                editor.CaretPosition = code.ContentEnd;
            }
            finally { editor.EndChange(); }
            return true;
        }

        private static bool TryInlineCode(TextEditor editor, Paragraph paragraph)
        {
            string prefix = new TextRange(paragraph.ContentStart, editor.CaretPosition).Text;
            var match = Regex.Match(prefix, "`([^`\\r\\n]+)`$");
            if (!match.Success) return false;
            int openIndex = match.Groups[1].Index - 1;
            int contentIndex = match.Groups[1].Index;
            int length = match.Groups[1].Length;
            var pointers = CharPointers(paragraph);
            if (openIndex < 0 || contentIndex + length >= pointers.Count) return false;
            var open = pointers[openIndex];
            var contentStart = pointers[contentIndex];
            var contentEnd = pointers[contentIndex + length];
            var closeEnd = contentEnd.GetPositionAtOffset(1);
            if (closeEnd == null) return false;
            editor.BeginChange();
            try
            {
                new TextRange(contentEnd, closeEnd).Text = "";
                new TextRange(open, open.GetPositionAtOffset(1)).Text = "";
                var range = new TextRange(contentStart, contentEnd);
                range.ApplyPropertyValue(TextElement.FontFamilyProperty, MarkdownRenderer.MonoFont);
                range.ApplyPropertyValue(TextElement.BackgroundProperty, MarkdownRenderer.CodeBackground);
                var bodyFamily = paragraph.FontFamily ?? editor.FontFamily ?? SystemFonts.MessageFontFamily;
                var space = new Run(" ", contentEnd) { FontFamily = bodyFamily, FontSize = paragraph.FontSize, FontWeight = paragraph.FontWeight, FontStyle = paragraph.FontStyle };
                editor.CaretPosition = space.ElementEnd;
            }
            finally { editor.EndChange(); }
            return true;
        }

        private static List<TextPointer> CharPointers(Paragraph paragraph)
        {
            var pointers = new List<TextPointer>();
            var position = paragraph.ContentStart;
            while (position != null && position.CompareTo(paragraph.ContentEnd) < 0)
            {
                if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string text = position.GetTextInRun(LogicalDirection.Forward);
                    for (int i = 0; i < text.Length; i++) pointers.Add(position.GetPositionAtOffset(i));
                    position = position.GetPositionAtOffset(text.Length);
                }
                else position = position.GetNextContextPosition(LogicalDirection.Forward);
            }
            return pointers;
        }

        public static bool ChangeListIndent(TextEditor editor, bool outdent)
        {
            if (editor == null || editor.IsReadOnly || !editor.Selection.IsEmpty) return false;
            var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            string marker;
            if (paragraph == null) return false;
            if (paragraph.Parent is ListItem) editor.ConvertLegacyLists();
            if (IsCodeBlockParagraph(paragraph)) return IndentPlain(editor, outdent);
            if (IsQuoteParagraph(paragraph)) return IndentPlain(editor, outdent);
            if (!MarkdownList.TryMarker(paragraph, out marker))
            {
                var raw = rawListPattern.Match(new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n'));
                if (!raw.Success) return IndentPlain(editor, outdent);
                editor.BeginChange();
                try
                {
                    if (outdent)
                    {
                        string lead = raw.Groups["lead"].Value;
                        int remove = Math.Min(4, lead.Length);
                        if (remove > 0)
                        {
                            var pointers = CharPointers(paragraph);
                            var end = remove <= pointers.Count ? pointers[remove - 1].GetPositionAtOffset(1) : paragraph.ContentStart;
                            new TextRange(paragraph.ContentStart, end).Text = "";
                        }
                    }
                    else paragraph.ContentStart.InsertTextInRun("    ");
                    editor.CaretPosition = paragraph.ContentEnd;
                }
                finally { editor.EndChange(); }
                return true;
            }
            int level = MarkdownList.Level(paragraph, marker, editor.Document.FontSize);
            int next = Math.Max(0, level + (outdent ? -1 : 1));
            if (next != level)
            {
                editor.BeginChange();
                try { MarkdownList.SetLevel(paragraph, next, marker, editor.Document.FontSize); editor.CaretPosition = paragraph.ContentEnd; }
                finally { editor.EndChange(); }
            }
            return true;
        }

        private static bool IndentPlain(TextEditor editor, bool outdent)
        {
            if (outdent)
            {
                var paragraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
                if (paragraph == null) return false;
                string before = new TextRange(paragraph.ContentStart, editor.CaretPosition).Text;
                int remove = 0; for (int i = before.Length - 1; i >= 0 && remove < 4 && before[i] == ' '; i--) remove++;
                if (remove > 0)
                {
                    var start = editor.CaretPosition.GetPositionAtOffset(-remove);
                    if (start != null) new TextRange(start, editor.CaretPosition).Text = "";
                }
            }
            else editor.CaretPosition.InsertTextInRun("    ");
            return true;
        }

        private static void ApplyListMarker(TextEditor editor, Paragraph paragraph, string marker, int level = 0)        {
            string existing;
            if (!MarkdownList.TryMarker(paragraph, out existing))
            {
                if (marker == "☐" || marker == "☑") { paragraph.Inlines.Add(MarkdownRenderer.CheckboxGlyph(marker == "☑")); paragraph.Inlines.Add(new Run(" ")); }
                else paragraph.Inlines.Add(new Run(marker + " "));
            }
            MarkdownList.SetLevel(paragraph, level, marker, editor.Document.FontSize);
            editor.CaretPosition = paragraph.ContentEnd;
        }

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

        public static void ApplyBulletList(TextEditor editor, bool ordered)
        {
            if (editor == null || editor.IsReadOnly) return;
            var paragraphs = SelectedParagraphs(editor);
            if (paragraphs.Count == 0) return;
            editor.BeginChange();
            try
            {
                int number = 1;
                foreach (var paragraph in paragraphs)
                {
                    string marker = ordered ? (number++).ToString() + "." : MarkdownList.BulletMarker;
                    string existing;
                    if (MarkdownList.TryMarker(paragraph, out existing)) MarkdownList.ReplaceMarker(paragraph, marker);
                    else new Run(marker + " ", paragraph.ContentStart);
                    MarkdownList.Apply(paragraph, 0, marker, editor.Document.FontSize);
                }
            }
            finally { editor.EndChange(); }
        }

        public static void ApplyTaskList(TextEditor editor)
        {
            if (editor == null || editor.IsReadOnly) return;
            var paragraphs = SelectedParagraphs(editor);
            if (paragraphs.Count == 0) return;
            editor.BeginChange();
            try
            {
                foreach (var paragraph in paragraphs)
                {
                    string existing;
                    if (MarkdownList.TryMarker(paragraph, out existing)) MarkdownList.ReplaceMarker(paragraph, "☐");
                    else { var glyph = new Run("☐", paragraph.ContentStart) { FontFamily = MarkdownRenderer.CheckboxFont }; new Run(" ", glyph.ElementEnd); }
                    MarkdownList.Apply(paragraph, 0, "☐", editor.Document.FontSize);
                }
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
            paragraph.TextIndent = 0; paragraph.Padding = new Thickness(0); paragraph.BorderThickness = new Thickness(0); paragraph.BorderBrush = null;
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
            paragraph.TextIndent = 0; paragraph.Padding = new Thickness(12, 0, 0, 0); paragraph.BorderThickness = new Thickness(3, 0, 0, 0); paragraph.BorderBrush = UiTheme.Muted;
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
