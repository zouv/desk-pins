# 会话接力 · DeskPins

## 状态快照

- 日期：2026-11-08。
- 状态：现有界面代码与离屏布局复查、缺陷修复已完成；本轮无进行中代码任务。P1–P6 已有开发版，完整复刻与真实桌面验收仍进行中。
- 分支：main；版本：0.1.1，界面与交互修复归档为本地提交。

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

- `sh manager.sh verify`：Release 构建 0 警告 / 0 错误，150 项核心 / STA WPF 回归、文档检查通过。包括 11 个管理分类的最小布局、提醒提交 / 取消、图片笔画与样式、查找、快捷键、富文本嵌入图片及既有持久化 / 通信回归。
- `sh manager.sh smoke`：0.1.1 包独立解压后启动、IPC、第二实例文件导入、内容读取与退出通过。2 张测试便签私有内存 49.67 MiB、工作集 90.93 MiB，5 秒新增 CPU 时间 0 ms；该样本不代表所有焦点 / 桌面环境。
- 0.1.0 独立数据的焦点场景曾测得 1 张便签 5 秒 CPU 546.88 ms、私有内存 52.64 MiB；全部隐藏后 5 秒 CPU 0 ms。前台渲染 / 插入点开销与后台分开记录，仍待实际桌面环境验证。
- `sh manager.sh benchmark`：1 / 10 张便签各 3 次，结果与条件见 docs/01。基准并非确认首帧。
- WPF 离屏像素可渲染，已检查便签 / 卷起 / 图片 / 管理 / 设置 / 提醒 / 附着 / 集合 / 堆叠 / 备份布局图（忽略入库的 .runtime/ui-audit/layouts）。真实捕获重试返回 FrameArrived / window capture timeout；访问树可读，输入返回 coordinate input geometry is unavailable，实际视觉与真实点击未验收。独立测试实例已退出。
- 试用包：dist/DeskPins-0.1.1-x64.zip 与 SHA256SUMS.txt。运行 / 参考 / 测量数据均在忽略入库目录；未推送远程。

## 下一步

1. 在可捕获桌面的环境实测 NoteWindow / ManagerWindow；按 docs/03 的剩余列验证托盘、绘图、Ghost、声音、附着与多屏 DPI，不再重做已通过的初始化和核心测试。
2. 补齐 F05 / F12 / F13 / F16 / F18：原版 SSK、STI / transfer.ini、真实 v10 样本与加密迁移、API 指令和语言 / 策略兼容。参考仅在 .runtime/reference，不拷贝原版资源。
3. 对照原版快捷键与管理细节补齐交互，完成外部 TCP / SMTP 与系统恢复验收，再优化真实冷启动和大量历史便签场景。

## 待验收

- 请在实际桌面验收新建后直接输入、管理器分隔条 / 拖放、提醒表单、图片 Escape 取消；同时检查托盘、Ghost、多屏 DPI 和原版交互一致性。此次离屏布局 / 自动回归通过，真实视觉尚未完成。
