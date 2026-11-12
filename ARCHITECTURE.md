# 代码链路图谱 · DeskPins

> 目的：给 AI 与维护者一份「读这一份就能定位代码」的导航地图，避免每次改动或排查都全量扫描。
> 维护：代码结构变化后同步更新（AGENTS §1）；长期有效的坑与设计理由记入 §7；按函数名 / 锚点 grep，不依赖行号。

## 0. 架构

纯 WPF 多窗口应用，AppHost 管理窗口与系统服务，NoteLibrary 管理便签状态，NoteRepository 通过系统 winsqlite3.dll 保存内容。历史便签只加载元数据；界面与内容按需创建，启动按帧逐张恢复桌面窗口。

```text
WPF 便签 / 管理 / 设置
          ↓
便签操作与状态服务
  ├─ 本地存储与备份
  ├─ 提醒与窗口附着
  └─ 网络 / 邮件 / 本地 API
```

## 0.5 任务作用域路由

| 任务 | 首读 | 可忽略 |
|---|---|---|
| 便签编辑和窗口行为 | NoteWindow.cs、TextEditor.cs、ImageEditor.cs、Native.cs、WindowResize.cs | 通信与迁移 |
| 公共 UI、弹框与菜单 | UiTheme.cs、Ui.cs、CollectionEditor.cs、CollectionsWindow.cs、ScheduleWindow.cs、AboutWindow.cs；菜单见 NoteWindow.Menu / TextMenu / ImageMenu、AppHost.BuildTray | 图片算法与通信协议 |
| 设置草稿、校验与分项应用 | SettingsWindow.cs、SettingsFieldState.cs、AppHost.ValidateSettings / ApplySettingsReport、Ui.cs、Localization.cs | 通信协议与迁移 |
| 屏幕截取 / 从屏幕新建图片便签 | CaptureWindow.cs、AppHost.Capture | 通信与迁移 |
| 状态、保存、加密和事务 | NoteLibrary.cs、NoteRepository.cs、Models.cs、Crypto.cs | 皮肤和通信 |
| 管理、收藏、标签和堆叠 | ManagerWindow.cs、ManagerWindow.Collections.cs、ManagerWindow.Calendar.cs、ManagerWindow.Search.cs、CollectionEditor.cs、NoteLibrary.cs | 通信协议 |
| 提醒、重复与窗口附着 | ScheduleWindow.cs、RepeatSchedule.cs、AppHost.Schedule / UpdateAttachments | 图片算法 |
| 导入导出、备份与恢复 | Transfer.cs、AtomicFile.cs、StickiesImporter.cs、BackupBrowserWindow.cs、AppHost.RestoreBackup / RestoreBackupData、NoteLibrary.Reload、NoteRepository.ImportBackup（§3 / §4 / §7） | 编辑算法 |
| 启动级数据目录切换 | DataLocation.cs、Program.Main、SettingsWindow.Save / ReloadDraft、AppHost.ValidateDataDirectory / ValidateDatabase / ApplyDataDirectory | 图片算法与通信协议 |
| 托盘、启动与通信 | Program.cs、AppHost.BuildTray / Start / ReceiveLoop、DataLocation.cs、ApiService.cs | 图片算法 |
| 构建、验证和打包 | manager.sh、scripts/、tests/DeskPins.Tests/Program.cs（§6） | 功能参考 |
| 完成度与接力 | HANDOFF.md、ROADMAP.md、docs/03-功能对照.md | 原版二进制 |

## 1. 文件职责

