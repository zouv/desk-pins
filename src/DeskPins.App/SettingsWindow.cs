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
        private Settings settings;
        // These are the last committed baseline, not necessarily the values at window creation.
        private string originalLanguage;
        private bool originalAutoStart;
        private int originalRetentionDays;
        private readonly Dictionary<string, SettingsFieldState> fields = new Dictionary<string, SettingsFieldState>(StringComparer.Ordinal);
        private readonly List<string> fieldOrder = new List<string>();
        private TextBlock saveStatus;
        private string statusMessage = "";
        private int selectedStyle, shortcutTab;
        private string shortcutSearch = "";
        private bool directoryApplied;
        private ListBox navigation;
        private ContentControl pageHost;
        private int category;
        private bool rebuilding, validationStatus;
        private string smtpPassword = "";
        private string pendingDataDirectory;
        private string appliedDataDirectory;

        public SettingsWindow(AppHost host)
        {
            this.host = host;
            pendingDataDirectory = appliedDataDirectory = host.DataDirectory;
            var current = host.Library.Catalog.Settings;
            originalLanguage = string.IsNullOrWhiteSpace(current.Language) ? Localization.Chinese : current.Language;
            originalAutoStart = current.AutoStart;
            settings = Json.Read<Settings>(Json.Bytes(current)) ?? new Settings();
            settings.Normalize();
            originalRetentionDays = current.ClosedRetentionDays;
            InitializeFields();
            Ui.LoadLanguage(settings.Language);
            Width = 940; Height = 680; MinWidth = 800; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = true;
            Ui.FitDialog(this, 800, 520);
            BuildWindow();
            Closed += (s, e) => { try { Ui.LoadLanguage(originalLanguage); } catch { } };
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
                BorderThickness = new Thickness(0), Background = UiTheme.Background, Padding = new Thickness(10, 14, 10, 14),
                ItemContainerStyle = NavigationStyle(), HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            navigation.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            for (int i = 0; i < categoryNames.Length; i++) navigation.Items.Add(NavigationItem(i));
            navigation.SelectionChanged += (s, e) => { if (rebuilding) return; category = Math.Max(0, navigation.SelectedIndex); ShowPage(); };
            var navigationHost = new Border { Background = UiTheme.Background, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 1, 0), Child = navigation };
            body.Children.Add(navigationHost);

            pageHost = new ContentControl { Margin = new Thickness(32, 22, 32, 18), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(pageHost, 1);
            body.Children.Add(pageHost);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = Ui.PrimaryButton("保存", Save, 84); save.IsDefault = true; buttons.Children.Add(save);
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; cancel.MinWidth = 84; buttons.Children.Add(cancel);
            var footerGrid = new Grid();
            footerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            saveStatus = new TextBlock { Text = statusMessage, TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Danger, Margin = new Thickness(0, 0, 0, 8), Visibility = string.IsNullOrEmpty(statusMessage) ? Visibility.Collapsed : Visibility.Visible };
            var statusScroll = new ScrollViewer { Content = saveStatus, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            footerGrid.Children.Add(statusScroll); Grid.SetRow(buttons, 1); footerGrid.Children.Add(buttons);
            var footer = new Border { Background = UiTheme.Surface, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10, 22, 10), Child = footerGrid };
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            Content = root;
            UiTheme.Apply(this);
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
            selected.Setters.Add(new Setter(Border.BackgroundProperty, UiTheme.Accent, "accent"));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("#1B4F72")));
            template.Triggers.Add(hover); template.Triggers.Add(selected);
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, UiTheme.Text));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 9, 0, 9)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            return style;
        }

        private void ShowPage()
        {
            if (pageHost == null) return;
            foreach (var field in fields.Values) { field.Control = null; field.RefreshFeedback = null; }
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            header.Children.Add(new TextBlock { Text = Ui.Text(categoryNames[category]), FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text });
            header.Children.Add(new Border { Height = 1, Background = UiTheme.Border, Margin = new Thickness(0, 10, 0, 0) });
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
            var panel = new StackPanel { MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 20) };
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
            var grid = SettingsGrid();
            AddRow(grid, "界面语言", language);
            fields["Language"].Control = language;
            AddHintRow(grid, "切换语言后立即预览；保存后应用到整个应用。");
            if (custom) AddFullRow(grid, PathText(settings.Language));
            SwitchRow(grid, "开机启动", settings.AutoStart, v => settings.AutoStart = v);
            SwitchRow(grid, "默认新建图片便签", settings.DefaultKind == NoteKind.Image, v => settings.DefaultKind = v ? NoteKind.Image : NoteKind.Text);
            AddRow(grid, "传输文件", FileRow("TransferFile", "DeskPins 传输文件|*.dpx", true));
            NumberControl(grid, "BackupHours", "自动备份间隔（小时，0=关闭）", settings.BackupHours, v => settings.BackupHours = v);
            AddSection(grid, "管理列表");
            SwitchRow(grid, "紧凑管理列表", settings.CompactList, v => settings.CompactList = v);
            SwitchRow(grid, "管理列表显示最后修改日期", settings.ManagerModifiedDates, v => settings.ManagerModifiedDates = v);
            panel.Children.Add(grid);
        }

        private void BuildDesktopPage(Panel panel)
        {
            var grid = SettingsGrid();
            SwitchRow(grid, "启动时隐藏全部便签", settings.StartHidden, v => settings.StartHidden = v);
            SwitchRow(grid, "关闭便签前确认", settings.ConfirmClose, v => settings.ConfirmClose = v);
            SwitchRow(grid, "点击一张便签时显示全部便签", settings.ClickMakesVisible, v => settings.ClickMakesVisible = v);
            AddSection(grid, "吸附");
            SwitchRow(grid, "移动和缩放时吸附便签", settings.Snap, v => settings.Snap = v);
            NumberControl(grid, "SnapDistance", "吸附距离", settings.SnapDistance, v => settings.SnapDistance = v, 0, 100, 70);
            NumberControl(grid, "SnapGap", "吸附间隙", settings.SnapGap, v => settings.SnapGap = v, 0, 100, 70);
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
            int current = Math.Max(0, Math.Min(selectedStyle, settings.Styles.Count - 1));
            var styleItems = new System.Collections.ObjectModel.ObservableCollection<string>(settings.Styles.Select((style, i) => StyleLabel(i)));
            var styles = new ComboBox { ItemsSource = styleItems, SelectedIndex = current, MinHeight = 30 };
            AddRow(grid, "样式", styles);
            AddHintRow(grid, "第一项是新建便签使用的默认样式；其他样式可从便签菜单应用。");
            var name = TextControl(StyleKey(current, "Name"));
            AddRow(grid, "名称", name);
            Action updateSample = null;
            var fontColor = ColorButton(settings.Styles[current].Foreground, v => { settings.Styles[current].Foreground = v; fields[StyleKey(current, "Foreground")].Raw = v; fields[StyleKey(current, "Foreground")].Check(); updateSample(); });
            fields[StyleKey(current, "Foreground")].Control = fontColor;
            AddRow(grid, "字体颜色", fontColor);
            var fontStyle = Ui.Button("选择字体…", () =>
            {
                try
                {
                    var style = settings.Styles[current];
                    using (var dialog = new System.Windows.Forms.FontDialog
                    {
                        ShowColor = true,
                        Color = System.Drawing.ColorTranslator.FromHtml(style.Foreground),
                        Font = new System.Drawing.Font(style.FontName, (float)(style.FontSize * .75))
                    })
                    {
                        if (dialog.ShowDialog(new NativeDialogOwner(this)) != System.Windows.Forms.DialogResult.OK) return;
                        style.FontName = dialog.Font.Name; style.FontSize = dialog.Font.Size * 4 / 3;
                        style.Foreground = System.Drawing.ColorTranslator.ToHtml(dialog.Color);
                        fields[StyleKey(current, "FontName")].Raw = style.FontName; fields[StyleKey(current, "FontName")].Check();
                        fields[StyleKey(current, "Foreground")].Raw = style.Foreground; fields[StyleKey(current, "Foreground")].Check();
                        fontColor.Background = Ui.Brush(style.Foreground); fontColor.Tag = style.Foreground; fontColor.ToolTip = style.Foreground;
                        updateSample();
                    }
                }
                catch (Exception ex) { Ui.Error(ex, this); }
            });
            fields[StyleKey(current, "FontName")].Control = fontStyle;
            AddRow(grid, "字体样式", fontStyle);
            var noteColor = ColorButton(settings.Styles[current].Color, v => { settings.Styles[current].Color = v; fields[StyleKey(current, "Color")].Raw = v; fields[StyleKey(current, "Color")].Check(); updateSample(); });
            fields[StyleKey(current, "Color")].Control = noteColor;
            AddRow(grid, "便签颜色", noteColor);
            SwitchRow(grid, "置顶", settings.Styles[current].Topmost, v => settings.Styles[current].Topmost = v);
            var opacity = ChoiceBox(Enumerable.Range(2, 9).Select(i => new Choice((i * 10).ToString(CultureInfo.InvariantCulture), (i * 10) + "%")), ((int)Math.Round(settings.Styles[current].Opacity * 10) * 10).ToString(CultureInfo.InvariantCulture), v => { settings.Styles[current].Opacity = int.Parse(v, CultureInfo.InvariantCulture) / 100d; updateSample(); }, 100);
            AddRow(grid, "不透明度", opacity);
            AddRow(grid, "Tab 宽度（twips）", NumberBox(StyleKey(current, "TabWidth"), "Tab 宽度（twips）", settings.Styles[current].TabWidth, v => settings.Styles[current].TabWidth = v, 1, 10000));
            var sampleText = new TextBlock { Text = Ui.Text("示例便签") + "\nQuick brown fox", TextWrapping = TextWrapping.Wrap, Padding = new Thickness(12), MinHeight = 72 };
            var noteSample = new Border { Child = sampleText, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1) };
            var background = new Grid { Background = UiTheme.Selected, MinHeight = 88 };
            background.Children.Add(new TextBlock { Text = Ui.Text("透明度背景示例"), Foreground = UiTheme.Muted, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Bottom });
            background.Children.Add(noteSample);
            AddRow(grid, "示例", background);
            SwitchRow(grid, "在便签和堆叠下显示阴影", settings.ShowShadow, v => settings.ShowShadow = v);
            AddRow(grid, "皮肤文件名", FileRow("SkinPath", "便签皮肤|*.json"));
            panel.Children.Add(grid);
            updateSample = () =>
            {
                var style = settings.Styles[current];
                noteSample.Background = Ui.Brush(style.Color); noteSample.Opacity = style.Opacity;
                sampleText.Foreground = Ui.Brush(style.Foreground);
                sampleText.FontFamily = new FontFamily(style.FontName); sampleText.FontSize = Math.Max(6, style.FontSize);
                fontStyle.ToolTip = style.FontName + " " + style.FontSize.ToString("0.#");
            };
            bool updatingStyleLabel = false;
            name.TextChanged += (s, e) => { updatingStyleLabel = true; styleItems[current] = StyleLabel(current); styles.SelectedIndex = current; updatingStyleLabel = false; };
            styles.SelectionChanged += (s, e) =>
            {
                if (updatingStyleLabel || styles.SelectedIndex < 0 || styles.SelectedIndex == current) return;
                selectedStyle = styles.SelectedIndex; ShowPage();
            };
            updateSample();
        }

        private string StyleLabel(int index)
        {
            var name = fields[StyleKey(index, "Name")].Raw.Trim();
            return index == 0 ? name + " — " + Ui.Text("默认") : name;
        }

        private void BuildAlertsPage(Panel panel)
        {
            var grid = SettingsGrid();
            var wakeTime = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            wakeTime.Children.Add(NumberBox("TomorrowHour", "明天唤醒小时", settings.TomorrowHour, v => settings.TomorrowHour = v, 0, 23, 58));
            wakeTime.Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), FontWeight = FontWeights.SemiBold });
            wakeTime.Children.Add(NumberBox("TomorrowMinute", "明天唤醒分钟", settings.TomorrowMinute, v => settings.TomorrowMinute = v, 0, 59, 58));
            AddRow(grid, "明天唤醒时间", wakeTime);
            SwitchRow(grid, "避免重复的循环便签", settings.PreventDuplicates, v => settings.PreventDuplicates = v);
            AddSection(grid, "唤醒默认值");
            SwitchRow(grid, "播放声音", settings.WakeSoundEnabled, v => settings.WakeSoundEnabled = v);
            AddRow(grid, "文件名", SoundRow("WakeSound"));
            SwitchRow(grid, "触发闹钟", settings.WakeAlarm, v => settings.WakeAlarm = v);
            AddRow(grid, "置顶", ChoiceBox(new[]
            {
                new Choice("current", Ui.Text("使用便签当前设置")),
                new Choice("on", Ui.Text("置顶")),
                new Choice("off", Ui.Text("普通"))
            }, settings.WakeTopmostMode, v => settings.WakeTopmostMode = v));
            AddSection(grid, "响铃闹钟");
            SwitchRow(grid, "播放声音", settings.AlarmSoundEnabled, v => settings.AlarmSoundEnabled = v);
            AddRow(grid, "文件名", SoundRow("AlarmSound"));
            SwitchRow(grid, "循环播放声音", settings.AlarmLoop, v => settings.AlarmLoop = v);
            SwitchRow(grid, "闹钟抖动", settings.AlarmJiggle, v => settings.AlarmJiggle = v);
            SwitchRow(grid, "闹钟闪烁边框", settings.AlarmFlash, v => settings.AlarmFlash = v);
            NumberControl(grid, "SnoozeMinutes", "稍后提醒分钟数", settings.SnoozeMinutes, v => settings.SnoozeMinutes = v, 1, 1440, 70);
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
            var palette = new WrapPanel();
            for (int i = 0; i < 7; i++)
            {
                int index = i;
                var swatch = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
                var button = ColorButton(settings.HighlightColors[i], v => settings.HighlightColors[index] = v);
                AutomationProperties.SetName(button, Ui.Text("高亮颜色") + " " + (i + 1));
                swatch.Children.Add(button);
                swatch.Children.Add(new TextBlock { Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, Foreground = UiTheme.Muted, Margin = new Thickness(0, 4, 0, 0) });
                palette.Children.Add(swatch);
            }
            AddRow(grid, "高亮颜色", palette);
            AddSection(grid, "图片便签");
            AddRow(grid, "屏幕截取界面", ChoiceBox(new[]
            {
                new Choice("Hole in white overlay", Ui.Text("白色遮罩中挖空")),
                new Choice("Dim overlay", Ui.Text("暗色遮罩")),
                new Choice("No overlay", Ui.Text("无遮罩"))
            }, settings.ScreenGrabUi, v => settings.ScreenGrabUi = v));
            NumberControl(grid, "ImageUndoLevels", "图片撤销层数", settings.ImageUndoLevels, v => settings.ImageUndoLevels = v, 1, 40, 70);
            AddRow(grid, "默认图片保存类型", ChoiceBox(new[]
            {
                new Choice("PNG", "PNG"), new Choice("JPEG", "JPEG"), new Choice("BMP", "BMP"), new Choice("TIFF", "TIFF")
            }, settings.ImageSaveType, v => settings.ImageSaveType = v, 160));
            panel.Children.Add(grid);
        }

        private void BuildDataPage(Panel panel)
        {
            var grid = SettingsGrid();
            AddSection(grid, "数据位置");
            AddRow(grid, "当前运行目录", PathText(host.DataDirectory));
            var nextDirectory = PathText(pendingDataDirectory);
            fields["DataDirectory"].Control = nextDirectory;
            AddRow(grid, "下次启动目录", nextDirectory);
            var pick = Ui.Button("浏览…", () =>
            {
                try
                {
                    using (var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = pendingDataDirectory, Description = Ui.Text("选择 DeskPins 数据目录") })
                    {
                        if (dialog.ShowDialog(new NativeDialogOwner(this)) != System.Windows.Forms.DialogResult.OK) return;
                        var target = Path.GetFullPath(dialog.SelectedPath);
                        if (DataLocation.Same(target, pendingDataDirectory)) return;
                        host.ValidateDataDirectory(target);
                        var targetDb = Path.Combine(target, "deskpins.db");
                        if (File.Exists(targetDb))
                        {
                            if (!Ui.Confirm("目标目录已有 DeskPins 数据库，切换后当前数据不会复制。继续？", this)) return;
                        }
                        else if (!Ui.Confirm("将把当前数据库复制到新目录；保存并重启后生效。继续？", this)) return;
                        pendingDataDirectory = target; fields["DataDirectory"].Raw = target; fields["DataDirectory"].Check(); directoryApplied = false; ShowPage();
                    }
                }
                catch (Exception ex) { Ui.Error(ex, this); }
            });
            AddRow(grid, "更改数据目录", pick);
            AddHintRow(grid, directoryApplied ? "数据目录已更新，重启 DeskPins 后生效。" : "更改数据目录后需要重启 DeskPins；目标目录为空时会先复制当前数据库。");
            AddHintRow(grid, "以下即时操作始终作用于当前运行目录，与待保存的设置无关。");
            AddSection(grid, "即时导入");
            var imports = new WrapPanel();
            imports.Children.Add(Ui.Button("导入便签…", () => RunDataAction(() => { var file = Ui.OpenFile("便签与内容|*.dpn;*.sti;*.rtf;*.txt;*.png;*.jpg;*.bmp", this); if (file != null) host.Import(file); })));
            imports.Children.Add(Ui.Button("从传输文件导入", () => RunDataAction(() => host.ImportTransfer())));
            imports.Children.Add(Ui.Button("导入 Stickies v10 数据库…", () => RunDataAction(() => host.ImportStickies())));
            AddFullRow(grid, imports);
            AddSection(grid, "即时备份与恢复");
            AddHintRow(grid, "恢复会先备份当前数据。恢复成功后重新载入设置，未保存的设置草稿将被丢弃。");
            var recovery = new WrapPanel();
            recovery.Children.Add(Ui.Button("备份…", () => RunDataAction(() => host.Backup())));
            recovery.Children.Add(Ui.DangerButton("恢复全部…", () => RestoreDraft(false)));
            recovery.Children.Add(Ui.Button("合并备份…", () => RestoreDraft(true)));
            recovery.Children.Add(Ui.Button("选择性恢复…", () => RunDataAction(() =>
            {
                if (!ConfirmDiscardDraft()) return;
                var file = Ui.OpenFile("DeskPins 数据库备份|*.dpbackup;*.db", this);
                if (file == null) return;
                var browser = new BackupBrowserWindow(host, file) { Owner = this };
                if (browser.ShowDialog() == true) ReloadDraft();
            })));
            AddFullRow(grid, recovery);
            AddSection(grid, "即时维护");
            AddFullRow(grid, Ui.Button("救回屏幕外便签", () => RunDataAction(() => host.Rescue())));
            AddSection(grid, "自动清理");
            NumberControl(grid, "ClosedRetentionDays", "自动永久删除 Closed 便签（保留天数）", settings.ClosedRetentionDays, v => settings.ClosedRetentionDays = v);
            AddHintRow(grid, "0 = 一直保留。大于 0 时自动永久删除超过天数的 Closed 便签，无法再恢复；请先备份。");
            panel.Children.Add(grid);
        }

        private bool ConfirmDiscardDraft()
        {
            return Ui.Confirm("恢复成功后将重新载入设置，未保存的设置草稿（包括数据目录选择）会被丢弃。继续？", this);
        }

        private void RestoreDraft(bool merge)
        {
            RunDataAction(() =>
            {
                if (!ConfirmDiscardDraft()) return;
                var file = Ui.OpenFile("DeskPins 数据库 / 备份|*.dpbackup;*.db", this);
                if (file != null && host.RestoreBackup(file, merge, null, this)) ReloadDraft();
            });
        }

        private void RunDataAction(Action action)
        {
            try { action(); } catch (Exception ex) { Ui.Error(ex, this); }
        }

        private void ReloadDraft()
        {
            var current = host.Library.Catalog.Settings;
            settings = Json.Read<Settings>(Json.Bytes(current)) ?? new Settings(); settings.Normalize();
            originalLanguage = settings.Language; originalAutoStart = settings.AutoStart; originalRetentionDays = settings.ClosedRetentionDays;
            smtpPassword = ""; pendingDataDirectory = appliedDataDirectory; directoryApplied = !DataLocation.Same(appliedDataDirectory, host.DataDirectory);
            selectedStyle = 0; shortcutSearch = ""; InitializeFields();
            string languageError = null;
            try { Ui.LoadLanguage(settings.Language); }
            catch (Exception ex) { languageError = ex.Message; Ui.LoadLanguage(Localization.Chinese); }
            SetStatus(Ui.Text("恢复完成，设置已重新载入；未保存的草稿已丢弃。") + (languageError == null ? "" : "\n" + Ui.Text("语言") + ": " + languageError)); BuildWindow();
        }

        private void BuildNetworkPage(Panel panel)
        {
            var grid = SettingsGrid();
            SwitchRow(grid, "启用 TCP 发送 / 接收", settings.Networking, v => settings.Networking = v);
            NumberControl(grid, "NetworkPort", "监听端口", settings.NetworkPort, v => settings.NetworkPort = v, 1, 65535);
            SwitchRow(grid, "使用 DNS 识别发送者", settings.UseDns, v => settings.UseDns = v);
            AddRow(grid, "联系人列表来源（文件或 HTTP URL）", TextControl("FriendsServer"));
            AddHintRow(grid, "监听只在启用后启动。支持 Stickies #sticky 文本 / RTF / 图片传输格式。");
            panel.Children.Add(grid);
        }

        private void BuildEmailPage(Panel panel)
        {
            var grid = SettingsGrid();
            SwitchRow(grid, "使用默认邮件客户端打开正文草稿（附件使用 SMTP）", settings.EmailViaClient, v => settings.EmailViaClient = v);
            SwitchRow(grid, "启用 SMTP 邮件发送", settings.EmailEnabled, v => settings.EmailEnabled = v);
            AddRow(grid, "SMTP 服务器", TextControl("SmtpHost"));
            NumberControl(grid, "SmtpPort", "端口", settings.SmtpPort, v => settings.SmtpPort = v, 1, 65535);
            SwitchRow(grid, "启用 TLS", settings.SmtpSsl, v => settings.SmtpSsl = v);
            AddRow(grid, "用户名", TextControl("SmtpUser"));
            var password = new PasswordBox { Password = smtpPassword, MinHeight = 30, Padding = new Thickness(6, 3, 6, 3) };
            password.PasswordChanged += (s, e) => smtpPassword = password.Password;
            AddRow(grid, "密码（留空保留当前值）", password);
            AddRow(grid, "发件人地址", TextControl("SmtpFrom"));
            panel.Children.Add(grid);
        }

        private FrameworkElement BuildShortcutsPage()
        {
            CheckShortcutFields();
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var search = new TextBox { Text = shortcutSearch, MinHeight = 30, Margin = new Thickness(0, 0, 0, 12), ToolTip = Ui.Text("搜索快捷键名称或按键") };
            AutomationProperties.SetName(search, Ui.Text("搜索快捷键名称或按键")); root.Children.Add(search);
            var tabs = new TabControl { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0, 4, 0, 0) };
            AutomationProperties.SetName(tabs, Ui.Text("快捷键范围"));
            var searchable = new List<Tuple<FrameworkElement, string, string>>();
            var groups = new List<Tuple<TextBlock, List<FrameworkElement>>>();
            var global = SettingsGrid();
            AddHintRow(global, "全局快捷键在 DeskPins 未聚焦时也生效。");
            AddHintRow(global, "只有开关控制启用；编辑已停用的按键不会自动启用。");
            foreach (var action in new[] { "global-new", "global-toggle", "global-manage" })
            {
                string label = action == "global-new" ? "新建便签" : action == "global-toggle" ? "显示 / 隐藏全部便签" : "管理便签";
                var row = ShortcutRow(label, action, action == "global-new" ? "Ctrl+Alt+N" : action == "global-toggle" ? "Ctrl+Alt+H" : "Ctrl+Alt+M");
                AddFullRow(global, row); searchable.Add(Tuple.Create(row, label, action));
            }
            var local = SettingsGrid();
            AddHintRow(local, "应用内快捷键仅在便签窗口获得焦点时生效。");
            foreach (var group in AppHost.ShortcutOrder.GroupBy(ShortcutGroup))
            {
                var header = new TextBlock { Text = Ui.Text(group.Key), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text, Margin = new Thickness(0, 16, 0, 6) };
                AddFullRow(local, header); var groupRows = new List<FrameworkElement>();
                foreach (var action in group)
                {
                    string label, suggested;
                    if (!shortcutLabels.TryGetValue(action, out label)) label = action;
                    if (!AppHost.DefaultShortcuts.TryGetValue(action, out suggested)) suggested = "";
                    var row = ShortcutRow(label, action, suggested); AddFullRow(local, row);
                    searchable.Add(Tuple.Create(row, label, action)); groupRows.Add(row);
                }
                groups.Add(Tuple.Create(header, groupRows));
            }
            tabs.Items.Add(HotkeyTab("全局快捷键", global)); tabs.Items.Add(HotkeyTab("应用内快捷键", local));
            tabs.SelectedIndex = shortcutTab;
            tabs.SelectionChanged += (s, e) => { if (e.Source == tabs) shortcutTab = Math.Max(0, tabs.SelectedIndex); };
            Grid.SetRow(tabs, 1); root.Children.Add(tabs);
            var empty = new TextBlock { Text = Ui.Text("没有匹配的快捷键"), Foreground = UiTheme.Muted, Margin = new Thickness(8, 44, 8, 8), IsHitTestVisible = false };
            Grid.SetRow(empty, 1); root.Children.Add(empty);
            Action filter = () =>
            {
                shortcutSearch = search.Text; var query = shortcutSearch.Trim();
                foreach (var item in searchable)
                {
                    string backup; settings.DisabledShortcuts.TryGetValue(item.Item3, out backup);
                    var haystack = Ui.Text(item.Item2) + " " + item.Item2 + " " + item.Item3 + " " + CurrentGesture(item.Item3) + " " + backup;
                    item.Item1.Visibility = query.Length == 0 || haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                foreach (var group in groups) group.Item1.Visibility = group.Item2.Any(r => r.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
                empty.Visibility = searchable.Any(r => r.Item1.Visibility == Visibility.Visible && (r.Item3.StartsWith("global-", StringComparison.Ordinal) ? 0 : 1) == shortcutTab) ? Visibility.Collapsed : Visibility.Visible;
            };
            search.TextChanged += (s, e) => filter(); tabs.SelectionChanged += (s, e) => filter(); filter();
            return root;
        }

        private static string ShortcutGroup(string action)
        {
            if (new[] { "bold", "italic", "underline", "strike", "bullets", "checkbox", "link", "date", "rule", "format-copy", "format-paste" }.Contains(action)) return "文本格式";
            if (new[] { "paste", "copy", "cut", "paste-plain", "copy-plain", "find", "find-next", "undo", "redo", "quote" }.Contains(action)) return "编辑与查找";
            if (new[] { "alarm", "sleep", "repeat", "tomorrow" }.Contains(action)) return "提醒与休眠";
            if (new[] { "transfer", "source", "add-friend", "send-many", "reply", "reply-secret", "sync-friends", "edit-friends", "friends" }.Contains(action)) return "联系人与发送";
            if (new[] { "size", "width", "width-left", "skin", "skin-reset", "top", "roll", "cycle", "cycle-back" }.Contains(action)) return "窗口与外观";
            return "便签与应用";
        }

        private static TabItem HotkeyTab(string title, Panel panel)
        {
            return new TabItem { Header = Ui.Text(title), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
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
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            var toggle = Switch(enabled);            AutomationProperties.SetName(toggle, Ui.Text(label) + " " + Ui.Text("打开 / 关闭快捷键"));
            toggle.Tag = action;
            row.Children.Add(toggle);
            var caption = new TextBlock { Text = Ui.Text(label), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.Text, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(caption, 1); row.Children.Add(caption);
            var modifiers = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(modifiers, 1); Grid.SetColumn(modifiers, 1); Grid.SetColumnSpan(modifiers, 3); modifiers.Margin = new Thickness(0, 7, 0, 0); row.Children.Add(modifiers);
            var shift = ModifierBox("Shift"); var win = ModifierBox("Win"); var ctrl = ModifierBox("Ctrl"); var alt = ModifierBox("Alt");
            modifiers.Children.Add(shift); modifiers.Children.Add(win); modifiers.Children.Add(ctrl); modifiers.Children.Add(alt);
            var plus = new TextBlock { Text = "+", Foreground = Ui.Brush("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(plus, 2); row.Children.Add(plus);
            var keyBox = new TextBox { Width = 72, Height = 28, IsReadOnly = false, IsUndoEnabled = false, Focusable = true, IsTabStop = true, TextAlignment = TextAlignment.Center, Cursor = Cursors.Hand, VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = Ui.Brush("#C9CED6"), Background = Brushes.White, Foreground = Ui.Brush("#111827"), ToolTip = Ui.Text("点击后按下按键") };
            AutomationProperties.SetName(keyBox, Ui.Text(label) + " " + Ui.Text("按下新的按键组合"));
            keyBox.Tag = action;
            keyBox.ContextMenu = null;
            InputMethod.SetIsInputMethodEnabled(keyBox, false);
            keyBox.PreviewMouseLeftButtonDown += (s, e) => { keyBox.Focus(); keyBox.SelectAll(); e.Handled = true; };
            keyBox.PreviewTextInput += (s, e) => { e.Handled = true; };
            Grid.SetColumn(keyBox, 3); row.Children.Add(keyBox);
            var shortcutField = fields["Shortcut." + action]; shortcutField.Control = keyBox;
            var feedback = new TextBlock { Foreground = UiTheme.Danger, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(40, 4, 0, 0) };
            var content = new StackPanel(); content.Children.Add(row); content.Children.Add(feedback);
            var container = new Border { Child = content, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 7, 0, 7) };
            shortcutField.RefreshFeedback = () =>
            {
                feedback.Text = shortcutField.Error ?? ""; feedback.Visibility = string.IsNullOrEmpty(shortcutField.Error) ? Visibility.Collapsed : Visibility.Visible;
                keyBox.BorderBrush = string.IsNullOrEmpty(shortcutField.Error) ? UiTheme.Border : UiTheme.Danger;
                keyBox.ToolTip = shortcutField.Error ?? Ui.Text("点击后按下按键");
            };
            shortcutField.RefreshFeedback();

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
                var modifierKeys = ModifierKeys.None;
                if (shift.IsChecked == true) modifierKeys |= ModifierKeys.Shift;
                if (win.IsChecked == true) modifierKeys |= ModifierKeys.Windows;
                if (ctrl.IsChecked == true) modifierKeys |= ModifierKeys.Control;
                if (alt.IsChecked == true) modifierKeys |= ModifierKeys.Alt;
                var value = GestureString(key, modifierKeys);
                if (string.IsNullOrWhiteSpace(value)) return;
                gesture = value;
                if (enabled) { settings.DisabledShortcuts.Remove(action); StoreGesture(action, gesture); }
                else { settings.DisabledShortcuts[action] = gesture; StoreGesture(action, ""); }
                CheckShortcutFields();
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
                CheckShortcutFields(); refreshEnabled();
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
                key = pressed; gesture = value;
                updating = true;
                shift.IsChecked = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                win.IsChecked = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;
                ctrl.IsChecked = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                alt.IsChecked = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                updating = false;
                keyBox.Text = KeyLabel(key);
                if (enabled) { settings.DisabledShortcuts.Remove(action); StoreGesture(action, gesture); }
                else { settings.DisabledShortcuts[action] = gesture; StoreGesture(action, ""); }
                CheckShortcutFields(); refreshEnabled();
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
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, UiTheme.Accent, "track"));
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

        private static TextBlock Hint(string text)
        {
            return new TextBlock
            {
                Text = Ui.Text(text), TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted,
                FontSize = 12, LineHeight = 18, Margin = new Thickness(2, 4, 2, 12)
            };
        }

        private static Grid SettingsGrid()
        {
            var grid = new Grid { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(178) });
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
            var text = new TextBlock { Text = Ui.Text(label), VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.Text, Margin = new Thickness(2, 9, 12, 9), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(text, row); Grid.SetColumn(text, 0); grid.Children.Add(text);
            AutomationProperties.SetName(control, Ui.Text(label));
            control.Margin = new Thickness(2, 7, 2, 7);
            var field = control.Tag as SettingsFieldState;
            FrameworkElement element = control;
            if (field != null)
            {
                var wrapper = new StackPanel(); wrapper.Children.Add(control);
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Danger, FontSize = 12, Margin = new Thickness(2, 0, 2, 4) };
                wrapper.Children.Add(error);
                var refresh = field.RefreshFeedback;
                field.RefreshFeedback = () => { if (refresh != null) refresh(); error.Text = field.Error ?? ""; error.Visibility = string.IsNullOrEmpty(field.Error) ? Visibility.Collapsed : Visibility.Visible; };
                field.RefreshFeedback(); element = wrapper;
            }
            Grid.SetRow(element, row); Grid.SetColumn(element, 1); grid.Children.Add(element);
        }
        private static void AddSection(Grid grid, string title)
        {
            AddFullRow(grid, new TextBlock { Text = Ui.Text(title), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Text, Margin = new Thickness(2, 16, 2, 4) });
        }
        private static void AddHintRow(Grid grid, string text) { AddFullRow(grid, Hint(text)); }
        private void SwitchRow(Grid grid, string label, bool value, Action<bool> setter)
        {
            var panel = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 8, 2, 8) };
            var toggle = Switch(value); toggle.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(toggle, Dock.Left);
            AutomationProperties.SetName(toggle, Ui.Text(label));
            toggle.Checked += (s, e) => setter(true); toggle.Unchecked += (s, e) => setter(false);
            panel.Children.Add(toggle);
            panel.Children.Add(new TextBlock { Text = Ui.Text(label), VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.Text, TextWrapping = TextWrapping.Wrap });
            AddFullRow(grid, panel);
        }
        private TextBox TextControl(string key, double width = double.NaN)
        {
            var box = new TextBox { Width = width, MinWidth = 60, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
            BindField(box, fields[key]); return box;
        }
        private TextBox NumberControl(Grid grid, string key, string label, int value, Action<int> setter, int min = 0, int max = int.MaxValue, double width = 70)
        {
            var box = NumberBox(key, label, value, setter, min, max, width);
            AddRow(grid, label, box); return box;
        }
        private TextBox NumberBox(string key, string label, int value, Action<int> setter, int min = 0, int max = int.MaxValue, double width = 70)
        {
            var box = new TextBox { Width = width, MinHeight = 30, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
            BindField(box, fields[key]); return box;
        }
        private static ComboBox ChoiceBox(IEnumerable<Choice> options, string selected, Action<string> setter, double width = 280)
        {
            var list = options.ToList(); var box = new ComboBox { ItemsSource = list, DisplayMemberPath = "Label", Width = width, Height = 28, SelectedItem = list.FirstOrDefault(c => c.Code == selected) ?? list.FirstOrDefault() };
            box.SelectionChanged += (s, e) => { var choice = box.SelectedItem as Choice; if (choice != null) setter(choice.Code); };
            return box;
        }
        private Button ColorButton(string color, Action<string> setter)
        {
            var current = color ?? "#FFFFB4";
            var button = new Button { Style = UiTheme.ButtonStyle(), Width = 54, Height = 30, Padding = new Thickness(0), Margin = new Thickness(0), Background = Ui.Brush(current), BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1), ToolTip = current, Tag = current };
            AutomationProperties.SetName(button, Ui.Text("颜色") + " " + current);
            button.Click += (s, e) => { try { var picked = Ui.Color(button.Tag as string ?? current); if (picked != null) { current = picked; button.Tag = picked; button.Background = Ui.Brush(picked); button.ToolTip = picked; setter(picked); } } catch (Exception ex) { Ui.Error(ex, this); } };
            return button;
        }
        private FrameworkElement FileRow(string key, string filter, bool save = false, bool sound = false)
        {
            var panel = new DockPanel { LastChildFill = true };
            var box = TextControl(key);
            var browse = Ui.Button("浏览…", () => { var picked = save ? Ui.SaveFile(filter, "transfer.dpx", this) : Ui.OpenFile(filter, this); if (picked != null) box.Text = picked; });
            browse.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); panel.Children.Add(browse);
            if (sound)
            {
                var play = Ui.Button("播放", () => PlaySound(box.Text));
                play.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(play, Dock.Right); panel.Children.Add(play);
            }
            panel.Children.Add(box); return panel;
        }
        private FrameworkElement SoundRow(string key) { return FileRow(key, "WAV|*.wav", false, true); }
        private void PlaySound(string file)
        {
            try { if (!string.IsNullOrWhiteSpace(file) && File.Exists(file)) { using (var player = new SoundPlayer(file)) player.Play(); } else SystemSounds.Asterisk.Play(); }
            catch (Exception ex) { Ui.Error(ex, this); }
        }
        private static TextBox PathText(string path)
        {
            return new TextBox { Text = path ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = path };
        }
        private sealed class NativeDialogOwner : System.Windows.Forms.IWin32Window
        {
            private readonly Window window;
            internal NativeDialogOwner(Window window) { this.window = window; }
            public IntPtr Handle { get { return new System.Windows.Interop.WindowInteropHelper(window).Handle; } }
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
                Ui.Error(ex, this);
            }
            fields["Language"].Raw = settings.Language;
            fields["Language"].Check();
            BuildWindow();
        }

        private void InitializeFields()
        {
            fields.Clear(); fieldOrder.Clear();
            RegisterNumber("BackupHours", "自动备份间隔（小时，0=关闭）", 0, settings.BackupHours, v => settings.BackupHours = v, 0, int.MaxValue);
            RegisterNumber("ClosedRetentionDays", "自动永久删除 Closed 便签（保留天数）", 8, settings.ClosedRetentionDays, v => settings.ClosedRetentionDays = v, 0, int.MaxValue);
            RegisterNumber("SnapDistance", "吸附距离", 1, settings.SnapDistance, v => settings.SnapDistance = v, 0, 100);
            RegisterNumber("SnapGap", "吸附间隙", 1, settings.SnapGap, v => settings.SnapGap = v, 0, 100);
            RegisterNumber("TomorrowHour", "明天唤醒小时", 4, settings.TomorrowHour, v => settings.TomorrowHour = v, 0, 23);
            RegisterNumber("TomorrowMinute", "明天唤醒分钟", 4, settings.TomorrowMinute, v => settings.TomorrowMinute = v, 0, 59);
            RegisterNumber("SnoozeMinutes", "稍后提醒分钟数", 4, settings.SnoozeMinutes, v => settings.SnoozeMinutes = v, 1, 1440);
            RegisterNumber("ImageUndoLevels", "图片撤销层数", 3, settings.ImageUndoLevels, v => settings.ImageUndoLevels = v, 1, 40);
            RegisterNumber("NetworkPort", "监听端口", 5, settings.NetworkPort, v => settings.NetworkPort = v, 1, 65535);
            RegisterNumber("SmtpPort", "端口", 6, settings.SmtpPort, v => settings.SmtpPort = v, 1, 65535);
            RegisterText("TransferFile", "传输文件", 0, settings.TransferFile, v => settings.TransferFile = v);
            RegisterText("FriendsServer", "联系人列表来源（文件或 HTTP URL）", 5, settings.FriendsServer, v => settings.FriendsServer = v);
            RegisterText("SmtpHost", "SMTP 服务器", 6, settings.SmtpHost, v => settings.SmtpHost = v);
            RegisterText("SmtpUser", "用户名", 6, settings.SmtpUser, v => settings.SmtpUser = v);
            RegisterText("SmtpFrom", "发件人地址", 6, settings.SmtpFrom, v => settings.SmtpFrom = v);
            RegisterText("SkinPath", "皮肤文件名", 2, settings.SkinPath, v => settings.SkinPath = v);
            fields["SkinPath"].Validate = raw => { try { if (!string.IsNullOrWhiteSpace(raw)) host.ValidateSkin(raw.Trim()); return null; } catch (Exception ex) { return ex.Message; } };
            RegisterText("Language", "界面语言", 0, settings.Language, v => settings.Language = v);
            fields["Language"].Validate = raw => { try { Ui.ValidateLanguage(raw); return null; } catch (Exception ex) { return ex.Message; } };
            RegisterText("DataDirectory", "下次启动目录", 8, pendingDataDirectory, v => pendingDataDirectory = v);
            fields["DataDirectory"].Validate = raw => { try { host.ValidateDataDirectory(raw); return null; } catch (Exception ex) { return ex.Message; } };
            RegisterText("WakeSound", "唤醒声音文件", 4, settings.WakeSound, v => settings.WakeSound = v);
            RegisterText("AlarmSound", "闹钟声音文件", 4, settings.AlarmSound, v => settings.AlarmSound = v);
            for (int i = 0; i < settings.Styles.Count; i++)
            {
                int index = i;
                var style = settings.Styles[index];
                RegisterText(StyleKey(index, "Name"), "名称", 2, style.Name, v => settings.Styles[index].Name = v);
                var tab = RegisterNumber(StyleKey(index, "TabWidth"), "Tab 宽度（twips）", 2, style.TabWidth, v => settings.Styles[index].TabWidth = v, 1, 10000);
                tab.StyleIndex = index; fields[StyleKey(index, "Name")].StyleIndex = index;
                RegisterText(StyleKey(index, "Color"), "便签颜色", 2, style.Color, v => settings.Styles[index].Color = v);
                RegisterText(StyleKey(index, "Foreground"), "字体颜色", 2, style.Foreground, v => settings.Styles[index].Foreground = v);
                foreach (var property in new[] { "Color", "Foreground" })
                {
                    var field = fields[StyleKey(index, property)]; field.StyleIndex = index;
                    field.Validate = raw => { try { ColorConverter.ConvertFromString(raw); return null; } catch (Exception ex) { return ex.Message; } };
                }
                RegisterText(StyleKey(index, "FontName"), "字体样式", 2, style.FontName, v => settings.Styles[index].FontName = v);
                fields[StyleKey(index, "FontName")].StyleIndex = index;
                fields[StyleKey(index, "FontName")].Validate = raw => string.IsNullOrWhiteSpace(raw) ? Ui.Text("字体名称不能为空。") : null;
            }
            CheckShortcutFields();
        }

        private static string StyleKey(int index, string property) { return "Styles." + index + "." + property; }

        private void RegisterText(string key, string label, int page, string value, Action<string> setter)
        {
            AddField(new SettingsFieldState { Key = key, Label = label, Category = page, Raw = value ?? "", Apply = raw => setter(raw.Trim()) });
        }

        private SettingsFieldState RegisterNumber(string key, string label, int page, int value, Action<int> setter, int min, int max)
        {
            var field = new SettingsFieldState
            {
                Key = key, Label = label, Category = page, Raw = value.ToString(CultureInfo.InvariantCulture),
                Validate = raw =>
                {
                    int number;
                    if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= min && number <= max) return null;
                    return max == int.MaxValue ? Ui.Text("必须为非负整数。") : string.Format(Ui.Text("必须是 {0}–{1} 之间的整数。"), min, max);
                },
                Apply = raw => setter(int.Parse(raw.Trim(), CultureInfo.InvariantCulture))
            };
            AddField(field); return field;
        }

        private void AddField(SettingsFieldState field) { fields.Add(field.Key, field); fieldOrder.Add(field.Key); }

        private void BindField(TextBox box, SettingsFieldState field)
        {
            box.Text = field.Raw; box.Tag = field; field.Control = box;
            AutomationProperties.SetName(box, Ui.Text(field.Label));
            field.RefreshFeedback = () =>
            {
                bool invalid = !string.IsNullOrEmpty(field.Error);
                box.BorderBrush = invalid ? UiTheme.Danger : UiTheme.Border;
                box.BorderThickness = new Thickness(invalid ? 2 : 1);
                box.ToolTip = invalid ? field.Error : null;
                AutomationProperties.SetHelpText(box, field.Error ?? "");
            };
            field.Check();
            box.TextChanged += (s, e) => { field.Raw = box.Text; field.Check(); if (validationStatus) SetStatus(""); };
        }

        private void LocateField(SettingsFieldState field)
        {
            category = field.Category;
            if (field.StyleIndex >= 0) selectedStyle = field.StyleIndex;
            if (field.ShortcutTab >= 0) { shortcutTab = field.ShortcutTab; shortcutSearch = ""; }
            if (navigation.SelectedIndex == category) ShowPage(); else navigation.SelectedIndex = category;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (field.Control == null) return;
                field.Control.BringIntoView(); field.Control.Focus();
                var box = field.Control as TextBox; if (box != null) box.SelectAll();
            }));
        }

        private void CheckShortcutFields()
        {
            var local = new Dictionary<string, SettingsFieldState>(StringComparer.OrdinalIgnoreCase);
            var global = new Dictionary<string, SettingsFieldState>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in new[] { "global-new", "global-toggle", "global-manage" }.Concat(AppHost.ShortcutOrder))
            {
                string key = "Shortcut." + action;
                SettingsFieldState field;
                string label;
                if (!shortcutLabels.TryGetValue(action, out label)) label = action == "global-toggle" ? "显示 / 隐藏全部便签" : action == "global-new" ? "新建便签" : "管理便签";
                if (!fields.TryGetValue(key, out field))
                {
                    field = new SettingsFieldState { Key = key, Label = label, Category = 7, ShortcutTab = action.StartsWith("global-", StringComparison.Ordinal) ? 0 : 1 };
                    AddField(field);
                }
                field.Raw = CurrentGesture(action); field.Error = null;
                if (!string.IsNullOrWhiteSpace(field.Raw))
                {
                    Key parsed; ModifierKeys mods;
                    if (!Ui.TryParseGesture(field.Raw, out parsed, out mods)) field.Error = Ui.Text("快捷键格式无效。");
                    else if (field.ShortcutTab == 0 && mods == ModifierKeys.None && !(parsed >= Key.F1 && parsed <= Key.F24)) field.Error = Ui.Text("全局快捷键必须包含修饰键或功能键。");
                    else
                    {
                        var group = field.ShortcutTab == 0 ? global : local;
                        string signature = parsed + ":" + mods;
                        SettingsFieldState duplicate;
                        if (group.TryGetValue(signature, out duplicate)) { field.Error = duplicate.Error = Ui.Text("快捷键不能重复。"); }
                        else group.Add(signature, field);
                    }
                }
            }
            foreach (var field in fields.Values.Where(f => f.Category == 7)) if (field.RefreshFeedback != null) field.RefreshFeedback();
        }

        private void SetStatus(string message)
        {
            validationStatus = false;
            statusMessage = message ?? "";
            if (saveStatus != null) { saveStatus.Text = statusMessage; saveStatus.Visibility = string.IsNullOrEmpty(statusMessage) ? Visibility.Collapsed : Visibility.Visible; }
        }

        private void Save()
        {
            bool committedThisSave = false;
            try
            {
                var invalid = new List<SettingsFieldState>();
                foreach (var key in fieldOrder.Where(k => !k.StartsWith("Shortcut.", StringComparison.Ordinal)))
                {
                    var field = fields[key]; if (!field.Check()) invalid.Add(field);
                }
                CheckShortcutFields();
                invalid.AddRange(fields.Values.Where(f => f.Category == 7 && !string.IsNullOrEmpty(f.Error)));
                if (invalid.Count > 0)
                {
                    SetStatus(string.Format(Ui.Text("有 {0} 个字段需要修正。"), invalid.Count) + "\n" + string.Join("\n", invalid.Select(f => Ui.Text(categoryNames[f.Category]) + " · " + Ui.Text(f.Label) + (f.StyleIndex >= 0 ? " (" + settings.Styles[f.StyleIndex].Name + ")" : "") + ": " + f.Error)));
                    validationStatus = true; LocateField(invalid[0]); return;
                }
                if (!string.IsNullOrWhiteSpace(smtpPassword)) settings.SmtpSecret = host.ProtectSecret(smtpPassword);
                try { Ui.ValidateLanguage(settings.Language); }
                catch { category = 0; navigation.SelectedIndex = 0; throw; }
                try { if (!string.IsNullOrWhiteSpace(settings.SkinPath)) host.ValidateSkin(settings.SkinPath); }
                catch (Exception ex) { var field = fields["SkinPath"]; field.Error = ex.Message; LocateField(field); throw; }
                host.ValidateSettings(settings);
                host.ValidateDataDirectory(pendingDataDirectory);
                if (settings.ClosedRetentionDays > 0 && (originalRetentionDays == 0 || settings.ClosedRetentionDays < originalRetentionDays) &&
                    !Ui.Confirm("保存后将自动永久删除超过保留天数的 Closed 便签，无法从 Closed 恢复；现有便签也会被清理。建议先备份。继续？", this)) return;
                settings.Normalize();
                // Never share the mutable draft with the live Catalog after committing.
                var committed = Json.Read<Settings>(Json.Bytes(settings));
                host.Library.Atomic(() => { host.Library.Catalog.Settings = committed; host.Library.SaveCatalog(); });
                committedThisSave = true;
                originalLanguage = settings.Language;
                originalRetentionDays = settings.ClosedRetentionDays;
                smtpPassword = "";
                var failures = new List<string>();
                if (settings.AutoStart != originalAutoStart)
                {
                    try { host.SetAutoStart(settings.AutoStart); originalAutoStart = settings.AutoStart; }
                    catch (Exception ex) { failures.Add(Ui.Text("开机启动") + ": " + ex.Message); }
                }
                bool dataDirectoryChanged = !DataLocation.Same(pendingDataDirectory, appliedDataDirectory);
                if (dataDirectoryChanged)
                {
                    try { host.ApplyDataDirectory(pendingDataDirectory); appliedDataDirectory = pendingDataDirectory; directoryApplied = true; }
                    catch (Exception ex) { directoryApplied = false; failures.Add(Ui.Text("更改数据目录") + ": " + ex.Message); }
                }
                try { failures.AddRange(host.ApplySettingsReport()); }
                catch (Exception ex) { failures.Add(ex.Message); }
                if (failures.Count > 0)
                {
                    if (dataDirectoryChanged && directoryApplied) failures.Add(Ui.Text("数据目录已更新，重启 DeskPins 后生效。"));
                    SetStatus(Ui.Text("设置已保存，但部分操作未应用。取消或关闭不会撤销已保存的设置；修正后可再次保存重试。") + "\n" + string.Join("\n", failures));
                    ShowPage(); return;
                }
                if (dataDirectoryChanged) MessageBox.Show(this, Ui.Text("数据目录已更新，重启 DeskPins 后生效。"), "DeskPins", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex) { SetStatus(Ui.Text(committedThisSave ? "设置已保存，但部分操作未应用。取消或关闭不会撤销已保存的设置；修正后可再次保存重试。" : "设置未保存。") + " " + ex.Message); Ui.Error(ex, this); }
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
