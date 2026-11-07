# 代码链路图谱 · DeskPins

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
| 便签编辑和窗口行为 | NoteWindow.cs、TextEditor.cs、ImageEditor.cs、Native.cs | 通信与迁移 |
| 状态、保存、加密和事务 | NoteLibrary.cs、NoteRepository.cs、Models.cs、Crypto.cs | 皮肤和通信 |
| 管理、收藏、标签和堆叠 | ManagerWindow.cs、CollectionsWindow.cs、NoteLibrary.cs | 通信协议 |
| 提醒、重复与窗口附着 | ScheduleWindow.cs、RepeatSchedule.cs、AppHost.Schedule / UpdateAttachments | 图片算法 |
| 导入导出、备份与迁移 | Transfer.cs、AtomicFile.cs、StickiesImporter.cs、BackupBrowserWindow.cs、AppHost.Import / Restore | 编辑算法 |
| 托盘、启动、设置与通信 | Program.cs、AppHost.cs、SettingsWindow.cs、ApiService.cs | 图片算法 |
| 构建、验证和打包 | manager.sh、scripts/、tests/DeskPins.Tests/Program.cs | 功能参考 |
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
| src/DeskPins.App/ | 程序入口、WPF 编辑器 / 管理 / 设置、原生窗口、托盘与 IPC |
| tests/DeskPins.Tests/ | 无第三方框架的 STA 回归；自建临时数据，结束后清理 |
| scripts/、manager.sh、*.bat | 构建、运行、打包、性能测量的统一入口 |
| assets/ | 自制图标和 JSON 皮肤示例 |
| Directory.Build.props、DeskPins.sln | 统一 net48、x64、版本和构建配置 |

## 2. 任务 → 入口

| 任务 | 入口 |
|---|---|
| 启动 / 单实例 / 命令行 | Program.Main、ApiService.Identity / Client |
| 新建 / 显隐 / 托盘 | AppHost.Start / New / Sync / BuildTray / SetHidden |
| 编辑 / 格式 / 窗口 | NoteWindow.Run / HandleKeyDown / Flush、TextEditor.Format / Read / Write |
| 图片算法 | ImageEditor.BeginStroke / ContinueStroke / CommitStroke / CancelEdit / Transform / Crop / ResizeImage、RasterCanvas |
| 状态转换 / 加密 | NoteLibrary.Move / Sleep / Load / Save / SetFolderEncrypted |
| 管理与预览 | ManagerWindow.Refresh / BuildTree / DisplaySelected / FlushPending / DropTo |
| 布局与设置校验 | Ui.FitDialog / Field、SettingsWindow、AppHost.ValidateSettings / ShortcutFor |
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

## 4. 接口契约

- Note：序列化元数据与非序列化 Body / PlainText 分离；Id 是字符串，创建新身份用 Clone。State 只有 Desktop / Closed / Stored / Sleeping / Recurring，附着与堆叠为独立字段。
- Body：文本为 RTF，图片为 PNG。Body=null 表示内容未加载，元数据更新不能改写内容。元数据读取失败不重置数据库。
- 网络：CArchive 长度帧 + UTF-8 `#sticky` 属性；最大 16 MiB。包含内容、颜色、位置、回复端口；原版 Unicode 与 FORM 等高级字段仍待核验。
- IPC：按数据目录和当前用户 SID 派生的 Named Pipe，仅当前用户访问；支持部分 Stickies 风格命令和菜单事件，不宣称完整原版 API 兼容。
- DeskPins 文件：`.dpn` 包含元数据、内容和版本；`.dpx` 是带接收身份的包列表；原版 `.sti` / transfer.ini 的完整兼容未验证。
- TextEditor.PlainText / SelectedPlainText 从当前文档读取；在保存合并延迟内，复制、自动宽度和替换判定不能依赖 Note.PlainText 缓存。ShortcutFor 同时供菜单和键盘分发读取。

## 7. 设计约束

- 纯 WPF 是用户明确选择；不引入 WebView / Electron / React 界面。
- 功能参考与实现证据分别记录；功能名称存在不代表行为已经等价。
- 不把历史便签全部创建为窗口，仅为需要显示的便签创建界面。
- SQLite 使用系统 DLL，开启 FULL synchronous 和 secure_delete；只对当前库中的记录提供保护，旧备份不随之后的加密设置改变。
- 不在 UI 线程播放完整唤醒声音；网络 I/O 异步，保存与短事务在 UI 线程串行执行。
- 性能基准是程序入口到 Dispatcher 空闲，并非已经确认的首帧；截图失效时禁止把离屏对象测试当成视觉验收。
- 离屏布局用显式 Measure / Arrange 和 RenderTargetBitmap.Render(root)；VisualBrush 的自动布局可能重新测量根节点，不能用缩放后的图像验证实际控件尺寸。测试图只用示例内容，清除路径字段后生成。