| 文件或目录 | 职责 |
|---|---|
| AGENTS.md / CLAUDE.md | 工作规则与 Claude 导入入口 |
| ARCHITECTURE.md | 按任务定位实际代码入口 |
| ROADMAP.md | 完整功能计划与完成度 |
| HANDOFF.md | 当前开发状态与下一步 |
| CHANGELOG.md | 完成的用户可感知变化 |
| README.md | 产品介绍与当前可运行状态 |
| docs/ | 技术栈、使用说明、功能对照 |
| .claude/ | 模板显式技能和文档检查 hook |
| check-docs.sh | 模板机械体检 |
| .vibe-template | 模板类型和版本来源 |
| .runtime/ | 忽略入库的参考、测试与构建产物 |
| src/DeskPins.Core/ | 元数据模型、状态、SQLite、加密、重复规则、传输与原版导入 |
| src/DeskPins.Core/Sqlite.cs | 系统 winsqlite3.dll 的最小 P/Invoke 封装，供 NoteRepository 使用 |
| src/DeskPins.App/ | 程序入口、WPF 编辑器 / 管理 / 设置、原生窗口、托盘与 IPC |
| src/DeskPins.App/ManagerWindow.cs | 文字分类导轨、列表 / 预览、Home 仪表盘、选择摘要、显式恢复、保存反馈与列宽约束 |
| src/DeskPins.App/ManagerWindow.Collections.cs | Stored / Stacks / Tags 大纲及“全部”节点、集合属性 / 行内改名 / 删除、树内排序与文件夹改父目录 |
| src/DeskPins.App/ManagerWindow.Calendar.cs | 浅色月历、键盘选日与焦点恢复、按日统计与日历筛选 |
| src/DeskPins.App/ManagerWindow.Search.cs | 查找栏、有效范围摘要、重置 / 标签 / Pin 与搜索结果筛选 |
| src/DeskPins.App/CollectionEditor.cs | 文件夹 / 堆叠 / 标签属性对话框、搜索标签过滤对话框 |
| src/DeskPins.App/CaptureWindow.cs | 全屏截取与框选，返回 BitmapSource 供 AppHost.Capture 创建图片便签 |
| src/DeskPins.App/AboutWindow.cs | 关于窗口：产品名、版本 / 作者、功能简介与关闭按钮 |
| src/DeskPins.App/CollectionsWindow.cs | 联系人完整草稿表单、收藏文件夹 / 标签批量管理；同文件 StackWindow 保持便签自定义配色 |
| src/DeskPins.App/SettingsWindow.cs | 非模态九页设置、独立草稿、全字段校验定位、样式预览、快捷键分组搜索及即时数据维护 |
| src/DeskPins.App/SettingsFieldState.cs | 跨页面 / 样式保留 Raw / Error 与定位信息；Check 仅把合法输入应用到草稿 |
| src/DeskPins.App/UiTheme.cs | 局部窗口字体、系统高对比度色刷、输入与按钮样式；不注入 Application 全局资源 |
| src/DeskPins.App/Ui.cs | 公共 UI：语言映射、滚动弹框与固定页脚、Owner / 工作区约束、菜单状态、颜色与快捷键解析 |
| src/DeskPins.App/AppHost.cs | 窗口 / 托盘协调、设置分项应用、目录只读预检、保护备份恢复与重载失败写阻断 |
| src/DeskPins.App/BackupBrowserWindow.cs | 只读备份列表、多选计数与覆盖说明；恢复统一调用 AppHost.RestoreBackup |
| src/DeskPins.App/Localization.cs | 内置中英文资源；自定义 JSON 语言文件继续由 Ui.LoadLanguage 读取 |
| src/DeskPins.App/DataLocation.cs | 默认 / 自定义数据目录解析与 `%LOCALAPPDATA%/DeskPins/data-directory.txt` 指针文件 |
| src/DeskPins.App/WindowResize.cs | 无边框便签 / 堆叠窗口的边缘拖拽缩放与最小尺寸计算 |
| tests/DeskPins.Tests/ | 无第三方框架的 STA 回归；自建临时数据，结束后清理 |
| scripts/、manager.sh、*.bat | 构建、运行、打包、性能测量与关闭运行实例的统一入口 |
| assets/ | 自制图标和 JSON 皮肤示例 |
| Directory.Build.props、DeskPins.sln | 统一 net48、x64、版本和构建配置 |

## 2. 任务 → 入口

| 任务 | 入口 |
|---|---|
| 启动 / 单实例 / 命令行 | Program.Main、ApiService.Identity / Client |
| 新建 / 显隐 / 托盘 | AppHost.Start / New / Sync / BuildTray / BuildTrayMenu / SetHidden / ToggleManage |
| 编辑 / 格式 / 窗口 | NoteWindow.Run / HandleKeyDown / Flush、TextEditor.Format / Read / Write |
| 图片算法 | ImageEditor.BeginStroke / ContinueStroke / CommitStroke / CancelEdit / Transform / Crop / ResizeImage、RasterCanvas |
| 屏幕截取 | AppHost.Capture、CaptureWindow 构造 / Result |
| 关于窗口 | AboutWindow 构造 / VersionText、AppHost.BuildTray 的“关于 DeskPins”菜单项 |
| 公共弹框 / 按钮 / 菜单 | UiTheme.Apply / ButtonStyle / ComboBoxStyle、Ui.DialogContent / FitDialog / ActiveWindow / PrimaryButton / DangerButton / Item；NoteWindow.Menu / TextMenu / ImageMenu |
| 状态转换 / 加密 / 重载 | NoteLibrary.Move / Sleep / Load / Save / SetFolderEncrypted / Reload |
| 管理布局与预览 | ManagerWindow.Refresh / DisplaySelected / OpenSelected / RestoreSelected / UpdateSelectionSummary / ConstrainPaneWidths / SavePaneWidths / ResizeOutlinePane / PaneSplitter / SelectCategory / ScrollRailToSelection / IconButton |
| 管理编辑 / 菜单 / 状态 | ManagerWindow.SaveEditor / FlushPending / EditNoteTitle / BeginInlineTitleEdit / ShowNoteProperties / Context / SaveManagerState / RestoreManagerState |
| 管理导航与集合 | ManagerWindow.BuildTree / CreateCollectionItem / BeginInlineCollectionRename / EditCollection / DeleteCollection / MoveCollectionItem / NewNoteIn / DropTo / NodeDrop |
| 管理器日历 | ManagerWindow.BuildCalendar / BuildCalendarView / CalendarDay / FocusCalendarDate / MatchesDate |
| 管理器搜索 | ManagerWindow.BuildFindBar / UpdateSearchSummary / ResetSearch / RefreshSearch / SearchResults / OpenSearchTagFilter、SearchTagWindow |
| 设置草稿与校验 | SettingsWindow.BuildWindow / InitializeFields / BindField / LocateField / CheckShortcutFields / Save / ReloadDraft、SettingsFieldState.Check、AppHost.ValidateSettings |
| 设置页面 / 样式 / 快捷键 | SettingsWindow.BuildCategoryPage / BuildAppearancePage / BuildContentPage / BuildAlertsPage / BuildDesktopPage / BuildDataPage / BuildShortcutsPage / ShortcutRow |
| 语言与分项应用 | SettingsWindow.ApplyLanguage、Ui.ValidateLanguage / LoadLanguage / Text、Localization.Dictionary、AppHost.ApplySteps / ApplySettingsReport / RefreshLanguageSurfaces |
| 快捷键解析 | Ui.TryParseGesture / FormatGesture、Native.RegisterHotkey、AppHost.ActionFor |
| 数据目录 | DataLocation.Resolve / SetCustom、Program.Main、AppHost.ValidateDataDirectory / ValidateDatabase / ApplyDataDirectory |
| 窗口边缘缩放 | WindowResize.Attach / Calculate、NoteWindow 构造、StackWindow 构造 |
| 拖拽吸附 / 堆叠前置 | AppHost.SnapAfterDrag / BringStacksForward、Native.RaiseNoActivate、StackWindow.BringForward |
| 定时 / 休眠恢复 | NoteLibrary.Tick / NextDue、RepeatSchedule.Next、AppHost.Schedule / Tick |
| 附着显隐 | AppHost.UpdateAttachments / EnsureAttachmentHook、AttachWindow |
| 备份 / 合并 / 选择恢复 | AppHost.RestoreBackup / RestoreBackupData / Exit、NoteRepository.Backup / ImportBackup、NoteLibrary.Reload、BackupBrowserWindow |
| 原版迁移 / 传输 | StickiesImporter.Read、AppHost.ImportStickies / ImportTransfer、Transfer |
| 插件 / 通信 / 设置 | ApiService.Execute / AddMenus、AppHost.ReceiveLoop / Send / ApplySettings |

