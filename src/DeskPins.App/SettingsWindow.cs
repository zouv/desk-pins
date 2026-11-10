using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private static readonly string[] categoryNames = { "应用", "桌面", "外观", "提醒", "网络", "邮件", "快捷键" };
        private static readonly string[] categoryIcons = { "", "", "", "", "", "", "" };
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

        public SettingsWindow(AppHost host)
        {
            this.host = host;
            var current = host.Library.Catalog.Settings;
            originalLanguage = string.IsNullOrWhiteSpace(current.Language) ? Localization.Chinese : current.Language;
            originalAutoStart = current.AutoStart;
            settings = Json.Read<Settings>(Json.Bytes(current)) ?? new Settings();
            settings.Normalize();
            Ui.LoadLanguage(settings.Language);
            Width = 940; Height = 680; MinWidth = 800; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
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
            return new ListBoxItem { Content = row, Tag = index };
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
            if (category == 6)
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
                case 3: BuildAlertsPage(panel); break;
                case 4: BuildNetworkPage(panel); break;
                case 5: BuildEmailPage(panel); break;
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
            Check(panel, "便签 / 屏幕边缘吸附", settings.Snap, v => settings.Snap = v);
            Check(panel, "编辑时临时不透明", settings.OpaqueEditing, v => settings.OpaqueEditing = v);
            Check(panel, "双击标题栏卷起", settings.DoubleClickRoll, v => settings.DoubleClickRoll = v);
        }

        private void BuildAppearancePage(Panel panel)
        {
            var styles = new ComboBox { ItemsSource = Enumerable.Range(0, 10).Select(i => i + "  " + settings.Styles[i].Name).ToList(), SelectedIndex = 0 };
            Ui.Field(panel, "样式（0 为新便签默认样式）", styles);
            var name = new TextBox(); var color = new TextBox(); var foreground = new TextBox(); var font = new TextBox(); var size = new TextBox(); var opacity = new TextBox();
            var onTop = new CheckBox { Content = Ui.Text("置顶"), Margin = new Thickness(4, 7, 4, 7) };
            int current = 0;
            Action load = () =>
            {
                loadingStyle = true;
                var style = settings.Styles[current];
                name.Text = style.Name; color.Text = style.Color; foreground.Text = style.Foreground; font.Text = style.FontName;
                size.Text = style.FontSize.ToString(CultureInfo.CurrentCulture); opacity.Text = (style.Opacity * 100).ToString(CultureInfo.CurrentCulture); onTop.IsChecked = style.Topmost;
                loadingStyle = false;
            };
            Action store = () =>
            {
                if (loadingStyle) return;
                var style = settings.Styles[current];
                style.Name = name.Text; style.Color = color.Text.Trim(); style.Foreground = foreground.Text.Trim(); style.FontName = font.Text.Trim();
                style.FontSize = Math.Max(6, Math.Min(200, Ui.Number(size.Text, style.FontSize)));
                style.Opacity = Math.Max(.1, Math.Min(1, Ui.Number(opacity.Text, style.Opacity * 100) / 100)); style.Topmost = onTop.IsChecked == true;
            };
            styles.SelectionChanged += (s, e) => { if (loadingStyle) return; store(); current = Math.Max(0, styles.SelectedIndex); load(); };
            Ui.Field(panel, "名称", name); Ui.Field(panel, "背景颜色", color); Ui.Field(panel, "文本颜色", foreground);
            Ui.Field(panel, "字体", font); Ui.Field(panel, "字号（WPF 像素）", size); Ui.Field(panel, "不透明度（10–100）", opacity);
            name.TextChanged += (s, e) => store(); color.TextChanged += (s, e) => store(); foreground.TextChanged += (s, e) => store();
            font.TextChanged += (s, e) => store(); size.TextChanged += (s, e) => store(); opacity.TextChanged += (s, e) => store();
            onTop.Checked += (s, e) => store(); onTop.Unchecked += (s, e) => store();
            panel.Children.Add(onTop);
            panel.Children.Add(Ui.Button("选择背景色", () => { var picked = Ui.Color(color.Text); if (picked != null) color.Text = picked; }));
            load();
            Text(panel, "默认皮肤文件（JSON）", settings.SkinPath, v => settings.SkinPath = v);
        }

        private void BuildAlertsPage(Panel panel)
        {
            Check(panel, "闹钟闪烁边框", settings.AlarmFlash, v => settings.AlarmFlash = v);
            Check(panel, "闹钟抖动", settings.AlarmJiggle, v => settings.AlarmJiggle = v);
            Check(panel, "循环播放声音", settings.AlarmLoop, v => settings.AlarmLoop = v);
            Check(panel, "避免重复的循环便签", settings.PreventDuplicates, v => settings.PreventDuplicates = v);
            Number(panel, "稍后提醒分钟数", settings.SnoozeMinutes, v => settings.SnoozeMinutes = v);
            Number(panel, "明天唤醒小时（0–23）", settings.TomorrowHour, v => settings.TomorrowHour = v);
            Text(panel, "闹钟 WAV 文件", settings.AlarmSound, v => settings.AlarmSound = v);
            Text(panel, "唤醒 WAV 文件", settings.WakeSound, v => settings.WakeSound = v);
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
            var toggle = Switch(enabled);
            AutomationProperties.SetName(toggle, Ui.Text(label) + " " + Ui.Text("打开 / 关闭快捷键"));
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
            var keyBox = new TextBox { Width = 72, Height = 28, TextAlignment = TextAlignment.Center, IsReadOnly = true, Cursor = Cursors.Hand, VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = Ui.Brush("#C9CED6"), Background = Brushes.White, Foreground = Ui.Brush("#111827") };
            AutomationProperties.SetName(keyBox, Ui.Text(label) + " " + Ui.Text("按下新的按键组合"));
            keyBox.Tag = action;
            Grid.SetColumn(keyBox, 4); row.Children.Add(keyBox);
            var container = new Border { Child = row, BorderBrush = Ui.Brush("#EEF0F3"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 7, 0, 7) };

            Action applyParsed = null, commit = null, refreshEnabled = null;
            Action<bool> setEnabled = null;
            applyParsed = () =>
            {
                var parsed = ParseGesture(gesture);
                key = parsed == null ? Key.None : parsed.Key;
                updating = true;
                shift.IsChecked = parsed != null && (parsed.Modifiers & ModifierKeys.Shift) != 0;
                win.IsChecked = parsed != null && (parsed.Modifiers & ModifierKeys.Windows) != 0;
                ctrl.IsChecked = parsed != null && (parsed.Modifiers & ModifierKeys.Control) != 0;
                alt.IsChecked = parsed != null && (parsed.Modifiers & ModifierKeys.Alt) != 0;
                keyBox.Text = key == Key.None ? "" : KeyLabel(key);
                updating = false;
            };
            commit = () =>
            {
                if (updating || !enabled || key == Key.None) return;
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
                shift.IsEnabled = win.IsEnabled = ctrl.IsEnabled = alt.IsEnabled = enabled;
                keyBox.IsEnabled = enabled;
                container.Opacity = enabled ? 1 : .55;
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
                if (!enabled || e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
                var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
                if (pressed == Key.LeftShift || pressed == Key.RightShift || pressed == Key.LeftCtrl || pressed == Key.RightCtrl ||
                    pressed == Key.LeftAlt || pressed == Key.RightAlt || pressed == Key.LWin || pressed == Key.RWin || pressed == Key.None)
                {
                    e.Handled = true; return;
                }
                if (pressed == Key.Escape) { keyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; return; }
                if (pressed == Key.Back || pressed == Key.Delete) { setEnabled(false); e.Handled = true; return; }
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
                settings.DisabledShortcuts.Remove(action);
                StoreGesture(action, gesture);
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
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.WidthProperty, 36d);
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
                Cursor = Cursors.Hand, ToolTip = Ui.Text("打开 / 关闭快捷键")
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

        private string CurrentGesture(string action)
        {
            switch (action)
            {
                case "global-new": return settings.GlobalNewKey ?? "";
                case "global-toggle": return settings.GlobalToggleKey ?? "";
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
                host.Library.Atomic(() => { host.Library.Catalog.Settings = settings; host.Library.SaveCatalog(); });
                host.ApplySettings();
                saved = true;
                Close();
            }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private static KeyGesture ParseGesture(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return (KeyGesture)new KeyGestureConverter().ConvertFromString(text); } catch { return null; }
        }

        private static string GestureString(Key key, ModifierKeys modifiers)
        {
            try { return new KeyGestureConverter().ConvertToString(new KeyGesture(key, modifiers)); } catch { return null; }
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

        private sealed class LanguageOption
        {
            public string Code { get; private set; }
            public string Label { get; private set; }
            public LanguageOption(string code, string label) { Code = code; Label = label; }
        }
    }
}
