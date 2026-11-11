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
| 屏幕截取 / 从屏幕新建图片便签 | CaptureWindow.cs、AppHost.Capture | 通信与迁移 |
| 状态、保存、加密和事务 | NoteLibrary.cs、NoteRepository.cs、Models.cs、Crypto.cs | 皮肤和通信 |
| 管理、收藏、标签和堆叠 | ManagerWindow.cs、ManagerWindow.Collections.cs、ManagerWindow.Calendar.cs、ManagerWindow.Search.cs、CollectionEditor.cs、NoteLibrary.cs | 通信协议 |
| 提醒、重复与窗口附着 | ScheduleWindow.cs、RepeatSchedule.cs、AppHost.Schedule / UpdateAttachments | 图片算法 |
| 导入导出、备份与迁移 | Transfer.cs、AtomicFile.cs、StickiesImporter.cs、BackupBrowserWindow.cs、AppHost.Import / Restore | 编辑算法 |
| 托盘、启动、设置与通信 | Program.cs、AppHost.cs、SettingsWindow.cs、Localization.cs、DataLocation.cs、ApiService.cs | 图片算法 |
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
| src/DeskPins.App/ManagerWindow.cs | 管理便签主布局、分类导轨、列表 / 预览、Home 仪表盘、卡片模板 |
| src/DeskPins.App/ManagerWindow.Collections.cs | Stored / Stacks / Tags 大纲、集合新建 / 删除 / 改名 / 属性、树内拖拽排序与文件夹改父目录 |
| src/DeskPins.App/ManagerWindow.Calendar.cs | 自绘月历、日期选择、按日统计与日历筛选 |
| src/DeskPins.App/ManagerWindow.Search.cs | 底部查找栏、搜索范围 / 内容 / Pin、标签过滤与搜索结果筛选 |
| src/DeskPins.App/CollectionEditor.cs | 文件夹 / 堆叠 / 标签属性对话框、搜索标签过滤对话框 |
| src/DeskPins.App/CaptureWindow.cs | 全屏截取与框选，返回 BitmapSource 供 AppHost.Capture 创建图片便签 |
| src/DeskPins.App/AboutWindow.cs | 关于窗口：产品名、版本 / 作者、功能简介与关闭按钮 |
| src/DeskPins.App/CollectionsWindow.cs | 联系人 / 收藏文件夹 / 标签的独立批量管理对话框 |
| src/DeskPins.App/Ui.cs | 公共 UI 辅助：语言加载 / 文本映射、对话框、颜色与快捷键解析 |
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
| 新建 / 显隐 / 托盘 | AppHost.Start / New / Sync / BuildTray / SetHidden / ToggleManage |
| 编辑 / 格式 / 窗口 | NoteWindow.Run / HandleKeyDown / Flush、TextEditor.Format / Read / Write |
| 图片算法 | ImageEditor.BeginStroke / ContinueStroke / CommitStroke / CancelEdit / Transform / Crop / ResizeImage、RasterCanvas |
| 屏幕截取 | AppHost.Capture、CaptureWindow 构造 / Result |
| 关于窗口 | AboutWindow 构造 / VersionText、AppHost.BuildTray 的“关于 DeskPins”菜单项 |
| 状态转换 / 加密 | NoteLibrary.Move / Sleep / Load / Save / SetFolderEncrypted |
| 管理与预览 | ManagerWindow 构造 / BuildTree / Refresh / DisplaySelected / DropTo / ApplyLayout / SavePaneWidths / EditNoteTitle / BeginInlineTitleEdit / SaveManagerState / RestoreManagerState |
| 管理器集合管理 | ManagerWindow.CreateCollectionItem / EditCollection / DeleteCollection / MoveCollectionItem / NewNoteIn / TreeMouseMove / NodeDrop |
| 管理器日历 | ManagerWindow.BuildCalendarView / CalendarDay / MatchesDate |
| 管理器搜索 | ManagerWindow.BuildFindBar / SearchResults / OpenSearchTagFilter、SearchTagWindow |
| 设置布局与校验 | SettingsWindow.BuildCategoryPage / BuildAppearancePage / BuildContentPage / BuildAlertsPage / BuildDesktopPage / BuildDataPage / Save、Ui.FitDialog / Field、AppHost.ValidateSettings / ShortcutFor |
| 语言资源 | Ui.LoadLanguage / Text、Localization.Dictionary、AppHost.ApplySettings / RefreshLanguageSurfaces |
| 快捷键解析 | Ui.TryParseGesture / FormatGesture、Native.RegisterHotkey、AppHost.ActionFor |
| 数据目录 | Program.Main、DataLocation.Resolve / SetCustom、AppHost.ApplyDataDirectory |
| 窗口边缘缩放 | WindowResize.Attach / Calculate、NoteWindow 构造、StackWindow 构造 |
| 拖拽吸附 / 堆叠前置 | AppHost.SnapAfterDrag / BringStacksForward、Native.RaiseNoActivate、StackWindow.BringForward |
| 定时 / 休眠恢复 | NoteLibrary.Tick / NextDue、RepeatSchedule.Next、AppHost.Schedule / Tick |
| 附着显隐 | AppHost.UpdateAttachments / EnsureAttachmentHook、AttachWindow |
| 备份 / 合并 / 选择恢复 | NoteRepository.Backup / ImportBackup、BackupBrowserWindow |
| 原版迁移 / 传输 | StickiesImporter.Read、AppHost.ImportStickies / ImportTransfer、Transfer |
| 插件 / 通信 / 设置 | ApiService.Execute / AddMenus、AppHost.ReceiveLoop / Send / ApplySettings |

