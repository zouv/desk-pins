using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfInline = System.Windows.Documents.Inline;

namespace DeskPins.App
{
    // FlowDocument → Markdown 导出；标题按字号识别，代码块按等宽字体识别，引用按左边框识别，均允许有损。
    public static class MarkdownExporter
    {
        public static string ToMarkdown(FlowDocument document, double baseSize = 0)
        {
            if (document == null) return "";
            var builder = new StringBuilder();
            if (baseSize <= 0) baseSize = document.FontSize > 0 ? document.FontSize : 12;
            foreach (var block in document.Blocks) WriteBlock(block, builder, baseSize, 0);
            return builder.ToString().TrimEnd() + "\n";
        }

        private static void WriteBlock(Block block, StringBuilder builder, double baseSize, int indent)
        {
            var paragraph = block as Paragraph;
            if (paragraph != null) { WriteParagraph(paragraph, builder, baseSize, indent); return; }
            var list = block as List;
            if (list != null) { WriteList(list, builder, baseSize, indent); return; }
            var section = block as Section;
            if (section != null) { foreach (var child in section.Blocks) WriteBlock(child, builder, baseSize, indent); return; }
            var table = block as Table;
            if (table != null) { foreach (var row in table.RowGroups.SelectMany(g => g.Rows)) builder.AppendLine(string.Join(" | ", row.Cells.Select(c => PlainText(c.Blocks)))); builder.AppendLine(); return; }
            var container = block as BlockUIContainer;
            if (container != null) { builder.AppendLine("[image]"); return; }
        }

        private static void WriteParagraph(Paragraph paragraph, StringBuilder builder, double baseSize, int indent)
        {
            string pad = new string(' ', indent);
            if (IsCodeParagraph(paragraph))
            {
                builder.AppendLine("```");
                builder.AppendLine(PlainText(paragraph));
                builder.AppendLine("```");
                builder.AppendLine();
                return;
            }
            int heading = MarkdownRenderer.HeadingLevel(paragraph, baseSize);
            string text = ParagraphText(paragraph, baseSize, heading > 0);
            if (IsThematicBreak(text)) { builder.AppendLine("---"); builder.AppendLine(); return; }
            if (heading > 0) text = new string('#', heading) + " " + text;
            bool quote = paragraph.BorderThickness.Left > 0;
            if (quote) builder.AppendLine(pad + "> " + text.Replace("\n", "\n" + pad + "> "));
            else builder.AppendLine(pad + text);
            builder.AppendLine();
        }

        private static void WriteList(List list, StringBuilder builder, double baseSize, int indent)
        {
            int number = list.StartIndex > 0 ? list.StartIndex : 1;
            string pad = new string(' ', indent);
            foreach (ListItem item in list.ListItems)
            {
                bool first = true;
                foreach (var block in item.Blocks)
                {
                    var paragraph = block as Paragraph;
                    if (paragraph != null)
                    {
                        string text = ParagraphText(paragraph, baseSize, false);
                        if (first)
                        {
                            if (text.StartsWith("☐ ")) text = "[ ] " + text.Substring(2).TrimStart();
                            else if (text.StartsWith("☑ ")) text = "[x] " + text.Substring(2).TrimStart();
                            string marker = list.MarkerStyle == TextMarkerStyle.Decimal ? (number++) + ". " : "- ";
                            builder.Append(pad).Append(marker).AppendLine(text);
                        }
                        else builder.AppendLine(pad + "  " + text);
                        first = false;
                    }
                    else if (block is List nested) { WriteList(nested, builder, baseSize, indent + 2); first = false; }
                    else { WriteBlock(block, builder, baseSize, indent + 2); first = false; }
                }
                if (first) builder.Append(pad).AppendLine(list.MarkerStyle == TextMarkerStyle.Decimal ? (number++) + ". " : "- ");
            }
            builder.AppendLine();
        }

        private static string ParagraphText(Paragraph paragraph, double baseSize, bool skipBoldMarkers)
        {
            var builder = new StringBuilder();
            WriteInlines(paragraph.Inlines, builder, false, false, false, skipBoldMarkers);
            return builder.ToString().TrimEnd();
        }

        private static void WriteInlines(InlineCollection inlines, StringBuilder builder, bool bold, bool italic, bool strike, bool skipBoldMarkers)
        {
            foreach (var inline in inlines)
            {
                var run = inline as Run;
                if (run != null)
                {
                    if (string.IsNullOrEmpty(run.Text)) continue;
                    bool runBold = bold || run.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
                    bool runItalic = italic || run.FontStyle == FontStyles.Italic;
                    bool runStrike = strike || (run.TextDecorations != null && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough));
                    if (IsMono(run.FontFamily)) { builder.Append(CodeSpan(run.Text)); continue; }
                    string text = Escape(run.Text);
                    if (runStrike) text = "~~" + text + "~~";
                    if (runItalic) text = "*" + text + "*";
                    if (runBold && !skipBoldMarkers) text = "**" + text + "**";
                    builder.Append(text); continue;
                }
                var hyperlink = inline as Hyperlink;
                if (hyperlink != null)
                {
                    var label = new StringBuilder();
                    WriteInlines(hyperlink.Inlines, label, bold, italic, strike, skipBoldMarkers);
                    builder.Append('[').Append(label.ToString()).Append("](").Append(hyperlink.NavigateUri == null ? "" : hyperlink.NavigateUri.OriginalString).Append(')');
                    continue;
                }
                var span = inline as Span;
                if (span != null)
                {
                    bool spanBold = bold || span.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
                    bool spanItalic = italic || span.FontStyle == FontStyles.Italic;
                    bool spanStrike = strike || (span.TextDecorations != null && span.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough));
                    WriteInlines(span.Inlines, builder, spanBold, spanItalic, spanStrike, skipBoldMarkers);
                    continue;
                }
                if (inline is LineBreak) { builder.Append('\n'); continue; }
                if (inline is InlineUIContainer) { builder.Append("[image]"); continue; }
            }
        }

        private static bool IsCodeParagraph(Paragraph paragraph)        {
            bool any = false;
            foreach (var inline in paragraph.Inlines)
            {
                var run = inline as Run;
                if (run == null) { if (inline is LineBreak) continue; return false; }
                if (string.IsNullOrWhiteSpace(run.Text)) continue;
                if (!IsMono(run.FontFamily)) return false;
                any = true;
            }
            return any;
        }

        private static bool IsThematicBreak(string text) { string value = (text ?? "").Trim(); return value.Length >= 3 && value.All(c => c == '─'); }
        private static bool IsMono(FontFamily font) { return font != null && (font.Source.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0 || font.Source.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0); }
        private static string PlainText(Block block) { var range = new TextRange(block.ContentStart, block.ContentEnd); return range.Text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n'); }
        private static string PlainText(BlockCollection blocks) { var builder = new StringBuilder(); foreach (var block in blocks) { if (builder.Length > 0) builder.Append('\n'); builder.Append(PlainText(block)); } return builder.ToString(); }
        private static string Escape(string text) { return (text ?? "").Replace("\\", "\\\\").Replace("*", "\\*").Replace("`", "\\`").Replace("[", "\\[").Replace("]", "\\]").Replace("~", "\\~"); }
        private static string CodeSpan(string text)
        {
            text = text ?? ""; int longest = 0, current = 0;
            foreach (var c in text) { current = c == '`' ? current + 1 : 0; longest = Math.Max(longest, current); }
            if (longest == 0) return "`" + text + "`";
            string fence = new string('`', longest + 1);
            return fence + " " + text + " " + fence;
        }
    }
}
