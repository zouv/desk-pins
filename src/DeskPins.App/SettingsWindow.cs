using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class SettingsWindow : Window
    {
        private readonly List<Action> commit = new List<Action>();
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
        public SettingsWindow(AppHost host)
        {
            Title = "DeskPins 设置"; Width = 720; Height = 610; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
            Ui.FitDialog(this, 540, 360);
            var settings = Json.Read<Settings>(Json.Bytes(host.Library.Catalog.Settings)); settings.Normalize(); var root = new DockPanel(); var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); var tabs = new TabControl { Margin = new Thickness(12) }; root.Children.Add(tabs);
            var app = Tab(tabs, "应用"); Text(app, "语言文件（zh-CN 为内置中文，其他值为 JSON 文件路径）", settings.Language, v => settings.Language = v); Check(app, "开机启动", settings.AutoStart, v => settings.AutoStart = v); Check(app, "默认新建图片便签", settings.DefaultKind == NoteKind.Image, v => settings.DefaultKind = v ? NoteKind.Image : NoteKind.Text); Text(app, "传输文件", settings.TransferFile, v => settings.TransferFile = v); Number(app, "自动备份间隔（小时，0=关闭）", settings.BackupHours, v => settings.BackupHours = v); Number(app, "Closed 保留天数（0=一直保留）", settings.ClosedRetentionDays, v => settings.ClosedRetentionDays = v); Check(app, "紧凑管理列表", settings.CompactList, v => settings.CompactList = v); Check(app, "管理列表显示最后修改日期", settings.ManagerModifiedDates, v => settings.ManagerModifiedDates = v);
            var desktop = Tab(tabs, "桌面"); Check(desktop, "便签 / 屏幕边缘吸附", settings.Snap, v => settings.Snap = v); Check(desktop, "编辑时临时不透明", settings.OpaqueEditing, v => settings.OpaqueEditing = v); Check(desktop, "双击标题栏卷起", settings.DoubleClickRoll, v => settings.DoubleClickRoll = v);
            var appearance = Tab(tabs, "外观"); var styles = new ComboBox { ItemsSource = Enumerable.Range(0, 10).Select(i => i + "  " + settings.Styles[i].Name), SelectedIndex = 0 }; Ui.Field(appearance, "样式（0 为新便签默认样式）", styles); var name = new TextBox(); var color = new TextBox(); var foreground = new TextBox(); var font = new TextBox(); var size = new TextBox(); var opacity = new TextBox(); var onTop = new CheckBox { Content = "置顶" }; int current = 0;
            Action load = () => { var s = settings.Styles[current]; name.Text = s.Name; color.Text = s.Color; foreground.Text = s.Foreground; font.Text = s.FontName; size.Text = s.FontSize.ToString(); opacity.Text = (s.Opacity * 100).ToString(); onTop.IsChecked = s.Topmost; }; Action save = () => { var s = settings.Styles[current]; s.Name = name.Text; s.Color = color.Text; s.Foreground = foreground.Text; s.FontName = font.Text; s.FontSize = Math.Max(6, Math.Min(200, Ui.Number(size.Text, 13.333))); s.Opacity = Math.Max(.1, Math.Min(1, Ui.Number(opacity.Text, 100) / 100)); s.Topmost = onTop.IsChecked == true; }; styles.SelectionChanged += (s, e) => { save(); current = styles.SelectedIndex; load(); }; Ui.Field(appearance, "名称", name); Ui.Field(appearance, "背景颜色", color); Ui.Field(appearance, "文本颜色", foreground); Ui.Field(appearance, "字体", font); Ui.Field(appearance, "字号（WPF 像素）", size); Ui.Field(appearance, "不透明度（10–100）", opacity); appearance.Children.Add(onTop); appearance.Children.Add(Ui.Button("选择背景色", () => { var c = Ui.Color(color.Text); if (c != null) color.Text = c; })); load(); commit.Add(save); Text(appearance, "默认皮肤文件（JSON）", settings.SkinPath, v => settings.SkinPath = v);
            var alerts = Tab(tabs, "提醒"); Check(alerts, "闹钟闪烁边框", settings.AlarmFlash, v => settings.AlarmFlash = v); Check(alerts, "闹钟抖动", settings.AlarmJiggle, v => settings.AlarmJiggle = v); Check(alerts, "循环播放声音", settings.AlarmLoop, v => settings.AlarmLoop = v); Check(alerts, "避免重复的循环便签", settings.PreventDuplicates, v => settings.PreventDuplicates = v); Number(alerts, "稍后提醒分钟数", settings.SnoozeMinutes, v => settings.SnoozeMinutes = v); Number(alerts, "明天唤醒小时（0–23）", settings.TomorrowHour, v => settings.TomorrowHour = v); Text(alerts, "闹钟 WAV 文件", settings.AlarmSound, v => settings.AlarmSound = v); Text(alerts, "唤醒 WAV 文件", settings.WakeSound, v => settings.WakeSound = v);
            var networking = Tab(tabs, "网络"); Check(networking, "启用 TCP 发送 / 接收", settings.Networking, v => settings.Networking = v); Number(networking, "监听端口", settings.NetworkPort, v => settings.NetworkPort = v); Check(networking, "使用 DNS 识别发送者", settings.UseDns, v => settings.UseDns = v); Text(networking, "联系人列表来源（文件或 HTTP URL）", settings.FriendsServer, v => settings.FriendsServer = v); networking.Children.Add(new TextBlock { Text = "监听只在启用后启动。支持 Stickies #sticky 文本 / RTF / 图片传输格式。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 4) });
            var email = Tab(tabs, "邮件"); Check(email, "使用默认邮件客户端打开正文草稿（附件使用 SMTP）", settings.EmailViaClient, v => settings.EmailViaClient = v); Check(email, "启用 SMTP 邮件发送", settings.EmailEnabled, v => settings.EmailEnabled = v); Text(email, "SMTP 服务器", settings.SmtpHost, v => settings.SmtpHost = v); Number(email, "端口", settings.SmtpPort, v => settings.SmtpPort = v); Check(email, "启用 TLS", settings.SmtpSsl, v => settings.SmtpSsl = v); Text(email, "用户名", settings.SmtpUser, v => settings.SmtpUser = v); var password = new PasswordBox(); Ui.Field(email, "密码（留空保留当前值）", password); commit.Add(() => { if (password.Password.Length > 0) settings.SmtpSecret = host.ProtectSecret(password.Password); }); Text(email, "发件人地址", settings.SmtpFrom, v => settings.SmtpFrom = v);
            var shortcuts = Tab(tabs, "快捷键"); Text(shortcuts, "全局新建（例如 Ctrl+Alt+N；留空关闭）", settings.GlobalNewKey, v => settings.GlobalNewKey = v); Text(shortcuts, "全局显示 / 隐藏", settings.GlobalToggleKey, v => settings.GlobalToggleKey = v); foreach (var pair in AppHost.DefaultShortcuts) { string action = pair.Key; string value; Text(shortcuts, shortcutLabels[action], settings.Shortcuts.TryGetValue(action, out value) ? value : pair.Value, v => settings.Shortcuts[action] = v); }
            var saveButton = Ui.Button("保存", () => { try { foreach (var action in commit) action(); host.ValidateSettings(settings); settings.Normalize(); if (host.Library.Catalog.Settings.AutoStart != settings.AutoStart) host.SetAutoStart(settings.AutoStart); host.Library.Atomic(() => { host.Library.Catalog.Settings = settings; host.Library.SaveCatalog(); }); host.ApplySettings(); Close(); } catch (Exception ex) { Ui.Error(ex); } }); saveButton.IsDefault = true; buttons.Children.Add(saveButton); var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; buttons.Children.Add(cancel); Content = root;
        }
        private static StackPanel Tab(TabControl tabs, string name) { var panel = new StackPanel { Margin = new Thickness(12) }; tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel; }
        private void Check(Panel panel, string label, bool value, Action<bool> setter) { var control = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(4, 7, 4, 7) }; panel.Children.Add(control); commit.Add(() => setter(control.IsChecked == true)); }
        private void Text(Panel panel, string label, string value, Action<string> setter) { var control = new TextBox { Text = value ?? "" }; Ui.Field(panel, label, control); commit.Add(() => setter(control.Text.Trim())); }
        private void Number(Panel panel, string label, int value, Action<int> setter) { Text(panel, label, value.ToString(), v => { int n; if (!int.TryParse(v, out n) || n < 0) throw new ArgumentException(label + " 必须为非负整数。"); setter(n); }); }
    }
}
