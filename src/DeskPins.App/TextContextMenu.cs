using System;
using System.Windows;
using System.Windows.Controls;

namespace DeskPins.App
{
    /// <summary>
    /// 桌面便签与管理器正文共用的右键菜单，保证格式 / Markdown / 段落 / 插入 / 查找 / 高亮入口一致。
    /// 窗口相关动作（粘贴、查找等）通过回调注入，菜单本身不依赖 NoteWindow。
    /// </summary>
    internal static class TextContextMenu
    {
        public static ContextMenu Build(TextEditor editor, AppHost host, Action<string> run, Action paste)
        {
            var m = new ContextMenu();
            bool locked = editor.IsReadOnly, hasSelection = !editor.Selection.IsEmpty;
            m.Items.Add(Ui.Item("剪切", () => editor.Cut(), host.ShortcutFor("cut"), enabled: !locked && hasSelection));
            m.Items.Add(Ui.Item("复制", () => editor.Copy(), host.ShortcutFor("copy"), enabled: hasSelection));
            m.Items.Add(Ui.Item("粘贴", paste, host.ShortcutFor("paste"), enabled: !locked && (Clipboard.ContainsText() || Clipboard.ContainsImage())));
            m.Items.Add(Ui.Item("粘贴为纯文本", () => run("paste-plain"), host.ShortcutFor("paste-plain"), enabled: !locked && Clipboard.ContainsText()));
            m.Items.Add(new Separator());
            m.Items.Add(Ui.Item(hasSelection ? "设置所选文字格式…" : "设置全文格式…", () => editor.FontDialog(), enabled: !locked));
            var markdown = Ui.Sub("Markdown");
            var markdownParagraph = editor.CaretPosition == null ? null : editor.CaretPosition.Paragraph;
            int heading = markdownParagraph == null ? 0 : MarkdownRenderer.HeadingLevel(markdownParagraph, editor.Document.FontSize);
            bool quoted = markdownParagraph != null && markdownParagraph.BorderThickness.Left > 0;
            for (int level = 1; level <= 6; level++)
            {
                int value = level;
                markdown.Items.Add(Ui.Item(Ui.Text("标题") + " " + level, () => MarkdownEditing.ApplyHeading(editor, value), check: heading == value, enabled: !locked));
            }
            markdown.Items.Add(Ui.Item("正文", () => MarkdownEditing.ApplyHeading(editor, 0), check: heading == 0, enabled: !locked));
            markdown.Items.Add(new Separator());
            markdown.Items.Add(Ui.Item("粗体", () => editor.Format("bold"), host.ShortcutFor("bold"), enabled: !locked));
            markdown.Items.Add(Ui.Item("斜体", () => editor.Format("italic"), host.ShortcutFor("italic"), enabled: !locked));
            markdown.Items.Add(Ui.Item("删除线", () => editor.Format("strike"), host.ShortcutFor("strike"), enabled: !locked));
            markdown.Items.Add(Ui.Item("行内代码", () => MarkdownEditing.ApplyInlineCode(editor), enabled: !locked && hasSelection));
            markdown.Items.Add(Ui.Item("插入链接…", () => run("link"), host.ShortcutFor("link"), enabled: !locked));
            markdown.Items.Add(new Separator());
            markdown.Items.Add(Ui.Item("项目符号", () => run("bullets"), enabled: !locked));
            markdown.Items.Add(Ui.Item("编号", () => run("numbers"), enabled: !locked));
            markdown.Items.Add(Ui.Item("引用", () => MarkdownEditing.ToggleQuote(editor), check: quoted, enabled: !locked));
            markdown.Items.Add(Ui.Item("任务列表", () => MarkdownEditing.ApplyTaskList(editor), enabled: !locked));
            markdown.Items.Add(Ui.Item("代码块", () => MarkdownEditing.ApplyCodeBlock(editor), enabled: !locked));
            markdown.Items.Add(Ui.Item("插入水平线", () => MarkdownEditing.InsertHorizontalRule(editor), enabled: !locked));
            markdown.Items.Add(new Separator());
            markdown.Items.Add(Ui.Item("应用 Markdown 到所选文字", () => MarkdownEditing.ConvertSelection(editor), enabled: !locked && hasSelection));
            markdown.Items.Add(Ui.Item("粘贴 Markdown", () => MarkdownEditing.PasteMarkdown(editor), enabled: !locked && Clipboard.ContainsText()));
            markdown.Items.Add(Ui.Item("复制为 Markdown", () => MarkdownEditing.CopyMarkdown(editor)));
            m.Items.Add(markdown);
            var paragraph = Ui.Sub("段落");
            foreach (var pair in new[] { new[] { "项目符号", "bullets" }, new[] { "编号", "numbers" }, new[] { "左对齐", "left" }, new[] { "居中", "center" }, new[] { "右对齐", "right" }, new[] { "从左到右", "ltr" }, new[] { "从右到左", "rtl" } }) { var action = pair[1]; paragraph.Items.Add(Ui.Item(pair[0], () => run(action), enabled: !locked)); }
            m.Items.Add(paragraph);
            var insert = Ui.Sub("插入");
            foreach (var pair in new[] { new[] { "插入复选框", "checkbox" }, new[] { "插入链接…", "link" }, new[] { "插入日期时间", "date" } }) { var action = pair[1]; insert.Items.Add(Ui.Item(pair[0], () => run(action), enabled: !locked)); }
            m.Items.Add(insert);
            m.Items.Add(Ui.Item("查找…", () => run("find"), host.ShortcutFor("find")));
            var highlight = Ui.Sub("高亮");
            for (int i = 0; i < host.Library.Catalog.Settings.HighlightColors.Count && i < 7; i++)
            {
                var color = host.Library.Catalog.Settings.HighlightColors[i];
                highlight.Items.Add(Ui.Item(Ui.Text("颜色") + " " + (i + 1), () => editor.Highlight(color), enabled: !locked));
            }
            highlight.Items.Add(new Separator()); highlight.Items.Add(Ui.Item("清除高亮", () => editor.Highlight(null), enabled: !locked));
            m.Items.Add(highlight);
            return m;
        }
    }
}
