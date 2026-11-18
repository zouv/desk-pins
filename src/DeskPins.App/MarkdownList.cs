using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace DeskPins.App
{
    // Markdown 列表使用普通段落 + 行首标记，而不是 WPF List：可精确控制左缘、悬挂缩进和上下间距，并随 RTF 保存。
    public static class MarkdownList
    {
        public const string BulletMarker = "•";
        // 零宽空格仅用于把嵌套层级写进 RTF（段落 Padding / TextIndent 不随 RTF 保存）；PlainText 与导出会剔除。
        private const char ZeroWidth = '​';
        private static readonly Regex markerPattern = new Regex("^(?:​)*(•|☐|☑|\\d{1,9}\\.)\\s+", RegexOptions.Compiled);

        public static string Prefix(int level) { return new string(ZeroWidth, Math.Max(0, level)); }
        public static int LevelFromPrefix(Paragraph paragraph)
        {
            if (paragraph == null) return 0;
            string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimStart('\r', '\n');
            int level = 0; while (level < text.Length && text[level] == ZeroWidth) level++;
            return level;
        }

        public static bool TryMarker(Paragraph paragraph, out string marker)
        {
            marker = null;
            if (paragraph == null) return false;
            string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimStart('\r', '\n');
            var match = markerPattern.Match(text);
            if (!match.Success) return false;
            marker = match.Groups[1].Value;
            return true;
        }

        public static double Indent(int level, string marker, double fontSize)
        {
            fontSize = Math.Max(1, fontSize);
            double baseIndent = marker == BulletMarker ? 1.0 : (marker == "☐" || marker == "☑" ? 1.25 : 1.3);
            return Math.Round(fontSize * baseIndent, MidpointRounding.AwayFromZero) + Math.Max(0, level) * Step(fontSize);
        }

        public static void Apply(Paragraph paragraph, int level, string marker, double fontSize)
        {
            if (paragraph == null) return;
            double indent = Indent(level, marker, fontSize);
            paragraph.Margin = new Thickness(0, 1, 0, 1);
            paragraph.Padding = new Thickness(indent, 0, 0, 0);
            paragraph.TextIndent = -indent;
        }

        public static void SetLevel(Paragraph paragraph, int level, string marker, double fontSize)
        {
            Apply(paragraph, level, marker, fontSize);
            var first = paragraph == null ? null : paragraph.Inlines.FirstInline as Run;
            if (first == null || first.Text == null) return;
            int zeros = 0; while (zeros < first.Text.Length && first.Text[zeros] == ZeroWidth) zeros++;
            first.Text = Prefix(level) + first.Text.Substring(zeros);
        }

        public static int Level(Paragraph paragraph, string marker, double fontSize)
        {
            if (paragraph == null) return 0;
            double baseIndent = Indent(0, marker, fontSize);
            return Math.Max(0, (int)Math.Round((paragraph.Padding.Left - baseIndent) / Step(fontSize), MidpointRounding.AwayFromZero));
        }

        // 每级缩进按约四个空格的宽度计算，供 Tab / Shift+Tab 使用。
        public static double Step(double fontSize) { return Math.Max(8, Math.Round(Math.Max(1, fontSize) * 1.3, MidpointRounding.AwayFromZero)); }

        public static void ReplaceMarker(Paragraph paragraph, string marker)
        {
            var first = paragraph == null ? null : paragraph.Inlines.FirstInline as Run;
            if (first == null || string.IsNullOrEmpty(first.Text)) return;
            var match = markerPattern.Match(first.Text);
            if (!match.Success) return;
            int prefix = match.Groups[1].Index;
            first.Text = first.Text.Substring(0, prefix) + marker + " " + first.Text.Substring(match.Length);
        }

        public static string NextMarker(string marker)
        {
            if (marker == "☐" || marker == "☑") return "☐";
            int number;
            if (int.TryParse(marker.TrimEnd('.'), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return (number + 1).ToString(CultureInfo.InvariantCulture) + ".";
            return BulletMarker;
        }

        public static string DisplayMarker(string marker)
        {
            if (marker == BulletMarker) return "- ";
            if (marker == "☐") return "- [ ] ";
            if (marker == "☑") return "- [x] ";
            return marker + " ";
        }
    }
}