## 3. 重点链路

- 文本编辑 → QueueSave（600 ms 合并）→ Flush → RTF / plain → NoteLibrary.Save → SQLite。失去焦点与退出也保存；保存失败保持 dirty，阻止依赖保存成功的状态迁移。
- Move / Sleep → PreparingChange → AppHost.FlushNote（含管理器）→ 持久化新状态 → 排队 Sync；非桌面窗口移除时不再序列化旧编辑器。
- 加密：Encrypted 表示磁盘行状态；ContentDecrypted 只表示当前 Body 已解密。Save 总是重新保护已加载的加密内容并卸载，锁定清除已解密缓存。
- Atomic 将数据库、内存元数据、目录一起回滚，提交完成后只通知一次。传输接收与 receipt 在同一事务；消费文件在事务成功后原子替换，重试不重复创建便签。
- 计时器按最近到期时间唤醒，不做秒级常驻轮询；系统恢复事件重新计算错过的提醒。附着使用 WinEvent hook 与 180 ms 合并。
- 管理器只在目录标识 / 名称变化时重建导航并恢复有效筛选；DropTo 先保存编辑再事务移动，子文件夹 Drop 在验证前标记 Handled，避免冒泡再次移动。
- 图片鼠标事件调用 BeginStroke / ContinueStroke / CommitStroke；提交才建立撤销并通知保存，CancelEdit 恢复笔画前图像。选区操作只更新显示，Escape 由 NoteWindow 转发。
- 设置窗口是非模态任务栏窗口：AppHost.Settings 复用单实例并用 Show() 打开，SettingsWindow.ShowInTaskbar=true，不能 ShowDialog 或设置 Owner，否则会阻塞便签编辑。语言下拉即时预览并重建设置页；保存时 ValidateSettings → Atomic 写 Catalog → ApplySettings。ApplySettings 用 appliedLanguage 判断真实语言变化：重载 Ui、重建托盘、关闭重开管理器与堆叠；便签窗口保留，只刷新静态提示，右键菜单按需生成新语言。取消关闭时恢复原语言。全局与应用内快捷键均以空手势表示停用，DisabledShortcuts 只保存恢复用的上次手势。
- 无边框窗口缩放由 WindowResize 监听 Window 的 Preview 鼠标事件，在四边 6px 内手动改 Left / Top / Width / Height；NoteWindow 完成时写回 Note，StackWindow 写回 NoteStack.Width / Height。缩放期间 NoteWindow 抑制多选拖动与组移动，最小尺寸由调用方传入。
- 便签标题拖动期间 MoveSelected 只同步组内窗口；DragMove 返回后才由 SnapAfterDrag 按屏幕 / 其他便签边缘吸附，避免连续吸附把小幅拖动拉回原位。SnapAfterDrag 移动全部选中窗口；按住 Alt 反向切换吸附。
- 堆叠窗口读取 NoteStack.Topmost，AppHost.NewStack 沿用来源便签的 Topmost；窗口设置 ShowActivated=false。选中或激活便签时 AppHost.BringStacksForward 用 SetWindowPos(NOACTIVATE) 把全部堆叠抬到最前，确保未置顶堆叠被其他窗口遮盖后仍可见。
- TextEditor 在 FlowDocument.Resources 注入 Paragraph Margin=0 隐式样式，并在 Read / TextChanged 归一化段落边距，避免 Paragraph 自动边距形成多余空行。
- NoteWindow 的 OpaqueEditing 只对活动便签强制不透明；用户从右键改透明度或应用样式时 showOpacity 临时绕过，失焦后清除，保证改动立即可见。
- 管理便签为左侧图标栏 + 动态工作区：Stored / Stacks / Tags 显示大纲 + 卡片列表 + 内容三栏，其余分类显示卡片列表 + 内容两栏，Home 为仪表盘。导轨视觉顺序由 CategoryOrder 决定，但每个 ListBoxItem 的 Tag 保存稳定分类 id；不要用 SelectedIndex 当分类 id。大纲只在三个集合分类显示；两条 GridSplitter 的宽度按分类持久化；树 / 列表计数写入快照，避免预览保存时反复清空选择。
- 集合大纲由 Catalog 列表顺序驱动：拖拽排序直接移动 Folders / Stacks / Tags 列表项；文件夹拖到节点上 / 下 1/4 排序，拖到中间 1/2 改 ParentId，禁止拖到自身或后代。拖放数据用 DeskPins.Collection，便签拖放仍用 DeskPins.Notes；节点 Drop 先判断集合数据，再处理便签。集合的增删改属性都走 CollectionEditor，删除后必须清空对应 filter 并重建树。
- 日历由 ManagerWindow.Calendar 自绘：月份标题与星期标签按 Ui.CurrentLanguage 本地化，日格按 MatchesDate 统计创建 / 唤醒 / 重复发生日；修改筛选逻辑时同时更新日历计数和列表筛选，二者共用 MatchesDate。
- 搜索由底部查找栏驱动：SearchResults 先按 SearchContent 选择数据库全文搜索或标题 / Id 内存匹配，再按 SearchTagFilter（任一标签）和六个范围开关过滤。输入 250 ms 防抖；范围 / 标签变化立即刷新并切到 Search 分类。Pin find bar 只在管理器内保持查找栏可见。
- 设置导航共 9 类：应用、桌面、外观、内容、提醒、网络、邮件、快捷键、数据。数据页承载托盘移出的导入 / 备份 / 恢复 / 救回操作；数据目录用 SQLite Backup 复制到空目标，目标已有数据库则直接切换，写 `data-directory.txt` 后重启生效。Program、单实例和 API 都通过 DataLocation.Resolve。
- 快捷键统一走 Ui.TryParseGesture / FormatGesture：支持无修饰普通键与 F1–F24；全局快捷键保存时必须带修饰键或功能键。全局新建 / 显隐 / 管理分别注册 HotKey ID 1 / 2 / 3；其中管理是 toggle：管理器前台时关闭，后台或未打开时打开。
- 带输入框的弹框（`Ui.Ask`、`CollectionEditor`、`AttachWindow`、`ScheduleWindow`）应在 `Loaded` 后通过 Dispatcher 聚焦并全选输入框，避免打开后还要手动点一下。
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
- Settings.Language：`zh-CN` / `en-US` 使用内置资源，其他值视为自定义 JSON 字典路径；Ui.LoadLanguage 负责切换，Ui.Text 只把中文源串映射为英文。
- Settings.Version：Catalog 升级时 Normalize 从旧版本补齐新增选项（链接、阴影、高亮色、吸附距离、图片类型等）；不要只看字段默认值，旧 JSON 缺字段会得到 false/0。Version < 3 会补开六个搜索范围、SearchContent 和 SearchBarExpanded。
- Settings.Search*：SearchDesktop / SearchAttached / SearchStored / SearchSleeping / SearchClosed / SearchRecurring 是搜索范围，SearchContent 控制是否查正文，SearchPinFindBar 让查找栏在其他分类也可见，SearchBarExpanded 记住折叠状态，SearchTagFilter 保存标签 id（任一匹配）。
- Settings.Shortcuts：空字符串表示停用该应用内快捷键；DisabledShortcuts 保存停用前的手势，仅用于设置界面恢复显示，不参与键盘分发。GlobalNewKey / GlobalToggleKey / GlobalManageKey 为空同样表示关闭。
- 数据目录不在 Catalog 内：DataLocation 的指针文件决定下次启动目录；`--data-dir` 命令行优先，显式参数会覆盖指针。
- ManagerOutlineWidths / ManagerListWidths：按分类索引保存管理器的三栏 / 两栏宽度；Home 不显示列表，Stored / Stacks / Tags 保存大纲和列表宽度，其他分类保存列表宽度。
- Settings.ManagerCategory / ManagerFolderFilter / ManagerStackFilter / ManagerTagFilter / ManagerSelectedNoteId / ManagerCalendarDate：管理便签关闭时保存的导航状态；`RestoreManagerState` 在构造时恢复。`categories.SelectionChanged` 会清空三个 filter，所以恢复顺序必须是“选分类 → 再写 filter → BuildTree / Refresh → 选便签”。失效的 folder / stack / tag 或已删除便签直接忽略。

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
- 语言切换是设置草稿的即时预览：取消 / 关闭窗口必须恢复原语言，只有保存成功后才写入 Catalog。英文翻译以中文源串为 key，新增 UI 文案先走 Ui.Text，再补 Localization 条目。
- 语言是否真正变化必须比较 AppHost.appliedLanguage，不能用 Ui.CurrentLanguage（设置预览会提前改它）；保存后刷新托盘、管理器和堆叠，但不要重建便签窗口。
- 设置窗口必须是非模态任务栏窗口（ShowInTaskbar=true + Show）；ShowDialog / Owner 会让便签无法编辑和选中。
- 自动吸附只在 DragMove 返回后执行；不要在 MouseMove 中实时吸附加回窗口，否则小幅拖动会看起来“拖不动”。
- Paragraph 的 Margin 可能是 Auto 或由 RTF 显式带入；TextEditor 必须用隐式样式和归一化保持 0，否则会出现多余空行。
- 堆叠窗口 ShowActivated=false，前置用 SetWindowPos(NOACTIVATE)，不能抢走便签焦点；NoteWindow 的 OpaqueEditing 对活动窗口强制不透明，用户显式改透明度时用 showOpacity 临时绕过。
- 数据目录是启动级配置：不要尝试在运行中替换 NoteLibrary 的 Repository；修改时先保存设置，再备份数据库到目标，写指针文件并提示重启。目标已有数据库时只能切换，不能覆盖。
- 托盘不再承载导入 / 备份 / 恢复 / 救回；这些操作只在设置-数据页，避免两套入口行为漂移。
- 管理便签的内容区是独立第三栏（Home 除外），不要恢复旧的“列表占满 + 隐藏预览”结构；BuildTree 必须把计数纳入快照，否则预览保存会清空选择。
- 管理便签的工作区是分类相关布局：不要在所有分类常驻 Stored folders / Stacks / Tags 大纲；只有 2 / 8 / 9 显示大纲，Home 显示仪表盘，其余显示列表 + 内容。分隔条宽度必须按分类读 ManagerOutlineWidths / ManagerListWidths，保存时不要覆盖其他分类。
- 管理便签的导轨可重排，但分类 id 必须稳定：CategoryOrder 只决定显示顺序，所有筛选、设置字典、布局判断都用 ListBoxItem.Tag；改成 SelectedIndex 会让已保存的列宽 / 排序错位。
- TreeViewItem 拖拽高亮用本地 Background，清理时用 ClearValue 而不是赋 Transparent，否则会压掉默认选中样式。文件夹 Drop 的 1/4、1/2、1/4 分区分别对应排序前、改父目录、排序后；集合排序不要重建 Catalog 字典。
- WPF 绑定不读公共字段。管理器卡片新增展示字段时用属性（如 ManagerTitle / Identifier / DisplayTags、Tag.DisplayName / ColorHex），字段只用于序列化或内部计算。
- 日历与搜索是同一套筛选的两处呈现：日格计数与日历列表必须共用 MatchesDate；搜索的范围 / 标签 / 正文开关必须同时作用于 SearchResults，不要在 Refresh 的 switch 里另外拼一套条件。
- 快捷键格式不要只用 KeyGestureConverter：它拒绝无修饰的普通字符键。统一使用 Ui.TryParseGesture / FormatGesture；全局快捷键必须带修饰键或功能键。
- 快捷键启停以空手势表达，不要用“删除自定义项”表示停用，否则会退回默认手势并重新生效。
- UI 测试使用 `new AppHost(..., testMode: true)`；`Focus` / `BringForward` / `ToggleAll` / `ShowAllNotes` / `BringStacksForward` 等会 `Show()` 窗口的路径必须尊重 `testMode`，否则 `package.bat` 跑 UI 回归时会闪一下便签窗口。
- 便签窗口必须同时设置 `WindowStyle=None` 与 `AllowsTransparency=true`，`Window.Opacity` 才会通过 WPF 分层窗口生效；不要再通过 `SetLayeredWindowAttributes` 手动改非透明窗口的 alpha（WPF 会重置 `WS_EX_LAYERED`）。`Native.SetGhost` 只负责 `WS_EX_TRANSPARENT`。
