# 会话接力 · DeskPins

## 状态快照

- 日期：2026-11-08。
- 状态：P0 文档初始化完成；P1–P6 已有开发版，独立免安装包进程检查通过；完整复刻仍进行中。
- 分支：main；0.1.0 初始版本仅在本地提交。

## 当前任务

- 目标：用 C# / WPF / net48 / x64 独立实现 Stickies 桌面便签完整工作流。
- 范围：src/DeskPins.Core、src/DeskPins.App、tests、scripts，按 docs/03-功能对照.md 验证。
- 完成定义：功能可执行、可靠持久化、对应自动回归与 WPF 实测、打包可运行；外部互通与原版格式差异明确记录。

## 进行中 / 确切断点

- 已建立 net48 / x64 的 Core、WPF App 和无第三方测试框架的回归工程。
- NoteLibrary.Load / Save 使用持久化 Encrypted + 内存 ContentDecrypted，Prepare → AppHost.FlushNote 在迁移前保存编辑；Atomic 回滚数据库与内存。已通过加密编辑、重启恢复、事务和传输重试回归。
- AppHost.ImportTransfer / NoteRepository.ReceiveOnce 已实现传输消费的重试保护；Program.Main 可将第二实例导入交给已有实例；ApiService 的显隐命令幂等。
- 原版格式与 UI 细节仍未完成，不能把本版本称为完整复刻。未完成点的单一清单为 docs/03-功能对照.md；尤其 Skin 为自定义 JSON、API 为子集、StickiesImporter 仅跳过加密行，真实数据库字段映射仍待核验。
- 项目文档已同步到实际代码；模板 check-docs.sh 零警告通过。

## 最后验证

- `sh manager.sh package`：Release 构建 0 警告 / 0 错误，96 项核心 / STA WPF 回归通过。包含 TCP 回环、无关数据库保护与恢复后重新锁定。
- `sh manager.sh smoke`：独立解压后启动、IPC、第二实例文件导入、内容读取与退出通过；退出应答竞态已修复。2 张测试便签私有内存 45.89 MiB、工作集 81.02 MiB、5 秒空闲新增 CPU 时间 0 ms。
- `sh manager.sh benchmark`：1 / 10 张便签各 3 次，结果与条件见 docs/01。基准并非确认首帧。
- WPF 离屏像素检查跳过。computer-use 找到管理窗口，但捕获返回 `IGraphicsCaptureItemInterop.CreateForMonitor` / 0x80070057，控件树为 null，实际视觉未验收；没有继续盲目点击。测试实例已退出。
- 试用包：dist/DeskPins-0.1.0-x64.zip 与 SHA256SUMS.txt。运行 / 参考 / 测量数据均在忽略入库目录；未推送远程。

## 下一步

1. 在可捕获桌面的环境实测 NoteWindow / ManagerWindow；按 docs/03 的剩余列验证托盘、绘图、Ghost、声音、附着与多屏 DPI，不再重做已通过的初始化和核心测试。
2. 补齐 F05 / F12 / F13 / F16 / F18：原版 SSK、STI / transfer.ini、真实 v10 样本与加密迁移、API 指令和语言 / 策略兼容。参考仅在 .runtime/reference，不拷贝原版资源。
3. 对照原版快捷键与管理细节补齐交互，完成外部 TCP / SMTP 与系统恢复验收，再优化真实冷启动和大量历史便签场景。

## 待验收

- 试用版独立窗口、编辑 / 关闭恢复和管理界面，请检查是否符合熟悉的 Stickies 操作；目前没有完成实际视觉验收。