## 3. 重点链路

- 文本编辑 → QueueSave（600 ms 合并）→ Flush → RTF / plain → NoteLibrary.Save → SQLite。失去焦点与退出也保存；保存失败保持 dirty，阻止依赖保存成功的状态迁移。
- Move / Sleep → PreparingChange → AppHost.FlushNote（含管理器）→ 持久化新状态 → 排队 Sync；非桌面窗口移除时不再序列化旧编辑器。
- 加密：Encrypted 表示磁盘行状态；ContentDecrypted 只表示当前 Body 已解密。Save 总是重新保护已加载的加密内容并卸载，锁定清除已解密缓存。
- Atomic 将数据库、内存元数据、目录一起回滚，提交完成后只通知一次。传输接收与 receipt 在同一事务；消费文件在事务成功后原子替换，重试不重复创建便签。
- 计时器按最近到期时间唤醒，不做秒级常驻轮询；系统恢复事件重新计算错过的提醒。附着使用 WinEvent hook 与 180 ms 合并。
- 管理器按分类、筛选、集合属性与计数快照决定是否重建导航并恢复有效筛选；DropTo 先保存编辑再事务移动，子文件夹 Drop 在验证前标记 Handled，避免冒泡再次移动。
- 图片鼠标事件调用 BeginStroke / ContinueStroke / CommitStroke；提交才建立撤销并通知保存，CancelEdit 恢复笔画前图像。选区操作只更新显示，Escape 由 NoteWindow 转发。
- 设置窗口非模态、无 Owner：AppHost.Settings 复用单实例并 Show，ShowInTaskbar=true。InitializeFields 建立跨页字段状态，ApplyLanguage 预览并重建控件；非法 Raw / Error 不因切页、切样式或切语言丢失。
- 设置保存：全字段 Check / 快捷键重复校验 → 语言、皮肤、目录预检 → 永久清理确认 → 深拷贝草稿 → Atomic 写 Catalog。提交后更新语言 / 清理基线；开机启动、目录应用各自捕获错误且仅成功才更新基线，再调用 ApplySettingsReport。部分应用失败保持窗口并报告；取消不回滚已提交设置。
- ApplySettingsReport 分别报告语言、各全局快捷键、网络、界面与附着错误；ApplySteps 捕获单项异常并继续后续项。语言加载成功且 appliedLanguage 真正变化时 RefreshLanguageSurfaces：保存编辑、重开管理器 / 堆叠、重建托盘、刷新原便签窗口；全部刷新成功后才推进 appliedLanguage，失败可重试。
- 无边框窗口缩放由 WindowResize 监听 Window 的 Preview 鼠标事件，在四边 6px 内手动改 Left / Top / Width / Height；NoteWindow 完成时写回 Note，StackWindow 写回 NoteStack.Width / Height。缩放期间 NoteWindow 抑制多选拖动与组移动，最小尺寸由调用方传入。
- 便签标题拖动期间 MoveSelected 只同步组内窗口；DragMove 返回后才由 SnapAfterDrag 按屏幕 / 其他便签边缘吸附，避免连续吸附把小幅拖动拉回原位。SnapAfterDrag 移动全部选中窗口；按住 Alt 反向切换吸附。
- 堆叠窗口读取 NoteStack.Topmost，AppHost.NewStack 沿用来源便签的 Topmost；窗口设置 ShowActivated=false。选中或激活便签时 AppHost.BringStacksForward 用 SetWindowPos(NOACTIVATE) 把全部堆叠抬到最前，确保未置顶堆叠被其他窗口遮盖后仍可见。
- TextEditor 在 FlowDocument.Resources 注入 Paragraph Margin=0 隐式样式，并在 Read / TextChanged 归一化段落边距，避免 Paragraph 自动边距形成多余空行。
- NoteWindow 的 OpaqueEditing 只对活动便签强制不透明；用户从右键改透明度或应用样式时 showOpacity 临时绕过，失焦后清除，保证改动立即可见。
- 管理便签为 60px 图标 + 小字导轨与动态工作区：导轨项是 30px 图标 + 居中 9px 浅色说明（分类 10 固定显示 Search），Tag 保存稳定分类 id；Stored / Stacks / Tags 显示大纲 + 卡片列表 + 内容三栏，其余分类显示卡片列表 + 内容两栏，Home 为仪表盘。导轨视觉顺序由 CategoryOrder 决定，不要用 SelectedIndex 当分类 id。列表头部第 1 行是分类标题 + 右对齐计数，第 2 行是图标操作组 + 右对齐排序下拉（宽度按最长文案 80–96px）；预览元信息与操作按钮同一行、按钮靠右。大纲列首次访问使用星号 Fill，之后按分类保存的像素宽度恢复；窗口变宽时未拖拽的首次布局与预览分担剩余空间。三条集合分类的大纲选中行使用 Selected 浅色背景。管理器滚动条统一压到 6px。树 / 列表计数写入快照，避免预览保存时反复清空选择。ScrollRailToSelection 在筛选、搜索栏展开或导轨尺寸变化后把当前分类滚入可见。
- 第一分隔条使用自定义 Thumb + ResizeOutlinePane，只同时调整大纲和列表且保持两者总宽不变，第二分隔条与预览不动；大纲最小 100px、列表最小 260px。第二分隔条仍用 GridSplitter 调整列表与预览。不要换回两个 GridSplitter 的 PreviousAndNext 组合，否则拖动第一条时第二条会跟着移动。
- 集合大纲由 Catalog 列表顺序驱动，三个集合分类都有 Tag="all" 的“全部”节点，选中后清空筛选且禁用属性 / 删除。拖拽排序移动 Folders / Stacks / Tags 列表项；文件夹节点上 / 下 1/4 排序，中间 1/2 改 ParentId，禁止自身 / 后代。DeskPins.Collection 与 DeskPins.Notes 分流；属性走 CollectionEditor，行内改名走 BeginInlineCollectionRename，删除后清筛选并重建树。
- 日历由 ManagerWindow.Calendar 自绘，月份 / 星期按 Ui.CurrentLanguage 本地化，计数和列表共用 MatchesDate。月历固定为紧凑 6 周并直接放入日历宿主，不嵌套 ScrollViewer；CalendarDay 支持方向键、PageUp / PageDown 和 Home，FocusCalendarDate 恢复焦点。
- 搜索由查找栏驱动：SearchResults 先保存预览，再按 SearchContent 选择全文或标题 / Id 匹配，最后应用任一标签与六范围筛选。输入 250 ms 防抖，范围 / 标签变化立即刷新；UpdateSearchSummary 显示有效条件，ResetSearch 清关键词 / 标签并开启六范围与正文，保留 Pin / 折叠状态。
- 管理器 OpenSelected 只查看 / 编辑历史便签，不迁移状态；恢复按钮单独走 RestoreSelected。UpdateSelectionSummary 和 Context 根据选择数量 / 状态启停并标明批量后果。SaveEditor 成功才清 dirty，失败保留修改和重试反馈；ConstrainPaneWidths 随工作区尺寸约束栏宽，再按分类保存。
- 管理器的集合与预览操作使用 IconButton：固定 32×30、Segoe MDL2 图标、ToolTip 与 AutomationProperties.Name；列表 / 大纲 / 预览各占一行水平排列。空态 CTA 与搜索按钮保留文字；查找栏的关闭 X 已改为重置搜索图标。排序下拉使用 UiTheme.ComboBoxStyle：平面边框、居中文本、自绘下拉项。
- 设置数据页即时维护始终作用于当前运行目录，不提交设置草稿。RestoreBackup 只读预检、说明覆盖数并确认 → RestoreBackupData 保存全部编辑、创建唯一保护备份 → ImportBackup 事务提交 → Reload 完整读取后交换内存 → 无保存关闭旧便签、同步界面并分项应用设置。成功后 ReloadDraft 深拷贝新设置、丢弃草稿与密码输入；语言加载失败回退中文仍重建控件。
- 恢复提交后的 Reload 失败不回滚已提交数据库：AppHost 标记 storageFailed / Exiting、停计时器并关闭 Repository，阻断旧数据写入；Exit 走无保存退出分支，管理器关闭 / 自动保存通过 StorageFailed 跳过旧内存保存。
- 数据目录：ValidateDataDirectory 只检查路径与已有库，不创建目录 / 数据库 / 指针；ApplyDataDirectory 对空目标用 SQLite Backup 复制，对已有库只切换，再写指针供重启。SettingsWindow 只在 pendingDataDirectory 不同于成功应用基线时调用；选回当前运行目录也必须更新指针。
- 快捷键统一走 Ui.TryParseGesture / FormatGesture：支持无修饰普通键与 F1–F24；全局快捷键保存时必须带修饰键或功能键。全局新建 / 显隐 / 管理分别注册 HotKey ID 1 / 2 / 3；其中管理是 toggle：管理器前台时关闭，后台或未打开时打开。
- 公共弹框：Ui.DialogContent 提供滚动正文与固定按钮页脚，FitDialog 限工作区并为非任务栏窗口补活动 Owner；PrimaryButton / DangerButton 区分主要 / 危险动作，确认默认 No。输入框在 Loaded 后 Dispatcher 聚焦 / 全选；集合、联系人、提醒、附着验证失败定位字段，危险动作独立并明确后果。
- 菜单按需生成：Ui.Item 接收 check / enabled；NoteWindow.Menu 标明关闭可恢复、永久删除及传输后关闭，TextMenu / ImageMenu 按锁定、选择、剪贴板和 CanUndo / CanRedo 启停相应操作。BuildTray 分新建、显隐 / 管理、工具与退出组；数据维护只在设置页。
- 设置表单：Appearance 使用样式下拉 + 颜色 / 字体 / 透明度 / Tab 宽度 / 示例 / 阴影 / 皮肤；Content 管理链接、复选框、Shift+Ctrl 方向、7 个高亮色和图片行为；Alerts 管理唤醒默认值、声音文件与响铃；Desktop 管理启动隐藏、关闭确认、吸附距离 / 间隙和双击标题栏行为。

