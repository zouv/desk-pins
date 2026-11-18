# 代码链路图谱 · DeskPins

> 目的：给 AI 与维护者一份「读这一份就能定位代码」的导航地图，避免每次改动或排查都全量扫描。
> 维护：代码结构变化后同步更新（AGENTS §1）；长期有效的坑与设计理由记入 §7；按函数名 / 锚点 grep，不依赖行号。
> 结构：总览只含 §0 / §0.5 / §1；§2 任务入口、§3 重点链路、§4 接口契约、§7 易踩坑按域拆在 `arch/<module>.md`（小节号不变），由 §0.5「入口小节」列定位；§5 样式地图与 §6 运维在 `arch/ops.md`。先读总览定位分册，再只读那一份。

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

| 任务 | 首读 | 入口小节 | 可忽略 |
|---|---|---|---|
| 便签编辑和窗口行为 | NoteWindow.cs、AppHost.cs、TextEditor.cs、TextFormatWindow.cs、ImageEditor.cs、Native.cs、WindowResize.cs | arch/notes.md | 通信与迁移 |
| Markdown 输入 / 粘贴 / 导出 | TextEditor.cs、NoteWindow.cs（TextMenu）、MarkdownEditing.cs、MarkdownRenderer.cs、MarkdownExporter.cs | arch/notes.md | 通信与迁移 |
| 公共 UI、弹框与菜单 | UiTheme.cs、Ui.cs、TextFormatWindow.cs、NotePropertiesWindow.cs、CollectionEditor.cs、CollectionsWindow.cs、ScheduleWindow.cs、AboutWindow.cs；菜单见 NoteWindow.Menu / TextMenu / ImageMenu、AppHost.BuildTray | arch/notes.md | 图片算法与通信协议 |
| 屏幕截取 / 从屏幕新建图片便签 | CaptureWindow.cs、AppHost.Capture | arch/notes.md | 通信与迁移 |
| 管理、收藏、标签和堆叠 | ManagerWindow.cs、ManagerWindow.Collections.cs、ManagerWindow.Calendar.cs、ManagerWindow.Search.cs、CollectionEditor.cs、CollectionsWindow.cs(StackDockWindow)、NoteLibrary.cs | arch/manager.md | 通信协议 |
| 设置草稿、校验与分项应用 | SettingsWindow.cs、SettingsFieldState.cs、AppHost.ValidateSettings / ApplySettingsReport、Ui.cs、Localization.cs | arch/settings.md | 通信协议与迁移 |
| 启动级数据目录切换 | DataLocation.cs、Program.Main、SettingsWindow.Save / ReloadDraft、AppHost.ValidateDataDirectory / ValidateDatabase / ApplyDataDirectory | arch/settings.md | 图片算法与通信协议 |
| 状态、保存、加密和事务 | NoteLibrary.cs、NoteRepository.cs、Models.cs、Crypto.cs | arch/storage.md | 皮肤和通信 |
| 导入导出、备份与恢复 | Transfer.cs、AtomicFile.cs、StickiesImporter.cs、BackupBrowserWindow.cs、AppHost.RestoreBackup / RestoreBackupData、NoteLibrary.Reload、NoteRepository.ImportBackup | arch/storage.md | 编辑算法 |
| 提醒、重复与窗口附着 | ScheduleWindow.cs、RepeatSchedule.cs、AppHost.Schedule / UpdateAttachments | arch/shell.md | 图片算法 |
| 托盘、启动与通信 | Program.cs、AppHost.BuildTray / Start / ReceiveLoop、DataLocation.cs、ApiService.cs | arch/shell.md | 图片算法 |
| 构建、验证和打包 | manager.sh、scripts/、tests/DeskPins.Tests/Program.cs | arch/ops.md（§5 / §6） | 功能参考 |
| 完成度与接力 | HANDOFF.md、ROADMAP.md、docs/03-功能对照.md | — | 原版二进制 |

## 1. 文件职责

