# ARCH 分册 · 设置 / 语言 / 快捷键 / 数据目录（settings）

> 由 ARCHITECTURE.md §0.5 路由进入；小节号与总览一致（§2 入口 / §3 链路 / §4 契约 / §7 坑）。按函数名定位，改结构即同步。

## 2. 任务 → 入口

| 任务 | 入口 |
|---|---|
| 设置草稿与校验 | SettingsWindow.BuildWindow / InitializeFields / BindField / LocateField / CheckShortcutFields / Save / ReloadDraft、SettingsFieldState.Check、AppHost.ValidateSettings |
| 设置页面 / 样式 / 快捷键 | SettingsWindow.BuildCategoryPage / BuildAppearancePage / BuildContentPage / BuildAlertsPage / BuildDesktopPage / BuildDataPage / BuildShortcutsPage / ShortcutRow |
| 语言与分项应用 | SettingsWindow.ApplyLanguage、Ui.ValidateLanguage / LoadLanguage / Text、Localization.Dictionary、AppHost.ApplySteps / ApplySettingsReport / RefreshLanguageSurfaces |
| 快捷键解析 | Ui.TryParseGesture / FormatGesture、Native.RegisterHotkey、AppHost.ActionFor |
| 数据目录 | DataLocation.Resolve / SetCustom、Program.Main、AppHost.ValidateDataDirectory / ValidateDatabase / ApplyDataDirectory |

## 3. 重点链路

- 设置窗口非模态、无 Owner：AppHost.Settings 复用单实例并 Show，ShowInTaskbar=true。InitializeFields 建立跨页字段状态，ApplyLanguage 预览并重建控件；非法 Raw / Error 不因切页、切样式或切语言丢失。
- 设置保存：全字段 Check / 快捷键重复校验 → 语言、皮肤、目录预检 → 永久清理确认 → 深拷贝草稿 → Atomic 写 Catalog。提交后更新语言 / 清理基线；开机启动、目录应用各自捕获错误且仅成功才更新基线，再调用 ApplySettingsReport。部分应用失败保持窗口并报告；取消不回滚已提交设置。
- ApplySettingsReport 分别报告语言、各全局快捷键、网络、界面与附着错误；ApplySteps 捕获单项异常并继续后续项。语言加载成功且 appliedLanguage 真正变化时 RefreshLanguageSurfaces：保存编辑、重开管理器 / 堆叠、重建托盘、刷新原便签窗口；全部刷新成功后才推进 appliedLanguage，失败可重试。
- 数据目录：ValidateDataDirectory 只检查路径与已有库，不创建目录 / 数据库 / 指针；ApplyDataDirectory 对空目标用 SQLite Backup 复制，对已有库只切换，再写指针供重启。SettingsWindow 只在 pendingDataDirectory 不同于成功应用基线时调用；选回当前运行目录也必须更新指针。
- 快捷键统一走 Ui.TryParseGesture / FormatGesture：支持无修饰普通键与 F1–F24；全局快捷键保存时必须带修饰键或功能键。全局新建 / 显隐 / 管理分别注册 HotKey ID 1 / 2 / 3；其中管理是 toggle：管理器前台时关闭，后台或未打开时打开。
- 设置表单：Appearance 顶部设置新建便签默认字体 / 整数 px 字号，样式下拉管理颜色 / 字体样式 / 透明度 / Tab 宽度 / 示例 / 阴影 / 皮肤（第一项的字体与字号不再用于新建便签）；Content 管理链接、复选框、Shift+Ctrl 方向、Ctrl+滚轮字号范围、7 个高亮色和图片行为；Alerts 管理唤醒默认值、声音文件与响铃；Desktop 管理启动隐藏、关闭确认、吸附距离 / 间隙和双击标题栏行为。快捷键搜索有持久可见标签；设置标签列 220px，开关整行可点击且文字不重复触发，导航与开关模板提供键盘焦点反馈。

## 4. 接口契约