## 4. 接口契约

- Note：序列化元数据与非序列化 Body / PlainText 分离；Id 是 GUID 字符串，创建新身份用 Clone。State 只有 Desktop / Closed / Stored / Sleeping / Recurring，附着与堆叠为独立字段。
- Note 的管理器展示字段：ManagerTitle 在 Closed 分类显示 `id <ShortId> <标题>`（ShortId 取 Id 前 8 位，完整 Id 在 Identifier / 卡片 ToolTip）；DisplayTags 是 Refresh 时从 Catalog.Tags 解析出的 Tag 列表，仅供 WPF 绑定。WPF 绑定只认属性，不认字段。
- 集合顺序即 Catalog.Folders / Stacks / Tags 的列表顺序；ManagerOrder 等字典仍按稳定分类 id 保存。Folder.ParentId 决定层级，拖动排序时先调整列表顺序再写 ParentId。
- Note 的卷起独立位置使用 ExpandedLeft / ExpandedTop / RolledLeft / RolledTop 可空字段；只有 Settings.RolledDifferentPosition 打开时才读写。
- NoteStack：Left / Top / Width / Height 均持久化；旧数据缺尺寸时 Catalog.Normalize 补 180×44，最小 100×32。
- Body：文本为 RTF，图片为 PNG。Body=null 表示内容未加载，元数据更新不能改写内容。元数据读取失败不重置数据库。
- 网络：CArchive 长度帧 + UTF-8 `#sticky` 属性；最大 16 MiB。包含内容、颜色、位置、回复端口；原版 Unicode 与 FORM 等高级字段仍待核验。
- IPC：按数据目录和当前用户 SID 派生的 Named Pipe，仅当前用户访问；支持部分 Stickies 风格命令和菜单事件，不宣称完整原版 API 兼容。
- DeskPins 文件：`.dpn` 包含元数据、内容和版本；`.dpx` 是带接收身份的包列表；原版 `.sti` / transfer.ini 的完整兼容未验证。
- TextEditor.PlainText / SelectedPlainText 从当前文档读取；在保存合并延迟内，复制、自动宽度和替换判定不能依赖 Note.PlainText 缓存。ShortcutFor 同时供菜单和键盘分发读取。
- Settings.Language：`zh-CN` / `en-US` 为内置资源，其他值为自定义 JSON 路径；ValidateLanguage 只校验，LoadLanguage 切换，Text 映射中文源串。SettingsWindow.originalLanguage 是最后已提交语言；AppHost.appliedLanguage 是最后成功刷新界面的语言，两者不等同预览中的 Ui.CurrentLanguage。
- SettingsFieldState：Key / Category / StyleIndex / ShortcutTab 定位字段，Raw / Error 跨控件重建保留；Check 校验通过才调用 Apply 写草稿。Control / RefreshFeedback 仅引用当前控件，不作为数据源。
- SettingsWindow.settings 与 Catalog.Settings 必须深拷贝隔离；Save 提交独立副本。pendingDataDirectory 是待选目录，appliedDataDirectory 是最后成功写指针的目录，host.DataDirectory 始终是运行库位置；ReloadDraft 回到成功应用目录基线。
- AppHost.ApplySteps 接收命名 Action 序列并返回错误列表，单项失败不中断循环；ApplySettingsReport 返回运行期应用错误，testMode 下不做外部应用。错误不表示 Catalog 提交失败，也不自动回滚已提交设置。
- AppHost.ValidateDatabase 用只读 Sqlite 检查 quick_check、user_version、便签 Id / 元数据和 Catalog；不证明目标目录可写。RestoreBackup 返回用户确认后的恢复结果；RestoreBackupData 不弹确认，返回唯一保护备份路径，合并 / 选择恢复同 Id 会覆盖当前版本，完整恢复替换便签与设置。
- NoteLibrary.Reload 先在局部完整读取 Catalog 与按 Id 建立元数据字典，再更新 Catalog / Notes 并清 SessionPassword；读取或重复 Id 失败保持旧内存。ImportBackup 已提交后 Reload 失败必须关闭 Repository，旧内存不能继续写回。
- UiTheme.Apply 只修改目标窗口及其 Resources；Ui.DialogContent 的按钮不随正文滚动。UiTheme.ComboBoxStyle 提供平面边框、居中文本和自绘 Popup/ComboBoxItem，供管理器排序使用。Ui.Item 的 check=null 表示非勾选菜单，enabled 决定可用性；ImageEditor.CanUndo / CanRedo 同时检查 Locked 与各自历史栈。
- Settings.Version：Catalog 升级时 Normalize 从旧版本补齐新增选项（链接、阴影、高亮色、吸附距离、图片类型等）；不要只看字段默认值，旧 JSON 缺字段会得到 false/0。Version < 3 会补开六个搜索范围、SearchContent 和 SearchBarExpanded。
- Settings.Search*：SearchDesktop / SearchAttached / SearchStored / SearchSleeping / SearchClosed / SearchRecurring 是搜索范围，SearchContent 控制是否查正文，SearchPinFindBar 让查找栏在其他分类也可见，SearchBarExpanded 记住折叠状态，SearchTagFilter 保存标签 id（任一匹配）。
- Settings.Shortcuts：空字符串表示停用该应用内快捷键；DisabledShortcuts 保存停用前的手势，仅用于设置界面恢复显示，不参与键盘分发。GlobalNewKey / GlobalToggleKey / GlobalManageKey 为空同样表示关闭。
- 数据目录不在 Catalog 内：DataLocation 的指针文件决定下次启动目录；`--data-dir` 命令行优先，显式参数会覆盖指针。
- ManagerOutlineWidths / ManagerListWidths：按分类索引保存管理器的三栏 / 两栏宽度；Home 不显示列表，Stored / Stacks / Tags 保存大纲和列表宽度，其他分类保存列表宽度。切换分类前 SaveCategoryState 写入当前实际宽度，返回时 ApplyLayout 优先恢复该像素值（无记录才首次 Fill）。
- Settings.ManagerSelectedNoteIds / ManagerFilters：按分类保存选中的便签 Id 列表和 folder / stack / tag 筛选；切换分类、刷新和关闭管理器都保留。ManagerCategory / ManagerFolderFilter / ManagerStackFilter / ManagerTagFilter / ManagerSelectedNoteId / ManagerCalendarDate 继续保存最后活动分类的兼容状态，`RestoreManagerState` 在构造时恢复；失效的 Id 或已删除便签直接忽略。

