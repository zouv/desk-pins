using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class SettingsWindow : Window
    {
        private static readonly string[] categoryNames = { "应用", "桌面", "外观", "内容", "提醒", "网络", "邮件", "快捷键", "数据" };
        private static readonly string[] categoryIcons = { "", "", "", "", "", "", "", "", "" };
        private static readonly Dictionary<string, string> shortcutLabels = new Dictionary<string, string>
        {
            {"paste", "粘贴"}, {"copy", "复制"}, {"cut", "剪切"}, {"cycle", "切换到下一张便签"}, {"cycle-back", "切换到上一张便签"},
            {"new", "新建便签"}, {"new-color", "新建并沿用当前颜色"}, {"close", "关闭便签（可恢复）"}, {"delete", "永久删除便签"},
            {"top", "切换置顶"}, {"title", "修改标题"}, {"lock", "锁定内容"}, {"roll", "卷起 / 展开"}, {"alarm", "设置闹钟"},
            {"sleep", "发送到休眠"}, {"repeat", "设置重复提醒"}, {"tomorrow", "明天唤醒"}, {"manage", "管理便签"}, {"settings", "应用设置"}, {"friends", "联系人"},
            {"save", "另存为"}, {"save-all", "立即保存全部便签"}, {"print", "打印"}, {"find", "查找文本"}, {"find-next", "查找下一个"},
            {"paste-plain", "粘贴纯文本"}, {"copy-plain", "复制纯文本"}, {"convert-image", "将空便签转换为图片"},
            {"bold", "粗体"}, {"italic", "斜体"}, {"underline", "下划线"}, {"strike", "删除线"}, {"bullets", "项目符号"},
            {"checkbox", "插入复选框"}, {"link", "插入链接"}, {"date", "插入日期时间"}, {"rule", "插入水平线"},
            {"undo", "撤销"}, {"redo", "重做"}, {"size", "设置便签尺寸"}, {"width", "根据文本调整右侧宽度"}, {"skin", "设置皮肤"},
            {"transfer", "发送到传输文件"}, {"source", "编辑发送来源"}, {"log", "查看活动日志"}, {"add-friend", "从便签添加联系人"},
            {"send-many", "发送给多个联系人"}, {"reply", "回复发送者"}, {"reply-secret", "以秘密便签回复"}, {"quote", "引用全部文本"},
            {"width-left", "根据文本调整左侧宽度"}, {"sync-friends", "同步联系人列表"}, {"edit-friends", "编辑联系人来源"},
            {"format-copy", "复制格式"}, {"format-paste", "粘贴格式"}, {"skin-reset", "恢复默认皮肤"}
        };
        private readonly AppHost host;
        private readonly Settings settings;
        private readonly string originalLanguage;
        private readonly bool originalAutoStart;
        private readonly List<Func<string>> validators = new List<Func<string>>();
        private ListBox navigation;
        private ContentControl pageHost;
        private int category;
        private bool rebuilding, saved, loadingStyle;
        private string smtpPassword = "";
        private string pendingDataDirectory;

        public SettingsWindow(AppHost host)
        {
            this.host = host;
            pendingDataDirectory = host.DataDirectory;
            var current = host.Library.Catalog.Settings;
            originalLanguage = string.IsNullOrWhiteSpace(current.Language) ? Localization.Chinese : current.Language;
            originalAutoStart = current.AutoStart;
            settings = Json.Read<Settings>(Json.Bytes(current)) ?? new Settings();
            settings.Normalize();
            Ui.LoadLanguage(settings.Language);
            Width = 940; Height = 680; MinWidth = 800; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = true;
            Ui.FitDialog(this, 800, 520);
            BuildWindow();
            Closed += (s, e) => { if (!saved) { try { Ui.LoadLanguage(originalLanguage); } catch { } } };
        }

        private void BuildWindow()
        {
            rebuilding = true;
            Title = Ui.Text("DeskPins 设置");
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(body);

            navigation = new ListBox
            {
                BorderThickness = new Thickness(0), Background = Ui.Brush("#F7F8FA"), Padding = new Thickness(10, 14, 10, 14),
                ItemContainerStyle = NavigationStyle(), HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            navigation.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            for (int i = 0; i < categoryNames.Length; i++) navigation.Items.Add(NavigationItem(i));
            navigation.SelectionChanged += (s, e) => { if (rebuilding) return; category = Math.Max(0, navigation.SelectedIndex); ShowPage(); };
            var navigationHost = new Border { Background = Ui.Brush("#F7F8FA"), BorderBrush = Ui.Brush("#E3E6EB"), BorderThickness = new Thickness(0, 0, 1, 0), Child = navigation };
            body.Children.Add(navigationHost);

            pageHost = new ContentControl { Margin = new Thickness(32, 22, 32, 18), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(pageHost, 1);
            body.Children.Add(pageHost);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = Ui.Button("保存", Save); save.IsDefault = true; save.MinWidth = 84; buttons.Children.Add(save);
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; cancel.MinWidth = 84; buttons.Children.Add(cancel);
            var footer = new Border { Background = Brushes.White, BorderBrush = Ui.Brush("#E3E6EB"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10, 22, 10), Child = buttons };
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            Content = root;
            rebuilding = false;
            navigation.SelectedIndex = Math.Max(0, Math.Min(category, navigation.Items.Count - 1));
        }

        private ListBoxItem NavigationItem(int index)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock
            {
                Text = categoryIcons[index], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
                Width = 30, VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = Ui.Text(categoryNames[index]), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = row, Tag = index };
            AutomationProperties.SetName(item, Ui.Text(categoryNames[index]));
            return item;
        }

        private static Style NavigationStyle()
        {
            var root = new FrameworkElementFactory(typeof(Border), "root");
            root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            root.SetValue(Border.PaddingProperty, new Thickness(0, 9, 0, 9));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var accent = new FrameworkElementFactory(typeof(Border), "accent");
            accent.SetValue(FrameworkElement.WidthProperty, 3d);
            accent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            accent.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            accent.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            accent.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 10, 0));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(accent); grid.AppendChild(presenter);
            root.AppendChild(grid);
            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = root };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#EFF4FA"), "root"));
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#E6F0FA"), "root"));
            selected.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#2F6DA8"), "accent"));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("#1B4F72")));
            template.Triggers.Add(hover); template.Triggers.Add(selected);
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("#374151")));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 9, 0, 9)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            return style;
        }

        private void ShowPage()
        {
            if (pageHost == null) return;
            validators.Clear();
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            header.Children.Add(new TextBlock { Text = Ui.Text(categoryNames[category]), FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#1F2937") });
            header.Children.Add(new Border { Height = 1, Background = Ui.Brush("#E5E7EB"), Margin = new Thickness(0, 10, 0, 0) });
            layout.Children.Add(header);
            FrameworkElement content;
            if (category == 7)
            {
                content = BuildShortcutsPage();
            }
            else
            {
                content = new ScrollViewer
                {
                    Content = BuildCategoryPage(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
            }
            Grid.SetRow(content, 1);
            layout.Children.Add(content);
            pageHost.Content = layout;
        }

        private FrameworkElement BuildCategoryPage()
        {
            var panel = new StackPanel { MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 12, 20) };
            switch (category)
            {
                case 0: BuildApplicationPage(panel); break;
                case 1: BuildDesktopPage(panel); break;
                case 2: BuildAppearancePage(panel); break;
                case 3: BuildContentPage(panel); break;
                case 4: BuildAlertsPage(panel); break;
                case 5: BuildNetworkPage(panel); break;
                case 6: BuildEmailPage(panel); break;
                case 7: BuildShortcutsPage(); break;
                case 8: BuildDataPage(panel); break;
            }
            return panel;
        }

        private void BuildApplicationPage(Panel panel)
        {
            var options = new List<LanguageOption>
            {
                new LanguageOption(Localization.Chinese, "中文（简体）"),
                new LanguageOption(Localization.English, "English")
            };
            bool custom = settings.Language != Localization.Chinese && settings.Language != Localization.English;
            if (custom) options.Add(new LanguageOption(settings.Language, Ui.Text("自定义语言文件") + "：" + Path.GetFileName(settings.Language)));
            options.Add(new LanguageOption("@custom", Ui.Text("自定义语言文件…")));
            var language = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", Height = 30, SelectedItem = options.FirstOrDefault(o => o.Code == settings.Language) ?? options[0] };
            language.SelectionChanged += (s, e) =>
            {
                if (rebuilding) return;
                var option = language.SelectedItem as LanguageOption;
                if (option == null) return;
                if (option.Code == "@custom")
                {
                    var file = Ui.OpenFile(Ui.Text("语言文件") + "|*.json");
                    if (file == null) { BuildWindow(); return; }
                    ApplyLanguage(file);
                    return;
                }
                ApplyLanguage(option.Code);
            };
            Ui.Field(panel, "界面语言", language);
            panel.Children.Add(Hint("切换语言后立即预览；保存后应用到整个应用。"));
            if (custom) panel.Children.Add(Hint(settings.Language));

            Check(panel, "开机启动", settings.AutoStart, v => settings.AutoStart = v);
            Check(panel, "默认新建图片便签", settings.DefaultKind == NoteKind.Image, v => settings.DefaultKind = v ? NoteKind.Image : NoteKind.Text);
            Text(panel, "传输文件", settings.TransferFile, v => settings.TransferFile = v);
            Number(panel, "自动备份间隔（小时，0=关闭）", settings.BackupHours, v => settings.BackupHours = v);
            Number(panel, "Closed 保留天数（0=一直保留）", settings.ClosedRetentionDays, v => settings.ClosedRetentionDays = v);
            Check(panel, "紧凑管理列表", settings.CompactList, v => settings.CompactList = v);
            Check(panel, "管理列表显示最后修改日期", settings.ManagerModifiedDates, v => settings.ManagerModifiedDates = v);
        }

        private void BuildDesktopPage(Panel panel)
        {
            var grid = SettingsGrid();
            SwitchRow(grid, "启动时隐藏全部便签", settings.StartHidden, v => settings.StartHidden = v);
            SwitchRow(grid, "关闭便签前确认", settings.ConfirmClose, v => settings.ConfirmClose = v);
            SwitchRow(grid, "点击一张便签时显示全部便签", settings.ClickMakesVisible, v => settings.ClickMakesVisible = v);
            AddSection(grid, "吸附");
            SwitchRow(grid, "移动和缩放时吸附便签", settings.Snap, v => settings.Snap = v);
            NumberControl(grid, "吸附距离", settings.SnapDistance, v => settings.SnapDistance = v, 0, 100, 70);
            NumberControl(grid, "吸附间隙", settings.SnapGap, v => settings.SnapGap = v, 0, 100, 70);
            AddRow(grid, "双击便签标题栏", ChoiceBox(new[]
            {
                new Choice("none", Ui.Text("不执行操作")),
                new Choice("roll", Ui.Text("卷起 / 展开")),
                new Choice("manage", Ui.Text("打开管理便签"))
            }, settings.DoubleClickAction, v => settings.DoubleClickAction = v));
            SwitchRow(grid, "卷起的便签使用独立屏幕位置", settings.RolledDifferentPosition, v => settings.RolledDifferentPosition = v);
            SwitchRow(grid, "锁定内容时禁止移动和缩放", settings.LockPreventsMove, v => settings.LockPreventsMove = v);
            SwitchRow(grid, "编辑透明便签时临时不透明", settings.OpaqueEditing, v => settings.OpaqueEditing = v);
            SwitchRow(grid, "结束编辑后恢复透明（不渐变）", settings.RestoreOpacityAfterEditing, v => settings.RestoreOpacityAfterEditing = v);
            SwitchRow(grid, "避免便签保存到屏幕外", settings.KeepOnScreen, v => settings.KeepOnScreen = v);
            panel.Children.Add(grid);
        }

        private void BuildAppearancePage(Panel panel)
        {
            var grid = SettingsGrid();
            Action UpdateSample = null;
            int current = 0;
            var styleItems = Enumerable.Range(0, 10).Select(i => i + "  " + settings.Styles[i].Name).ToList();
            var styles = new ComboBox { ItemsSource = styleItems, SelectedIndex = 0, Width = 280, Height = 28 };
            AddRow(grid, "样式", styles);
            AddHintRow(grid, "选择样式后修改下方设置。");
            var name = TextControl(settings.Styles[0].Name, v => { if (!loadingStyle) { settings.Styles[current].Name = v; styleItems[current] = current + "  " + v; } }, 280);
            AddRow(grid, "名称", name);
            var fontColor = ColorButton(settings.Styles[0].Foreground, v => { if (!loadingStyle) { settings.Styles[current].Foreground = v; UpdateSample(); } });
            AddRow(grid, "字体颜色", fontColor);
            var fontStyle = new Button { Content = "Aa", Width = 54, Height = 28, FontSize = 16, ToolTip = Ui.Text("选择字体") };
            AddRow(grid, "字体样式", fontStyle);
            var noteColor = ColorButton(settings.Styles[0].Color, v => { if (!loadingStyle) { settings.Styles[current].Color = v; UpdateSample(); } });
            AddRow(grid, "便签颜色", noteColor);
            var onTop = new CheckBox { Content = Ui.Text("置顶"), VerticalAlignment = VerticalAlignment.Center };
            AddRow(grid, "置顶", onTop);
            var opacity = new ComboBox { Width = 100, Height = 28, ItemsSource = Enumerable.Range(2, 9).Select(i => (i * 10) + "%").ToList(), SelectedIndex = 7 };
            AddRow(grid, "不透明度", opacity);
            var tabWidth = NumberBox("Tab 宽度（twips）", settings.Styles[0].TabWidth, v => { if (!loadingStyle) settings.Styles[current].TabWidth = v; }, 1, 10000, 70);
            AddRow(grid, "Tab 宽度（twips）", tabWidth);
            var sampleText = new TextBlock { Text = "Sample Text\nQuick brown fox", TextWrapping = TextWrapping.Wrap, Padding = new Thickness(7, 5, 7, 5) };
            var sample = new Border { Child = sampleText, MinHeight = 64, BorderBrush = Ui.Brush("#E5E7EB"), BorderThickness = new Thickness(1), Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
            AddRow(grid, "示例", sample);
            SwitchRow(grid, "在便签和堆叠下显示阴影", settings.ShowShadow, v => settings.ShowShadow = v);
            var skinPanel = new DockPanel { Width = 360 };
            var skinBrowse = new Button { Content = "…", Width = 32, Height = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = Ui.Text("浏览…") };
            DockPanel.SetDock(skinBrowse, Dock.Right);
            var skin = TextControl(settings.SkinPath, v => settings.SkinPath = v, 280);
            skinBrowse.Click += (s, e) => { var picked = Ui.OpenFile("便签皮肤|*.json"); if (picked != null) skin.Text = picked; };
            skinPanel.Children.Add(skinBrowse); skinPanel.Children.Add(skin);
            AddRow(grid, "皮肤文件名", skinPanel);
            panel.Children.Add(grid);

            UpdateSample = () =>
            {
                var style = settings.Styles[current];
                sample.Background = Ui.Brush(style.Color);
                sampleText.Foreground = Ui.Brush(style.Foreground);
                sampleText.FontFamily = new FontFamily(style.FontName);
                sampleText.FontSize = Math.Max(6, style.FontSize);
            };
            Action load = () =>
            {
                loadingStyle = true;
                current = Math.Max(0, styles.SelectedIndex); var style = settings.Styles[current];
                name.Text = style.Name; fontColor.Background = Ui.Brush(style.Foreground); fontColor.ToolTip = style.Foreground;
                noteColor.Background = Ui.Brush(style.Color); noteColor.ToolTip = style.Color;
                onTop.IsChecked = style.Topmost; tabWidth.Text = style.TabWidth.ToString(CultureInfo.InvariantCulture);
                int percent = (int)Math.Round(style.Opacity * 10) * 10; percent = Math.Max(20, Math.Min(100, percent));
                opacity.SelectedItem = percent + "%";
                fontStyle.ToolTip = Ui.Text("选择字体") + "：" + style.FontName + " " + style.FontSize.ToString("0.#");
                loadingStyle = false; UpdateSample();
            };
            Action store = () =>
            {
                if (loadingStyle) return;
                var style = settings.Styles[current];
                style.Name = name.Text.Trim(); style.Topmost = onTop.IsChecked == true;
                int percent; if (int.TryParse((opacity.SelectedItem as string ?? "100%").TrimEnd('%'), out percent)) style.Opacity = Math.Max(.1, Math.Min(1, percent / 100d));
                int tab; if (int.TryParse(tabWidth.Text.Trim(), out tab) && tab > 0) style.TabWidth = tab;
                UpdateSample();
            };
            styles.SelectionChanged += (s, e) => { if (loadingStyle) return; store(); load(); };
            name.TextChanged += (s, e) => store();
            onTop.Checked += (s, e) => store(); onTop.Unchecked += (s, e) => store();
            opacity.SelectionChanged += (s, e) => store();
            tabWidth.TextChanged += (s, e) => store();
            fontStyle.Click += (s, e) =>
            {
                try
                {
                    var style = settings.Styles[current];
                    using (var dialog = new System.Windows.Forms.FontDialog { ShowColor = true, Font = new System.Drawing.Font(style.FontName, (float)(style.FontSize * .75)) })
                    {
                        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                        style.FontName = dialog.Font.Name; style.FontSize = dialog.Font.Size * 4 / 3;
                        style.Foreground = System.Drawing.ColorTranslator.ToHtml(dialog.Color);
                        load(); store();
                    }
                }
                catch (Exception ex) { Ui.Error(ex); }
            };
            load();
        }

        private void BuildAlertsPage(Panel panel)
        {
            var grid = SettingsGrid();
            var wakeTime = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            wakeTime.Children.Add(NumberBox("明天唤醒小时", settings.TomorrowHour, v => settings.TomorrowHour = v, 0, 23, 58));
            wakeTime.Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), FontWeight = FontWeights.SemiBold });
            wakeTime.Children.Add(NumberBox("明天唤醒分钟", settings.TomorrowMinute, v => settings.TomorrowMinute = v, 0, 59, 58));
            AddRow(grid, "明天唤醒时间", wakeTime);
            SwitchRow(grid, "避免重复的循环便签", settings.PreventDuplicates, v => settings.PreventDuplicates = v);
            AddSection(grid, "唤醒默认值");
            SwitchRow(grid, "播放声音", settings.WakeSoundEnabled, v => settings.WakeSoundEnabled = v);
            AddRow(grid, "文件名", SoundRow(settings.WakeSound, v => settings.WakeSound = v));
            SwitchRow(grid, "触发闹钟", settings.WakeAlarm, v => settings.WakeAlarm = v);
            AddRow(grid, "置顶", ChoiceBox(new[]
            {
                new Choice("current", Ui.Text("使用便签当前设置")),
                new Choice("on", Ui.Text("置顶")),
                new Choice("off", Ui.Text("普通"))
            }, settings.WakeTopmostMode, v => settings.WakeTopmostMode = v));
            AddSection(grid, "响铃闹钟");
            SwitchRow(grid, "播放声音", settings.AlarmSoundEnabled, v => settings.AlarmSoundEnabled = v);
            AddRow(grid, "文件名", SoundRow(settings.AlarmSound, v => settings.AlarmSound = v));
            SwitchRow(grid, "循环播放声音", settings.AlarmLoop, v => settings.AlarmLoop = v);
            SwitchRow(grid, "闹钟抖动", settings.AlarmJiggle, v => settings.AlarmJiggle = v);
            SwitchRow(grid, "闹钟闪烁边框", settings.AlarmFlash, v => settings.AlarmFlash = v);
            NumberControl(grid, "稍后提醒分钟数", settings.SnoozeMinutes, v => settings.SnoozeMinutes = v, 1, 1440, 70);
            panel.Children.Add(grid);
        }

        private void BuildContentPage(Panel panel)
        {
            var grid = SettingsGrid();
            AddSection(grid, "文本便签");
            SwitchRow(grid, "文本中的链接可以点击", settings.LinksClickable, v => settings.LinksClickable = v);
            SwitchRow(grid, "插入复选框时额外留一个空格", settings.CheckboxTrailingSpace, v => settings.CheckboxTrailingSpace = v);
            SwitchRow(grid, "同时按下 Shift 和 Ctrl 切换文字方向", settings.ShiftCtrlDirection, v => settings.ShiftCtrlDirection = v);
            AddSection(grid, "高亮颜色");
            for (int i = 0; i < 7; i++)
            {
                int index = i;
                AddRow(grid, Ui.Text("颜色") + " " + (i + 1), ColorButton(settings.HighlightColors[i], v => settings.HighlightColors[index] = v));
            }
            AddSection(grid, "图片便签");
            AddRow(grid, "屏幕截取界面", ChoiceBox(new[]
            {
                new Choice("Hole in white overlay", Ui.Text("白色遮罩中挖空")),
                new Choice("Dim overlay", Ui.Text("暗色遮罩")),
                new Choice("No overlay", Ui.Text("无遮罩"))
            }, settings.ScreenGrabUi, v => settings.ScreenGrabUi = v));
            NumberControl(grid, "图片撤销层数", settings.ImageUndoLevels, v => settings.ImageUndoLevels = v, 1, 40, 70);
            AddRow(grid, "默认图片保存类型", ChoiceBox(new[]
            {
                new Choice("PNG", "PNG"), new Choice("JPEG", "JPEG"), new Choice("BMP", "BMP"), new Choice("TIFF", "TIFF")
            }, settings.ImageSaveType, v => settings.ImageSaveType = v, 160));
            panel.Children.Add(grid);
        }

        private void BuildDataPage(Panel panel)
        {
            var grid = SettingsGrid();
            var dataText = new TextBlock
            {
                Text = pendingDataDirectory, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Brush("#374151"), ToolTip = pendingDataDirectory
            };
            AddRow(grid, "当前数据目录", dataText);
            var pick = new Button { Content = Ui.Text("浏览…"), Height = 28, Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Left };
            AddRow(grid, "更改数据目录", pick);
            AddHintRow(grid, "更改数据目录后需要重启 DeskPins；目标目录为空时会先复制当前数据库。");
            pick.Click += (s, e) =>
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = pendingDataDirectory, Description = Ui.Text("选择 DeskPins 数据目录") })
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    var target = Path.GetFullPath(dialog.SelectedPath);
                    if (DataLocation.Same(target, pendingDataDirectory)) return;
                    var targetDb = Path.Combine(target, "deskpins.db");
                    if (File.Exists(targetDb))
                    {
                        if (!Ui.Confirm(Ui.Text("目标目录已有 DeskPins 数据库，切换后当前数据不会复制。继续？"))) return;
                    }
                    else if (!Ui.Confirm(Ui.Text("将把当前数据库复制到新目录；保存并重启后生效。继续？"))) return;
                    pendingDataDirectory = target; dataText.Text = target; dataText.ToolTip = target;
                }
            };
            AddSection(grid, "导入与备份");
            var actions = new WrapPanel();
            actions.Children.Add(Ui.Button("导入便签…", () => { var file = Ui.OpenFile("便签与内容|*.dpn;*.sti;*.rtf;*.txt;*.png;*.jpg;*.bmp"); if (file != null) host.Import(file); }));
            actions.Children.Add(Ui.Button("从传输文件导入", () => host.ImportTransfer()));
            actions.Children.Add(Ui.Button("导入 Stickies v10 数据库…", () => host.ImportStickies()));
            actions.Children.Add(Ui.Button("备份…", () => host.Backup()));
            actions.Children.Add(Ui.Button("恢复全部…", () => { if (host.Restore(false)) Close(); }));
            actions.Children.Add(Ui.Button("合并备份…", () => { if (host.Restore(true)) Close(); }));
            actions.Children.Add(Ui.Button("选择性恢复…", () => { var file = Ui.OpenFile("DeskPins 数据库备份|*.dpbackup;*.db"); if (file != null) { var browser = new BackupBrowserWindow(host, file); if (browser.ShowDialog() == true) Close(); } }));
            actions.Children.Add(Ui.Button("救回屏幕外便签", () => host.Rescue()));
            AddFullRow(grid, actions);
            panel.Children.Add(grid);
        }

        private void BuildNetworkPage(Panel panel)
        {
            Check(panel, "启用 TCP 发送 / 接收", settings.Networking, v => settings.Networking = v);
            Number(panel, "监听端口", settings.NetworkPort, v => settings.NetworkPort = v);
            Check(panel, "使用 DNS 识别发送者", settings.UseDns, v => settings.UseDns = v);
            Text(panel, "联系人列表来源（文件或 HTTP URL）", settings.FriendsServer, v => settings.FriendsServer = v);
            panel.Children.Add(Hint("监听只在启用后启动。支持 Stickies #sticky 文本 / RTF / 图片传输格式。"));
        }

        private void BuildEmailPage(Panel panel)
        {
            Check(panel, "使用默认邮件客户端打开正文草稿（附件使用 SMTP）", settings.EmailViaClient, v => settings.EmailViaClient = v);
            Check(panel, "启用 SMTP 邮件发送", settings.EmailEnabled, v => settings.EmailEnabled = v);
            Text(panel, "SMTP 服务器", settings.SmtpHost, v => settings.SmtpHost = v);
            Number(panel, "端口", settings.SmtpPort, v => settings.SmtpPort = v);
            Check(panel, "启用 TLS", settings.SmtpSsl, v => settings.SmtpSsl = v);
            Text(panel, "用户名", settings.SmtpUser, v => settings.SmtpUser = v);
            var password = new PasswordBox { Password = smtpPassword };
            password.PasswordChanged += (s, e) => smtpPassword = password.Password;
            Ui.Field(panel, "密码（留空保留当前值）", password);
            Text(panel, "发件人地址", settings.SmtpFrom, v => settings.SmtpFrom = v);
        }

        private FrameworkElement BuildShortcutsPage()
        {
            var tabs = new TabControl { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0, 4, 0, 0) };
            AutomationProperties.SetName(tabs, "快捷键范围");
            var global = new StackPanel { Margin = new Thickness(0, 14, 0, 20) };
            global.Children.Add(Hint("全局快捷键在 DeskPins 未聚焦时也生效。"));
            global.Children.Add(ShortcutRow("新建便签", "global-new", "Ctrl+Alt+N"));
            global.Children.Add(ShortcutRow("显示 / 隐藏全部便签", "global-toggle", "Ctrl+Alt+H"));
            global.Children.Add(ShortcutRow("管理便签", "global-manage", "Ctrl+Alt+M"));
            var local = new StackPanel { Margin = new Thickness(0, 14, 0, 20) };
            local.Children.Add(Hint("应用内快捷键仅在便签窗口获得焦点时生效。"));
            foreach (var action in AppHost.ShortcutOrder)
            {
                string label;
                if (!shortcutLabels.TryGetValue(action, out label)) label = action;
                string suggested;
                if (!AppHost.DefaultShortcuts.TryGetValue(action, out suggested)) suggested = "";
                local.Children.Add(ShortcutRow(label, action, suggested));
            }
            tabs.Items.Add(HotkeyTab("全局快捷键", global));
            tabs.Items.Add(HotkeyTab("应用内快捷键", local));
            return tabs;
        }

        private static TabItem HotkeyTab(string title, Panel panel)
        {
            return new TabItem
            {
                Header = Ui.Text(title),
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }
            };
        }

        private FrameworkElement ShortcutRow(string label, string action, string suggested)
        {
            string gesture = CurrentGesture(action);
            bool enabled = !string.IsNullOrWhiteSpace(gesture);
            string backup;
            if (string.IsNullOrWhiteSpace(gesture) && settings.DisabledShortcuts.TryGetValue(action, out backup)) gesture = backup;
            if (string.IsNullOrWhiteSpace(gesture)) gesture = suggested;
            bool updating = false;
            Key key = Key.None;

            var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 130 });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            var toggle = Switch(enabled);            AutomationProperties.SetName(toggle, Ui.Text(label) + " " + Ui.Text("打开 / 关闭快捷键"));
            toggle.Tag = action;
            row.Children.Add(toggle);
            var caption = new TextBlock { Text = Ui.Text(label), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("#374151"), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(caption, 1); row.Children.Add(caption);
            var modifiers = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(modifiers, 2); row.Children.Add(modifiers);
            var shift = ModifierBox("Shift"); var win = ModifierBox("Win"); var ctrl = ModifierBox("Ctrl"); var alt = ModifierBox("Alt");
            modifiers.Children.Add(shift); modifiers.Children.Add(win); modifiers.Children.Add(ctrl); modifiers.Children.Add(alt);
            var plus = new TextBlock { Text = "+", Foreground = Ui.Brush("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(plus, 3); row.Children.Add(plus);
            var keyBox = new TextBox { Width = 72, Height = 28, IsReadOnly = false, IsUndoEnabled = false, Focusable = true, IsTabStop = true, TextAlignment = TextAlignment.Center, Cursor = Cursors.Hand, VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = Ui.Brush("#C9CED6"), Background = Brushes.White, Foreground = Ui.Brush("#111827"), ToolTip = Ui.Text("点击后按下按键") };
            AutomationProperties.SetName(keyBox, Ui.Text(label) + " " + Ui.Text("按下新的按键组合"));
            keyBox.Tag = action;
            keyBox.ContextMenu = null;
            InputMethod.SetIsInputMethodEnabled(keyBox, false);
            keyBox.PreviewMouseLeftButtonDown += (s, e) => { keyBox.Focus(); keyBox.SelectAll(); e.Handled = true; };
            keyBox.PreviewTextInput += (s, e) => { e.Handled = true; };
            Grid.SetColumn(keyBox, 4); row.Children.Add(keyBox);
            var container = new Border { Child = row, BorderBrush = Ui.Brush("#EEF0F3"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 7, 0, 7) };

            Action applyParsed = null, commit = null, refreshEnabled = null;
            Action<bool> setEnabled = null;
            applyParsed = () =>
            {
                Key parsedKey; ModifierKeys parsedMods;
                var parsed = Ui.TryParseGesture(gesture, out parsedKey, out parsedMods);
                key = parsed ? parsedKey : Key.None;
                updating = true;
                shift.IsChecked = parsed && (parsedMods & ModifierKeys.Shift) != 0;
                win.IsChecked = parsed && (parsedMods & ModifierKeys.Windows) != 0;
                ctrl.IsChecked = parsed && (parsedMods & ModifierKeys.Control) != 0;
                alt.IsChecked = parsed && (parsedMods & ModifierKeys.Alt) != 0;
                keyBox.Text = key == Key.None ? "" : KeyLabel(key);
                updating = false;
            };
            commit = () =>
            {
                if (updating || key == Key.None) return;
                if (!enabled)
                {
                    enabled = true;
                    updating = true; toggle.IsChecked = true; updating = false;
                    settings.DisabledShortcuts.Remove(action);
                    refreshEnabled();
                }
                var modifierKeys = ModifierKeys.None;
                if (shift.IsChecked == true) modifierKeys |= ModifierKeys.Shift;
                if (win.IsChecked == true) modifierKeys |= ModifierKeys.Windows;
                if (ctrl.IsChecked == true) modifierKeys |= ModifierKeys.Control;
                if (alt.IsChecked == true) modifierKeys |= ModifierKeys.Alt;
                var value = GestureString(key, modifierKeys);
                if (string.IsNullOrWhiteSpace(value)) return;
                gesture = value;
                settings.DisabledShortcuts.Remove(action);
                StoreGesture(action, gesture);
            };
            refreshEnabled = () =>
            {
                updating = true;
                toggle.IsChecked = enabled;
                shift.IsEnabled = win.IsEnabled = ctrl.IsEnabled = alt.IsEnabled = true;
                keyBox.IsEnabled = true;
                container.Opacity = enabled ? 1 : .7;
                updating = false;
            };
            setEnabled = value =>
            {
                enabled = value;
                if (value)
                {
                    if (key == Key.None)
                    {
                        string saved;
                        if (!settings.DisabledShortcuts.TryGetValue(action, out saved) || string.IsNullOrWhiteSpace(saved)) saved = suggested;
                        gesture = saved;
                        applyParsed();
                    }
                    settings.DisabledShortcuts.Remove(action);
                    StoreGesture(action, gesture);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(gesture)) settings.DisabledShortcuts[action] = gesture;
                    StoreGesture(action, "");
                }
                refreshEnabled();
            };

            toggle.Checked += (s, e) => { if (!updating) setEnabled(true); };
            toggle.Unchecked += (s, e) => { if (!updating) setEnabled(false); };
            shift.Checked += (s, e) => commit(); shift.Unchecked += (s, e) => commit();
            win.Checked += (s, e) => commit(); win.Unchecked += (s, e) => commit();
            ctrl.Checked += (s, e) => commit(); ctrl.Unchecked += (s, e) => commit();
            alt.Checked += (s, e) => commit(); alt.Unchecked += (s, e) => commit();
            keyBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
                var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
                if (pressed == Key.LeftShift || pressed == Key.RightShift || pressed == Key.LeftCtrl || pressed == Key.RightCtrl ||
                    pressed == Key.LeftAlt || pressed == Key.RightAlt || pressed == Key.LWin || pressed == Key.RWin || pressed == Key.None)
                {
                    e.Handled = true; return;
                }
                if (pressed == Key.Escape) { keyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; return; }
                if (pressed == Key.Tab && Keyboard.Modifiers == ModifierKeys.Shift) return;
                var value = GestureString(pressed, Keyboard.Modifiers);
                if (string.IsNullOrWhiteSpace(value)) { e.Handled = true; return; }
                if (!enabled) { enabled = true; updating = true; toggle.IsChecked = true; updating = false; settings.DisabledShortcuts.Remove(action); }
                key = pressed; gesture = value;
                updating = true;
                shift.IsChecked = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                win.IsChecked = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;
                ctrl.IsChecked = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                alt.IsChecked = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                updating = false;
                keyBox.Text = KeyLabel(key);
                StoreGesture(action, gesture);
                refreshEnabled();
                e.Handled = true;
            };
            applyParsed();
            refreshEnabled();
            return container;
        }

        private static CheckBox ModifierBox(string text)
        {
            return new CheckBox { Content = text, FontSize = 12, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        }

        private static ToggleButton Switch(bool isChecked)
        {
            var grid = new FrameworkElementFactory(typeof(Grid));            grid.SetValue(FrameworkElement.WidthProperty, 36d);
            grid.SetValue(FrameworkElement.HeightProperty, 20d);
            var track = new FrameworkElementFactory(typeof(Border), "track");
            track.SetValue(Border.BackgroundProperty, Ui.Brush("#C9CED6"));
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var knob = new FrameworkElementFactory(typeof(Border), "knob");
            knob.SetValue(FrameworkElement.WidthProperty, 16d);
            knob.SetValue(FrameworkElement.HeightProperty, 16d);
            knob.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            knob.SetValue(Border.BackgroundProperty, Brushes.White);
            knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            knob.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            knob.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 0));
            grid.AppendChild(track); grid.AppendChild(knob);
            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = grid };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#AEB7C4"), "track"));
            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#2F6DA8"), "track"));
            checkedTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "knob"));
            checkedTrigger.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 0), "knob"));
            template.Triggers.Add(hover); template.Triggers.Add(checkedTrigger);
            return new ToggleButton
            {
                IsChecked = isChecked, Width = 36, Height = 20, Template = template, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
        }

        private void Check(Panel panel, string label, bool value, Action<bool> setter)
        {
            var control = new CheckBox { Content = Ui.Text(label), IsChecked = value, Margin = new Thickness(4, 7, 4, 7), VerticalContentAlignment = VerticalAlignment.Center };
            control.Checked += (s, e) => setter(true);
            control.Unchecked += (s, e) => setter(false);
            panel.Children.Add(control);
        }

        private void Text(Panel panel, string label, string value, Action<string> setter)
        {
            var control = new TextBox { Text = value ?? "" };
            control.TextChanged += (s, e) => setter(control.Text.Trim());
            Ui.Field(panel, label, control);
        }

        private void Number(Panel panel, string label, int value, Action<int> setter)
        {
            var control = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture) };
            control.TextChanged += (s, e) =>
            {
                int number;
                if (int.TryParse(control.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out number) && number >= 0) setter(number);
            };
            validators.Add(() =>
            {
                int number;
                return int.TryParse(control.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out number) && number >= 0
                    ? null : Ui.Text(label) + " " + Ui.Text("必须为非负整数。");
            });
            Ui.Field(panel, label, control);
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock
            {
                Text = Ui.Text(text), TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("#6B7280"),
                FontSize = 12, LineHeight = 18, Margin = new Thickness(2, 4, 2, 12)
            };
        }

        private static Grid SettingsGrid()
        {
            var grid = new Grid { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 12, 20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }
        private static void AddFullRow(Grid grid, FrameworkElement element)
        {
            int row = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(element, row); Grid.SetColumnSpan(element, 2); grid.Children.Add(element);
        }
        private static void AddRow(Grid grid, string label, FrameworkElement control)
        {
            int row = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = Ui.Text(label), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("#374151"), Margin = new Thickness(2, 9, 12, 9), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(text, row); Grid.SetColumn(text, 0); grid.Children.Add(text);
            control.Margin = new Thickness(2, 7, 2, 7); Grid.SetRow(control, row); Grid.SetColumn(control, 1); grid.Children.Add(control);
        }
        private static void AddSection(Grid grid, string title)
        {
            AddFullRow(grid, new TextBlock { Text = Ui.Text(title), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#1F2937"), Margin = new Thickness(2, 16, 2, 4) });
        }
        private static void AddHintRow(Grid grid, string text) { AddFullRow(grid, Hint(text)); }
        private void SwitchRow(Grid grid, string label, bool value, Action<bool> setter)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 8, 2, 8) };
            var toggle = Switch(value); toggle.Margin = new Thickness(0, 0, 10, 0);
            AutomationProperties.SetName(toggle, Ui.Text(label));
            toggle.Checked += (s, e) => setter(true); toggle.Unchecked += (s, e) => setter(false);
            panel.Children.Add(toggle);
            panel.Children.Add(new TextBlock { Text = Ui.Text(label), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("#374151"), TextWrapping = TextWrapping.Wrap });
            AddFullRow(grid, panel);
        }
        private static TextBox TextControl(string value, Action<string> setter, double width = 280)
        {
            var box = new TextBox { Text = value ?? "", Width = width, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 5, 0) };
            box.TextChanged += (s, e) => setter(box.Text.Trim());
            return box;
        }
        private TextBox NumberControl(Grid grid, string label, int value, Action<int> setter, int min = 0, int max = int.MaxValue, double width = 70)
        {
            var box = NumberBox(label, value, setter, min, max, width);
            AddRow(grid, label, box);
            return box;
        }
        private TextBox NumberBox(string label, int value, Action<int> setter, int min = 0, int max = int.MaxValue, double width = 70)
        {
            var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Width = width, Height = 28, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 5, 0) };
            box.TextChanged += (s, e) => { int number; if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out number) && number >= min && number <= max) setter(number); };
            validators.Add(() =>
            {
                int number;
                if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out number) && number >= min && number <= max) return null;
                return max == int.MaxValue ? Ui.Text(label) + " " + Ui.Text("必须为非负整数。") : Ui.Text(label) + " " + string.Format(Ui.Text("必须是 {0}–{1} 之间的整数。"), min, max);
            });
            return box;
        }
        private static ComboBox ChoiceBox(IEnumerable<Choice> options, string selected, Action<string> setter, double width = 280)
        {
            var list = options.ToList(); var box = new ComboBox { ItemsSource = list, DisplayMemberPath = "Label", Width = width, Height = 28, SelectedItem = list.FirstOrDefault(c => c.Code == selected) ?? list.FirstOrDefault() };
            box.SelectionChanged += (s, e) => { var choice = box.SelectedItem as Choice; if (choice != null) setter(choice.Code); };
            return box;
        }
        private static Button ColorButton(string color, Action<string> setter)
        {
            var current = color ?? "#FFFFB4";
            var button = new Button { Width = 54, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0), Background = Ui.Brush(current), BorderBrush = Ui.Brush("#9CA3AF"), BorderThickness = new Thickness(1), ToolTip = current };
            AutomationProperties.SetName(button, Ui.Text("颜色") + " " + current);
            button.Click += (s, e) => { try { var picked = Ui.Color(current); if (picked != null) { current = picked; button.Background = Ui.Brush(picked); button.ToolTip = picked; setter(picked); } } catch (Exception ex) { Ui.Error(ex); } };
            return button;
        }
        private FrameworkElement SoundRow(string file, Action<string> setter)
        {
            var panel = new DockPanel { Width = 360 };
            var play = new Button { Content = "▶", Width = 32, Height = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = Ui.Text("播放") };
            var browse = new Button { Content = "…", Width = 32, Height = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = Ui.Text("浏览…") };
            DockPanel.SetDock(play, Dock.Right); DockPanel.SetDock(browse, Dock.Right);
            var box = TextControl(file, setter, 280);
            browse.Click += (s, e) => { var picked = Ui.OpenFile("WAV|*.wav"); if (picked != null) box.Text = picked; };
            play.Click += (s, e) => PlaySound(box.Text);
            panel.Children.Add(play); panel.Children.Add(browse); panel.Children.Add(box);
            return panel;
        }
        private static void PlaySound(string file)
        {
            try { if (!string.IsNullOrWhiteSpace(file) && File.Exists(file)) { using (var player = new SoundPlayer(file)) player.Play(); } else SystemSounds.Asterisk.Play(); }
            catch (Exception ex) { Ui.Error(ex); }
        }
        private void FillDataDirectoryText(TextBlock text)
        {
            text.Text = pendingDataDirectory;
            text.ToolTip = pendingDataDirectory;
        }

        private string CurrentGesture(string action)
        {
            switch (action)
            {
                case "global-new": return settings.GlobalNewKey ?? "";
                case "global-toggle": return settings.GlobalToggleKey ?? "";
                case "global-manage": return settings.GlobalManageKey ?? "";
                default:
                    string value;
                    return settings.Shortcuts.TryGetValue(action, out value) ? value ?? "" : (AppHost.DefaultShortcuts.TryGetValue(action, out value) ? value : "");
            }
        }

        private void StoreGesture(string action, string gesture)
        {
            gesture = gesture ?? "";
            switch (action)
            {
                case "global-new": settings.GlobalNewKey = gesture; break;
                case "global-toggle": settings.GlobalToggleKey = gesture; break;
                case "global-manage": settings.GlobalManageKey = gesture; break;
                default: settings.Shortcuts[action] = gesture; break;
            }
        }

        private void ApplyLanguage(string code)
        {
            var previous = settings.Language;
            settings.Language = code;
            try { Ui.LoadLanguage(code); }
            catch (Exception ex)
            {
                settings.Language = previous;
                try { Ui.LoadLanguage(previous); } catch { }
                Ui.Error(ex);
            }
            BuildWindow();
        }

        private void Save()
        {
            try
            {
                foreach (var validate in validators)
                {
                    var message = validate();
                    if (!string.IsNullOrWhiteSpace(message)) throw new ArgumentException(message);
                }
                if (!string.IsNullOrWhiteSpace(smtpPassword)) settings.SmtpSecret = host.ProtectSecret(smtpPassword);
                host.ValidateSettings(settings);
                settings.Normalize();
                if (settings.AutoStart != originalAutoStart) host.SetAutoStart(settings.AutoStart);
                var dataDirectoryChanged = !DataLocation.Same(pendingDataDirectory, host.DataDirectory);
                host.Library.Atomic(() => { host.Library.Catalog.Settings = settings; host.Library.SaveCatalog(); });
                if (dataDirectoryChanged) host.ApplyDataDirectory(pendingDataDirectory);
                host.ApplySettings();
                saved = true;
                if (dataDirectoryChanged) MessageBox.Show(Ui.Text("数据目录已更新，重启 DeskPins 后生效。"), "DeskPins");
                Close();
            }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private static string GestureString(Key key, ModifierKeys modifiers)
        {
            return Ui.FormatGesture(key, modifiers);
        }

        private static string KeyLabel(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
            switch (key)
            {
                case Key.Return: return "Enter";
                case Key.Escape: return "Esc";
                case Key.Back: return "Backspace";
                case Key.Space: return "Space";
                case Key.Next: return "PgDn";
                case Key.Prior: return "PgUp";
                case Key.OemPlus: return "+";
                case Key.OemMinus: return "-";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                case Key.OemQuestion: return "/";
                case Key.OemSemicolon: return ";";
                case Key.OemQuotes: return "'";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
                case Key.OemPipe: return "\\";
                case Key.OemTilde: return "`";
            }
            var text = new KeyConverter().ConvertToString(key);
            return string.IsNullOrWhiteSpace(text) ? key.ToString() : text;
        }

        private sealed class Choice
        {
            public string Code { get; private set; }
            public string Label { get; private set; }
            public Choice(string code, string label) { Code = code; Label = label; }
            public override string ToString() { return Label; }
        }
        private sealed class LanguageOption
        {
            public string Code { get; private set; }
            public string Label { get; private set; }
            public LanguageOption(string code, string label) { Code = code; Label = label; }
            public override string ToString() { return Label; }
        }
    }
}