- Settings.Language：`zh-CN` / `en-US` 为内置资源，其他值为自定义 JSON 路径；ValidateLanguage 只校验，LoadLanguage 切换，Text 映射中文源串。SettingsWindow.originalLanguage 是最后已提交语言；AppHost.appliedLanguage 是最后成功刷新界面的语言，两者不等同预览中的 Ui.CurrentLanguage。
- SettingsFieldState：Key / Category / StyleIndex / ShortcutTab 定位字段，Raw / Error 跨控件重建保留；Check 校验通过才调用 Apply 写草稿。Control / RefreshFeedback 仅引用当前控件，不作为数据源。
- SettingsWindow.settings 与 Catalog.Settings 必须深拷贝隔离；Save 提交独立副本。pendingDataDirectory 是待选目录，appliedDataDirectory 是最后成功写指针的目录，host.DataDirectory 始终是运行库位置；ReloadDraft 回到成功应用目录基线。
- AppHost.ApplySteps 接收命名 Action 序列并返回错误列表，单项失败不中断循环；ApplySettingsReport 返回运行期应用错误，testMode 下不做外部应用。错误不表示 Catalog 提交失败，也不自动回滚已提交设置。
- Settings.Version：Catalog 升级时 Normalize 从旧版本补齐新增选项（链接、阴影、高亮色、吸附距离、图片类型等）；不要只看字段默认值，旧 JSON 缺字段会得到 false/0。Version < 3 会补开六个搜索范围、SearchContent 和 SearchBarExpanded。
- Settings.DefaultFontName / DefaultFontSize 只用于 NoteLibrary.Create 新建便签，默认 Verdana / 10px；DefaultFontSize 与 ZoomMinFontSize / ZoomMaxFontSize 均按整数 px 持久化，范围 1–200，缩放范围默认 5 / 150。Normalize 将旧小数四舍五入（远离零）、补默认值并交换反向缩放范围；ValidateSettings 拒绝非整数。TextEditor.Scale 接收 delta 与 px 范围，返回缩放后最小 / 最大 px Tuple；RTF 以 0.5 pt 精度存储，因此 Read 后必须归一化各段为整数 px，保存往返不承诺保留旧小数值。
- Settings.Shortcuts：空字符串表示停用该应用内快捷键；DisabledShortcuts 保存停用前的手势，仅用于设置界面恢复显示，不参与键盘分发。GlobalNewKey / GlobalToggleKey / GlobalManageKey 为空同样表示关闭。
- 数据目录不在 Catalog 内：DataLocation 的指针文件决定下次启动目录；`--data-dir` 命令行优先，显式参数会覆盖指针。

## 7. 易踩的坑 / 设计取舍

- 语言切换只预览设置草稿；关闭恢复最后已提交语言，不是窗口创建时语言。合法与非法输入都要保留原始字段状态，不能从重建控件的默认值回填。英文以中文源串为 key，文案走 Ui.Text 并补 Localization。
- 实际语言变化比较 AppHost.appliedLanguage，不比较 Ui.CurrentLanguage；加载及 RefreshLanguageSurfaces 成功后才更新应用基线，失败保留旧基线以便重试，不重建便签编辑器。
- 设置窗口必须是非模态任务栏窗口（ShowInTaskbar=true + Show）；ShowDialog / Owner 会让便签无法编辑和选中。
- 数据目录是启动级配置，不替换运行 Repository。预检只读，不保证可写；保存后空目标复制，已有库不覆盖。目录是否需应用比较成功写指针基线而非运行目录；从 B 选回运行目录 A 仍要写指针，无变化保存不得写指针。
- 设置提交与外部应用不是一个事务：Catalog 成功后开机启动 / 目录 / 热键 / 网络等仍可能失败，必须分别报告并允许重试，取消不撤销提交；恢复 / 导入 / 维护不以取消设置回滚。
- 快捷键格式不要只用 KeyGestureConverter：它拒绝无修饰的普通字符键。统一使用 Ui.TryParseGesture / FormatGesture；全局快捷键必须带修饰键或功能键。
- 快捷键启停以空手势表达，不要用“删除自定义项”表示停用，否则会退回默认手势并重新生效。