## 5. 样式 / 资源分段地图（可选）

| 区段 | 锚点 / 路径 | 内容 |
|---|---|---|
| 程序图标 | `assets/icons/DeskPins.ico`、`DeskPins.App.csproj` 的 `ApplicationIcon` | 程序与窗口图标 |
| JSON 皮肤 | `assets/skins/*.json`、`AppHost.Skin` / `ValidateSkin` | 标题栏颜色、按钮动作；示例见 `classic-plus.json` |
| 颜色 / 阴影 | `Ui.Brush` / `Ui.NoteShadow` | 便签与管理器共用的画刷、阴影生成 |
| 语言资源 | `Localization.cs`、`Ui.Text` | 内置中英文映射；自定义 JSON 由 `Ui.LoadLanguage` 读取 |

---

## 6. 运维 / 脚本（不碰业务即可独立维护）

> 做构建 / 打包 / 文档检查任务只读本节、`manager.sh`、`scripts/` 与 `*.bat`，不用打开业务代码；具体操作见 docs/02。

- **命令唯一来源**：`manager.sh` 分发到 `scripts/*.ps1`；`check-docs` 直接调用 `check-docs.sh` 并透传参数（如 `--strict`）。
- **双击脚本**：`build.bat` / `dev.bat` / `run.bat` / `test.bat` / `package.bat` 直接调用对应 `scripts/*.ps1`，不依赖 Git Bash；`dev.bat` 构建前用 `scripts/ensure-closed.ps1 -Force` 关闭运行中的 DeskPins，`package.bat` 用同一脚本先询问。`verify.bat` 先构建 + 回归，再用 Git Bash 跑 `check-docs.sh`。失败时暂停，`DESKPINS_NO_PAUSE=1` 可跳过。
- **脚本分工**：`ensure-closed.ps1` 检测 / 关闭 DeskPins（`-Force` 不询问，否则 `Read-Host` 确认）；`build.ps1` Release 构建（`-Test` 追加回归）并复制到 `.runtime/build/dev/`；`run.ps1` 启动（`-Development` 先构建）；`package.ps1` 构建 Debug / Release、跑回归，按 `dist/dev` 与 `dist/release` 出包；`PackageTransaction.ps1` 负责 zip 校验与发布回滚；`benchmark.ps1` 独立数据测量；`smoke-test.ps1` 从 `dist/release` 的 zip 解压并以隔离数据启动、验证单实例导入。
- **出包布局**：模板 `dotnet-native-desktop` 的默认 `package.ps1` 只把单个 zip 写到 `dist/`；本项目按 explorer-tabs / git-extensions 的约定改为 `dist/dev/DeskPins_<版本>_x64-unpack-debug.zip`（Debug + PDB + 构建日志 + `build-info.txt`）与 `dist/release/unpack/` + `dist/release/DeskPins_<版本>_x64-unpack.zip` + `SHA256SUMS.txt`（另含 `unpack-manifest.json`）。发布前校验 zip；失败时保留旧包并回滚，不删除历史 `dist/` 内容。
- **版本号**：只在 `Directory.Build.props`；打包脚本从该文件读取。
- **已知限制**：开发实例运行时 `.runtime/build/dev/DeskPins.exe` 被占用，`build.ps1` / `test.bat` / `verify.bat` 的复制步骤会失败；`dev.bat` / `package.bat` 会先处理运行实例。

