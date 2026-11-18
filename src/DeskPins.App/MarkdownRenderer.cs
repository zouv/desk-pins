using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace DeskPins.App
{
    // 只在输入 / 粘贴边界使用 Markdig 解析，再把 AST 映射为 WPF FlowDocument；便签仍以 RTF 持久化。
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().UseTaskLists().UseAutoLinks().Build();
        private static readonly FontFamily monoFont = new FontFamily("Consolas");
        public static readonly FontFamily CheckboxFont = new FontFamily("Segoe UI Symbol");
        private static readonly Brush codeBackground = Ui.Brush("#F2F2F2");
        private static readonly Regex markdownPattern = new Regex(
            @"(?m)(^[ \t]{0,3}(#{1,6}[ \t]|[-*+][ \t]|\d+[.)][ \t]|>[ \t]|```|[ \t]*([-*_])(?:[ \t]*\2){2,}[ \t]*$))|(\*\*[^\r\n*]+\*\*)|(__[^\r\n_]+__)|(~~[^\r\n~]+~~)|(`[^`\r\n]+`)|(\[[^\]\r\n]+\]\([^)\r\n]+\))|((?<!\*)\*[^*\r\n]+\*(?!\*))|((?<!_)_[^_\r\n]+_(?!_))",
            RegexOptions.Compiled);
        private static readonly double[] headingFactors = { 0, 2.0, 1.7, 1.45, 1.25, 1.1, 1.0 };

        public static bool LooksLikeMarkdown(string text) { return !string.IsNullOrEmpty(text) && markdownPattern.IsMatch(text); }
        public static MarkdownPipeline Pipeline { get { return pipeline; } }
        public static FontFamily MonoFont { get { return monoFont; } }
        public static Brush CodeBackground { get { return codeBackground; } }
        public static bool IsMonospace(FontFamily font) { return font != null && (font.Source.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0 || font.Source.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0); }
        public static Run CheckboxGlyph(bool checkedState) { return new Run(checkedState ? "☑" : "☐") { FontFamily = CheckboxFont }; }
        public static bool IsCheckboxRun(Run run)
        {
            string value = run == null || run.Text == null ? "" : run.Text.TrimStart('​').TrimEnd();
            return value == "☐" || value == "☑";
        }
        public static void NormalizeCheckboxes(InlineCollection inlines)
        {
            foreach (var inline in inlines.ToArray())
            {
                var run = inline as Run;
                if (run != null)
                {
                    string text = run.Text ?? "";
                    int zeros = 0; while (zeros < text.Length && text[zeros] == '​') zeros++;
                    string payload = text.Substring(zeros);
                    if (payload == "☐" || payload == "☑") { run.FontFamily = CheckboxFont; continue; }
                    if (payload.StartsWith("☐ ") || payload.StartsWith("☑ "))
                    {
                        string rest = payload.Substring(2);
                        run.Text = text.Substring(0, zeros + 1);
                        run.FontFamily = CheckboxFont;
                        if (rest.Length > 0) new Run(rest, run.ElementEnd);
                    }
                    continue;
                }
                var span = inline as Span;
                if (span != null) NormalizeCheckboxes(span.Inlines);
            }
        }
        // 旧 WPF List 只统一上下间距；新 Markdown 列表是带行首标记的普通段落（MarkdownList），不经过这里。
        public static void ApplyListLayout(List list, double fontSize)
        {
            if (list == null) return;
            list.Margin = new Thickness(0, 2, 0, 2);
            foreach (ListItem item in list.ListItems)
            {
                item.Margin = new Thickness(0); item.Padding = new Thickness(0);
                foreach (var block in item.Blocks)
                {
                    var paragraph = block as Paragraph;
                    if (paragraph != null) { paragraph.Margin = new Thickness(0); continue; }
                    var nested = block as List;
                    if (nested != null) ApplyListLayout(nested, fontSize);
                }
            }
        }
        public static bool HasInlineMarkdown(string text) { return !string.IsNullOrEmpty(text) && Regex.IsMatch(text, @"(\*\*|__|~~|`|\[[^\]]+\]\(|(?<!\*)\*[^*\r\n]+\*(?!\*)|(?<!_)_[^_\r\n]+_(?!_))"); }
        public static bool TryRenderInline(string text, double baseFontSize, out List<WpfInline> inlines)
        {
            inlines = null;
            var document = Markdown.Parse(text ?? "", pipeline);
            var paragraph = document.Count == 1 ? document[0] as ParagraphBlock : null;
            if (paragraph == null || paragraph.Inline == null || !HasFormatting(paragraph.Inline)) return false;
            inlines = RenderInlines(paragraph.Inline, baseFontSize);
            return inlines.Count > 0;
        }
        private static bool HasFormatting(ContainerInline container)
        {
            if (container == null) return false;
            foreach (var child in container)
            {
                if (child is EmphasisInline || child is CodeInline || child is LinkInline || child is AutolinkInline || child is TaskList) return true;
                var nested = child as ContainerInline; if (nested != null && HasFormatting(nested)) return true;
            }
            return false;
        }
        public static double HeadingSize(int level, double baseSize) { level = Math.Max(1, Math.Min(6, level)); return Math.Max(1, Math.Round(Math.Max(1, baseSize) * headingFactors[level], MidpointRounding.AwayFromZero)); }
        public static int HeadingLevel(Paragraph paragraph, double baseSize)
        {
            if (paragraph == null) return 0;
            double size = paragraph.FontSize; FontWeight weight = paragraph.FontWeight;
            // RTF 往返可能把段落字号 / 粗体落到 Run 上，范围属性优先，段落属性兜底。
            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            var rangeSize = range.GetPropertyValue(TextElement.FontSizeProperty);
            if (rangeSize is double && (double)rangeSize > 0) size = (double)rangeSize;
            var rangeWeight = range.GetPropertyValue(TextElement.FontWeightProperty);
            if (rangeWeight is FontWeight) weight = (FontWeight)rangeWeight;
            if (weight.ToOpenTypeWeight() < FontWeights.SemiBold.ToOpenTypeWeight()) return 0;
            // H6 与正文粗体同字号，无法可靠区分；导出只识别 H1–H5，H6 按粗体段落处理。
            for (int level = 1; level <= 5; level++) if (Math.Abs(size - HeadingSize(level, baseSize)) < .6) return level;
            return 0;
        }

        public static FlowDocument ToFlowDocument(string markdown, double baseFontSize, FontFamily baseFont)
        {
            var document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = baseFont ?? new FontFamily("Verdana"), FontSize = Math.Max(1, baseFontSize) };
            foreach (var block in RenderBlocks(Markdown.Parse(markdown ?? "", pipeline), baseFontSize)) document.Blocks.Add(block);
            if (document.Blocks.Count == 0) document.Blocks.Add(new Paragraph());
            return document;
        }

        public static List<WpfBlock> RenderBlocks(ContainerBlock container, double baseFontSize)
        {
            var blocks = new List<WpfBlock>();
            if (container == null) return blocks;
            foreach (var block in container) AddBlock(blocks, block, baseFontSize);
            return blocks;
        }

        private static void AddBlock(List<WpfBlock> blocks, MdBlock block, double baseFontSize)
        {
            var heading = block as HeadingBlock;
            if (heading != null)
            {
                var paragraph = new Paragraph { FontSize = HeadingSize(heading.Level, baseFontSize), FontWeight = FontWeights.Bold, Padding = new Thickness(0, heading.Level <= 2 ? 6 : 3, 0, 2) };
                AddInlines(paragraph.Inlines, heading.Inline, baseFontSize);
                blocks.Add(paragraph); return;
            }
            var text = block as ParagraphBlock;
            if (text != null)
            {
                var paragraph = new Paragraph();
                AddInlines(paragraph.Inlines, text.Inline, baseFontSize);
                blocks.Add(paragraph); return;
            }
            var list = block as ListBlock;
            if (list != null) { AddListBlocks(blocks, list, baseFontSize, 0); return; }
            var quote = block as QuoteBlock;
            if (quote != null)
            {
                int before = blocks.Count;
                foreach (var child in quote) AddBlock(blocks, child, baseFontSize);
                for (int i = before; i < blocks.Count; i++) StyleQuote(blocks[i]);
                return;
            }
            var fenced = block as FencedCodeBlock;
            if (fenced != null) { blocks.Add(CodeParagraph(fenced)); return; }
            var code = block as CodeBlock;
            if (code != null) { blocks.Add(CodeParagraph(code)); return; }
            var rule = block as ThematicBreakBlock;
            if (rule != null) { blocks.Add(new Paragraph(new Run("────"))); return; }
            var html = block as HtmlBlock;
            if (html != null) { blocks.Add(new Paragraph(new Run(LinesText(html)))); return; }
            var leaf = block as LeafBlock;
            if (leaf != null) { blocks.Add(new Paragraph(new Run(LinesText(leaf)))); return; }
            var nested = block as ContainerBlock;
            if (nested != null) foreach (var child in nested) AddBlock(blocks, child, baseFontSize);
        }

        private static void AddListBlocks(List<WpfBlock> blocks, ListBlock list, double fontSize, int level)
        {
            int number = 1; if (list.IsOrdered) int.TryParse(list.OrderedStart, out number); if (number <= 0) number = 1;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                bool task = HasTaskList(item);
                string marker = task ? TaskMarker(item) : (list.IsOrdered ? number.ToString() + "." : MarkdownList.BulletMarker);
                if (list.IsOrdered) number++;
                bool first = true;
                foreach (var child in item)
                {
                    var paragraphBlock = child as ParagraphBlock;
                    if (paragraphBlock != null)
                    {
                        var paragraph = new Paragraph();
                        if (first && !task) paragraph.Inlines.Add(new Run(MarkdownList.Prefix(level) + marker + " "));
                        else if (first && level > 0) paragraph.Inlines.Add(new Run(MarkdownList.Prefix(level)));
                        AddInlines(paragraph.Inlines, paragraphBlock.Inline, fontSize);
                        if (first && task) NormalizeTaskSpacing(paragraph);
                        MarkdownList.Apply(paragraph, level, marker, fontSize);
                        blocks.Add(paragraph);
                        first = false;
                        continue;
                    }
                    var nested = child as ListBlock;
                    if (nested != null) { AddListBlocks(blocks, nested, fontSize, level + 1); first = false; continue; }
                    AddBlock(blocks, child, fontSize);
                    first = false;
                }
                if (first)
                {
                    var empty = new Paragraph();
                    if (level > 0) empty.Inlines.Add(new Run(MarkdownList.Prefix(level)));
                    if (task) { empty.Inlines.Add(CheckboxGlyph(false)); empty.Inlines.Add(new Run(" ")); }
                    else empty.Inlines.Add(new Run(marker + " "));
                    MarkdownList.Apply(empty, level, marker, fontSize);
                    blocks.Add(empty);
                }
            }
        }

        private static void NormalizeTaskSpacing(Paragraph paragraph)
        {
            Run previous = null;
            foreach (var inline in paragraph.Inlines.ToArray())
            {
                var run = inline as Run;
                if (previous != null && run != null && (previous.Text == "☐ " || previous.Text == "☑ ") && run.Text.StartsWith(" ")) run.Text = run.Text.Substring(1);
                previous = run;
            }
        }

        private static string TaskMarker(ListItemBlock item)        {
            foreach (var paragraph in item.OfType<ParagraphBlock>())
            {
                if (paragraph.Inline == null) continue;
                foreach (var inline in paragraph.Inline) { var checkedMarker = FindTask(inline); if (checkedMarker.HasValue) return checkedMarker.Value ? "☑" : "☐"; }
            }
            return "☐";
        }

        private static bool? FindTask(MdInline inline)
        {
            var task = inline as TaskList;
            if (task != null) return task.Checked;
            var container = inline as ContainerInline;
            if (container != null) foreach (var child in container) { var value = FindTask(child); if (value.HasValue) return value; }
            return null;
        }

        private static void AddBlockToList(ListItem item, MdBlock block, double baseFontSize)
        {
            var produced = new List<WpfBlock>(); AddBlock(produced, block, baseFontSize);
            foreach (var child in produced) item.Blocks.Add(child);
        }

        // 旧便签的 WPF List 在读取时转换为统一的 Markdown 列表段落，避免旧缩进 / 块间距继续出现。
        public static void ConvertLists(FlowDocument document, double fontSize)
        {
            if (document == null) return;
            foreach (var block in document.Blocks.ToArray()) { var list = block as List; if (list != null) ConvertListInto(document, list, Math.Max(1, fontSize), 0, block, true); }
        }

        private static WpfBlock ConvertListInto(FlowDocument document, List list, double fontSize, int level, WpfBlock anchor, bool removeList)
        {
            int number = list.StartIndex > 0 ? list.StartIndex : 1;
            bool ordered = list.MarkerStyle == TextMarkerStyle.Decimal || list.MarkerStyle == TextMarkerStyle.LowerLatin || list.MarkerStyle == TextMarkerStyle.UpperLatin || list.MarkerStyle == TextMarkerStyle.LowerRoman || list.MarkerStyle == TextMarkerStyle.UpperRoman;
            foreach (ListItem item in list.ListItems.ToArray())
            {
                foreach (var child in item.Blocks.ToArray())
                {
                    var paragraph = child as Paragraph;
                    if (paragraph != null)
                    {
                        item.Blocks.Remove(paragraph);
                        string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimStart();
                        string marker;
                        if (text.StartsWith("☐")) marker = "☐";
                        else if (text.StartsWith("☑")) marker = "☑";
                        else if (ordered) marker = (number++).ToString() + ".";
                        else marker = MarkdownList.BulletMarker;
                        string existing;
                        if (!MarkdownList.TryMarker(paragraph, out existing)) new Run(MarkdownList.Prefix(level) + marker + " ", paragraph.ContentStart);
                        else if (level > 0) new Run(MarkdownList.Prefix(level), paragraph.ContentStart);
                        MarkdownList.Apply(paragraph, level, marker, fontSize);
                        paragraph.Margin = new Thickness(0);
                        document.Blocks.InsertAfter(anchor, paragraph); anchor = paragraph;
                        continue;
                    }
                    var nested = child as List;
                    if (nested != null)
                    {
                        item.Blocks.Remove(nested);
                        anchor = ConvertListInto(document, nested, fontSize, level + 1, anchor, true);
                        continue;
                    }
                    item.Blocks.Remove(child);
                    document.Blocks.InsertAfter(anchor, child); anchor = child;
                }
            }
            if (removeList) document.Blocks.Remove(list);
            return anchor;
        }

        private static bool HasTaskList(ContainerBlock item)
        {
            foreach (var child in item)
            {
                var paragraph = child as ParagraphBlock;
                if (paragraph == null || paragraph.Inline == null) continue;
                foreach (var inline in paragraph.Inline) if (ContainsTask(inline)) return true;
            }
            return false;
        }

        private static bool ContainsTask(MdInline inline)
        {
            if (inline is TaskList) return true;
            var container = inline as ContainerInline;
            if (container != null) foreach (var child in container) if (ContainsTask(child)) return true;
            return false;
        }

        private static Paragraph CodeParagraph(LeafBlock block)
        {
            var paragraph = new Paragraph { FontFamily = monoFont, Background = codeBackground, Padding = new Thickness(8, 5, 8, 5) };
            var lines = LinesText(block).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run(lines[i]) { FontFamily = monoFont });
            }
            return paragraph;
        }

        private static string LinesText(LeafBlock block)
        {
            var builder = new StringBuilder();
            var lines = block.Lines;
            for (int i = 0; i < lines.Count; i++) { if (i > 0) builder.Append('\n'); var slice = lines.Lines[i].Slice; if (!slice.IsEmpty && slice.Text != null) builder.Append(slice.Text.Substring(slice.Start, slice.Length)); }
            return builder.ToString();
        }

        private static void StyleQuote(WpfBlock block)
        {
            var paragraph = block as Paragraph;
            if (paragraph != null)
            {
                paragraph.FontStyle = FontStyles.Italic; paragraph.Foreground = UiTheme.Muted;
                paragraph.TextIndent = 0; paragraph.Padding = new Thickness(12, 0, 0, 0); paragraph.BorderThickness = new Thickness(3, 0, 0, 0); paragraph.BorderBrush = UiTheme.Muted;
                return;
            }
            var list = block as List;
            if (list != null) foreach (ListItem item in list.ListItems) foreach (var child in item.Blocks) StyleQuote(child);
        }

        public static List<WpfInline> RenderInlines(ContainerInline container, double baseFontSize)
        {
            var result = new List<WpfInline>();
            if (container == null) return result;
            foreach (var child in container) result.Add(RenderInline(child, baseFontSize));
            return result;
        }

        private static void AddInlines(InlineCollection target, ContainerInline container, double baseFontSize)
        {
            if (container == null) return;
            foreach (var inline in container) target.Add(RenderInline(inline, baseFontSize));
        }

        private static WpfInline RenderInline(MdInline inline, double baseFontSize)
        {
            var literal = inline as LiteralInline;
            if (literal != null) return new Run(literal.Content.Text == null || literal.Content.Length <= 0 ? "" : literal.Content.Text.Substring(literal.Content.Start, literal.Content.Length));
            var code = inline as CodeInline;
            if (code != null) return new Run(code.Content ?? "") { FontFamily = monoFont, Background = codeBackground };
            var emphasis = inline as EmphasisInline;
            if (emphasis != null)
            {
                var span = new Span();
                bool strikethrough = emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2;
                if (strikethrough) span.TextDecorations = TextDecorations.Strikethrough;
                if (!strikethrough && emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
                if (emphasis.DelimiterCount == 1 || emphasis.DelimiterCount >= 3) span.FontStyle = FontStyles.Italic;
                AddInlines(span.Inlines, emphasis, baseFontSize);
                return span;
            }
            var link = inline as LinkInline;
            if (link != null)
            {
                if (link.IsImage)
                {
                    var alt = new Span(); AddInlines(alt.Inlines, link, baseFontSize);
                    if (alt.Inlines.Count == 0) alt.Inlines.Add(new Run("[image]"));
                    return alt;
                }
                var hyperlink = new Hyperlink();
                AddInlines(hyperlink.Inlines, link, baseFontSize);
                if (hyperlink.Inlines.Count == 0) hyperlink.Inlines.Add(new Run(link.Url ?? ""));
                Uri uri; if (!string.IsNullOrWhiteSpace(link.Url) && Uri.TryCreate(link.Url, UriKind.Absolute, out uri) && Allowed(uri)) hyperlink.NavigateUri = uri;
                if (!string.IsNullOrWhiteSpace(link.Url)) hyperlink.ToolTip = link.Url;
                return hyperlink;
            }
            var auto = inline as AutolinkInline;
            if (auto != null)
            {
                var hyperlink = new Hyperlink(new Run(auto.Url ?? ""));
                Uri uri; if (!string.IsNullOrWhiteSpace(auto.Url) && Uri.TryCreate(auto.IsEmail ? "mailto:" + auto.Url : auto.Url, UriKind.Absolute, out uri) && Allowed(uri)) hyperlink.NavigateUri = uri;
                hyperlink.ToolTip = auto.Url;
                return hyperlink;
            }
            var lineBreak = inline as LineBreakInline;
            if (lineBreak != null) return lineBreak.IsHard ? new LineBreak() : new Run(" ");
            var html = inline as HtmlInline;
            if (html != null) return new Run(html.Tag ?? "");
            var entity = inline as HtmlEntityInline;
            if (entity != null) return new Run(entity.Transcoded.Text == null || entity.Transcoded.Length <= 0 ? "" : entity.Transcoded.Text.Substring(entity.Transcoded.Start, entity.Transcoded.Length));
            var task = inline as TaskList;
            if (task != null) return CheckboxGlyph(task.Checked);
            var nested = inline as ContainerInline;
            if (nested != null) { var span = new Span(); AddInlines(span.Inlines, nested, baseFontSize); return span; }
            return new Run("");
        }

        private static bool Allowed(Uri uri) { return uri != null && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto || uri.Scheme == Uri.UriSchemeFile); }
    }
}