| 文件或目录 | 职责 |
|---|---|
| AGENTS.md / CLAUDE.md | 工作规则与 Claude 导入入口 |
| ARCHITECTURE.md | 导航总览：架构、作用域路由、文件职责 |
| arch/ | ARCH 分册：notes（便签 / 公共 UI）、manager（管理器 / 堆叠面板）、settings（设置 / 语言 / 快捷键 / 数据目录）、storage（状态 / 存储 / 备份 / 迁移）、shell（托盘 / 启动 / 通信 / 附着 / 提醒）、ops（样式 / 运维 / 全局约束） |
| ROADMAP.md | 完整功能计划与完成度 |
| HANDOFF.md | 当前开发状态与下一步 |
| CHANGELOG.md | 完成的用户可感知变化 |
| README.md | 产品介绍与当前可运行状态 |
| docs/ | 技术栈、使用说明、功能对照、第三方依赖许可 |
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
| src/DeskPins.App/NotePropertiesWindow.cs | 便签属性窗口（桌面便签右键与管理器共用）：元数据、本地序号与完整 GUID 标识符 |
| src/DeskPins.App/MarkdownRenderer.cs | Markdig 解析结果 → WPF FlowDocument：标题 / 列表 / 引用 / 代码块 / 行内强调 / 链接；Markdown 粘贴与导入共用 |
| src/DeskPins.App/MarkdownEditing.cs | 行首标记自动格式化、回车行内提交、纯文本粘贴转换与插入；由 TextEditor 的键盘 / 粘贴事件调用 |
| src/DeskPins.App/MarkdownExporter.cs | FlowDocument → Markdown（标题 / 列表 / 强调 / 链接 / 代码块，允许有损）；`.md` 导出使用 |
| src/DeskPins.App/CollectionsWindow.cs | 联系人完整草稿表单、收藏文件夹 / 标签批量管理；同文件 StackDockWindow 为统一竖排堆叠面板（便签式外观，单击行展开，极简模式隐藏名称） |
| src/DeskPins.App/SettingsWindow.cs | 非模态九页设置、独立草稿、全字段校验定位、样式预览、快捷键分组搜索及即时数据维护 |
| src/DeskPins.App/SettingsFieldState.cs | 跨页面 / 样式保留 Raw / Error 与定位信息；Check 仅把合法输入应用到草稿 |
| src/DeskPins.App/UiTheme.cs | 局部窗口字体、系统高对比度色刷、输入与按钮样式；不注入 Application 全局资源 |
| src/DeskPins.App/Ui.cs | 公共 UI：语言映射、滚动弹框与固定页脚、Owner / 工作区约束、菜单状态、颜色与快捷键解析 |
| src/DeskPins.App/AppHost.cs | 窗口 / 托盘协调、设置分项应用、目录只读预检、保护备份恢复与重载失败写阻断 |
| src/DeskPins.App/BackupBrowserWindow.cs | 只读备份列表、多选计数与覆盖说明；恢复统一调用 AppHost.RestoreBackup |
| src/DeskPins.App/Localization.cs | 内置中英文资源；自定义 JSON 语言文件继续由 Ui.LoadLanguage 读取 |
| src/DeskPins.App/DataLocation.cs | 默认 / 自定义数据目录解析与 `%LOCALAPPDATA%/DeskPins/data-directory.txt` 指针文件 |
| src/DeskPins.App/WindowResize.cs | 无边框便签 / 堆叠面板的边缘拖拽缩放与最小尺寸计算 |
| src/DeskPins.App/TextFormatWindow.cs | 选区 / 全文格式窗口：仅应用显式修改的字体、整数 px 字号、样式、颜色与高亮，保留混合格式 |
| tests/DeskPins.Tests/ | 无第三方框架的 STA 回归；自建临时数据，结束后清理 |
| scripts/、manager.sh、*.bat | 构建、运行、打包、性能测量与关闭运行实例的统一入口 |
| assets/ | 自制图标和 JSON 皮肤示例 |
| Directory.Build.props、DeskPins.sln | 统一 net48、x64、版本和构建配置 |