---

## 7. 易踩的坑 / 设计取舍

- 纯 WPF 是用户明确选择；不引入 WebView / Electron / React 界面。
- 功能参考与实现证据分别记录；功能名称存在不代表行为已经等价。
- 不把历史便签全部创建为窗口，仅为需要显示的便签创建界面。
- SQLite 使用系统 DLL，开启 FULL synchronous 和 secure_delete；只对当前库中的记录提供保护，旧备份不随之后的加密设置改变。
- 不在 UI 线程播放完整唤醒声音；网络 I/O 异步，保存与短事务在 UI 线程串行执行。
- 性能基准是程序入口到 Dispatcher 空闲，并非已经确认的首帧；截图失效时禁止把离屏对象测试当成视觉验收。
- 离屏布局用显式 Measure / Arrange 和 RenderTargetBitmap.Render(root)；VisualBrush 的自动布局可能重新测量根节点，不能用缩放后的图像验证实际控件尺寸。测试图只用示例内容，清除路径字段后生成。
- 便签 / 堆叠使用 WindowStyle=None + ResizeMode.NoResize，没有原生非客户区缩放；不要再通过 WM_NCLBUTTONDOWN 模拟，改用 WindowResize 的应用内命中与拖拽。
- 语言切换只预览设置草稿；关闭恢复最后已提交语言，不是窗口创建时语言。合法与非法输入都要保留原始字段状态，不能从重建控件的默认值回填。英文以中文源串为 key，文案走 Ui.Text 并补 Localization。
- 实际语言变化比较 AppHost.appliedLanguage，不比较 Ui.CurrentLanguage；加载及 RefreshLanguageSurfaces 成功后才更新应用基线，失败保留旧基线以便重试，不重建便签编辑器。
- 设置窗口必须是非模态任务栏窗口（ShowInTaskbar=true + Show）；ShowDialog / Owner 会让便签无法编辑和选中。
- 自动吸附只在 DragMove 返回后执行；不要在 MouseMove 中实时吸附加回窗口，否则小幅拖动会看起来“拖不动”。
- Paragraph 的 Margin 可能是 Auto 或由 RTF 显式带入；TextEditor 必须用隐式样式和归一化保持 0，否则会出现多余空行。
- 堆叠窗口 ShowActivated=false，前置用 SetWindowPos(NOACTIVATE)，不能抢走便签焦点；NoteWindow 的 OpaqueEditing 对活动窗口强制不透明，用户显式改透明度时用 showOpacity 临时绕过。
- 数据目录是启动级配置，不替换运行 Repository。预检只读，不保证可写；保存后空目标复制，已有库不覆盖。目录是否需应用比较成功写指针基线而非运行目录；从 B 选回运行目录 A 仍要写指针，无变化保存不得写指针。
- 设置提交与外部应用不是一个事务：Catalog 成功后开机启动 / 目录 / 热键 / 网络等仍可能失败，必须分别报告并允许重试，取消不撤销提交；恢复 / 导入 / 维护不以取消设置回滚。
- 恢复前必须确认同 Id 覆盖并创建唯一保护备份。Reload 在完整读取前不能清内存；提交后重载失败必须 storageFailed / Exiting、关闭 Repository 阻断旧写，不能把应用失败说成恢复未提交。
- StorageFailed 是恢复提交后重载失败的写阻断状态：Repository 关闭阻断所有数据库写入，ManagerWindow.Closing / SaveEditor 跳过保存；AppHost.Dispose 以 CloseInternal(false) 关闭便签，Exit 仍允许无保存退出。
- UiTheme 只覆盖工具窗口，便签正文与堆叠保留用户色彩；不要向 Application 注入隐式输入样式。FitDialog 对非任务栏窗口补 Owner，设置窗口必须保持 ShowInTaskbar=true / 无 Owner；按钮 / 菜单的启停与明确文案不能代替处理函数的安全检查。
- 托盘不再承载导入 / 备份 / 恢复 / 救回；这些操作只在设置-数据页，避免两套入口行为漂移。BuildTrayMenu 独立构造、不显示图标，供 testMode 回归；菜单只能向 ShortcutFor 查询 DefaultShortcuts 已登记动作，截屏无快捷键不查询，新建提示读取 GlobalNewKey。
- 管理便签的内容区是独立第三栏（Home 除外），不要恢复旧的“列表占满 + 隐藏预览”结构；BuildTree 必须把计数纳入快照，否则预览保存会清空选择。
- 管理便签的工作区是分类相关布局：不要在所有分类常驻 Stored folders / Stacks / Tags 大纲；只有 2 / 8 / 9 显示大纲，Home 显示仪表盘，其余显示列表 + 内容。第一分隔条必须保持大纲+列表总宽不变（自定义 Thumb），第二分隔条独立调整列表与预览；大纲最小 100px、列表最小 260px。分隔条宽度按分类读取 / 保存 ManagerOutlineWidths / ManagerListWidths，保存时不要覆盖其他分类。
- 管理便签的导轨可重排，但分类 id 必须稳定：CategoryOrder 只决定显示顺序，所有筛选、设置字典、布局判断都用 ListBoxItem.Tag；改成 SelectedIndex 会让已保存的列宽 / 排序错位。
- TreeViewItem 拖拽高亮用本地 Background，清理时用 ClearValue 而不是赋 Transparent，否则会压掉默认选中样式。文件夹 Drop 的 1/4、1/2、1/4 分区分别对应排序前、改父目录、排序后；集合排序不要重建 Catalog 字典。
- WPF 绑定不读公共字段。管理器卡片新增展示字段时用属性（如 ManagerTitle / Identifier / DisplayTags、Tag.DisplayName / ColorHex），字段只用于序列化或内部计算。
- 日历与搜索是同一套筛选的两处呈现：日格计数与日历列表必须共用 MatchesDate；搜索的范围 / 标签 / 正文开关必须同时作用于 SearchResults，不要在 Refresh 的 switch 里另外拼一套条件。月历不要套内部 ScrollViewer：用 26px 日格和 32px 月份栏固定 6 周，最小窗口仍要让便签列表 ≥120px。
- 管理器操作按钮统一用 IconButton（32×30、图标 + ToolTip + 自动化名称），不要恢复尺寸不一的文字按钮组；空态 CTA 与搜索按钮保留文字，查找栏重置/展开使用图标。排序下拉必须使用 UiTheme.ComboBoxStyle（平面边框、居中文本、自绘 Popup 和菜单项），否则会退回系统灰色下拉样式；新增操作时同时检查列表、大纲、预览三行是否各自单行对齐。
- ScrollViewer 默认模板会给 ScrollBar 写入系统 17px 宽度，单靠隐式 Style 的 Width setter 不生效；ManagerWindow.ApplyThinScrollBars 在 Loaded / LayoutUpdated 后把实际 ScrollBar 的 Width / Height 设为 6px。不要把“细滚动条”只做成 UiTheme 样式而不验证 ActualWidth。
- 分类宽度 / 选中 / 筛选在切换前由 SaveCategoryState 写入 Settings，切换后再 RestoreCategoryState / RefreshCore 恢复；只靠关闭时 SaveManagerState 会导致切回分类重置。首次访问无保存宽度才 Fill，之后恢复像素值。
- 管理器的集合大纲使用自定义 TreeViewItem 模板：默认模板的系统选中色会覆盖样式 Background，必须由模板的 Bd 触发器绘制 Selected 浅色背景；展开按钮绑定 IsExpanded，叶子节点隐藏。显式创建的 TreeViewItem 也要赋 Style（只设 ItemContainerStyle 不够），否则子目录会用默认模板导致计数列不对齐；缩进通过每层 Padding 实现，ItemsPresenter 不再加左边距，计数才能统一贴右。不要只改 ItemContainerStyle 的 Background 就宣称选中色已改。
- 快捷键格式不要只用 KeyGestureConverter：它拒绝无修饰的普通字符键。统一使用 Ui.TryParseGesture / FormatGesture；全局快捷键必须带修饰键或功能键。
- 快捷键启停以空手势表达，不要用“删除自定义项”表示停用，否则会退回默认手势并重新生效。
- UI 测试使用 `new AppHost(..., testMode: true)`；`Focus` / `BringForward` / `ToggleAll` / `ShowAllNotes` / `BringStacksForward` 等会 `Show()` 窗口的路径必须尊重 `testMode`，否则 `package.bat` 跑 UI 回归时会闪一下便签窗口。
- 便签窗口必须同时设置 `WindowStyle=None` 与 `AllowsTransparency=true`，`Window.Opacity` 才会通过 WPF 分层窗口生效；不要再通过 `SetLayeredWindowAttributes` 手动改非透明窗口的 alpha（WPF 会重置 `WS_EX_LAYERED`）。`Native.SetGhost` 只负责 `WS_EX_TRANSPARENT`。
