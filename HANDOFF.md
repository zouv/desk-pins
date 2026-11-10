# 会话接力 · DeskPins

## 状态快照

- 日期：2026-11-10。
- 状态：P8 设置体验迭代代码完成，Release 构建与 168 项自动回归、离屏设置 / 快捷键布局通过；真实桌面拖拽和视觉仍待验收。无进行中代码任务。
- 分支：main；版本：0.1.1 基线，本轮改动尚未发版（见 CHANGELOG `[未发布]`）。

## 当前任务

- 无进行中代码任务。总目标与剩余工作见 ROADMAP 与 docs/03-功能对照.md。

## 进行中 / 确切断点

- 无。本轮新增 SettingsWindow 分类导航 / 语言 / 快捷键页、Localization.cs、WindowResize.cs；NoteWindow 与 StackWindow 已接入手动边缘缩放，NoteStack 与 Settings 模型已持久化新字段。

## 最后验证

- `dotnet build DeskPins.sln -c Release`：0 警告 / 0 错误。
- `tests/DeskPins.Tests.exe --ui --audit-dir .runtime/ui-audit-new`：167 项通过（1 项像素渲染检查因环境无图形捕获跳过）；像素可用时已检查 settings-0…6、settings-hotkeys-app、settings-en、settings-hotkeys-en、stack 布局图；最小宽度按列宽计算核对。
- 独立数据目录启动 + `--api quit`：当前 Release 构建启动、初始化托盘与退出通过；未做真实视觉检查。
- `sh manager.sh verify` 未完整执行：本机已有开发实例占用 `.runtime/build/dev/DeskPins.exe`，构建脚本的最后复制步骤失败；构建与回归已直接执行。真实桌面拖拽 / 视觉验收未完成。
- 0.1.0 / 0.1.1 原有验证仍有效；包与 benchmark 未在本轮重跑。

## 下一步

1. 在可捕获桌面的环境启动新构建，实测设置左导航、中英文切换、快捷键启停 / 捕获，以及便签和堆叠四边 / 四角缩放；确认重启后尺寸保持。
2. 继续 docs/03 剩余列：托盘、绘图、Ghost、声音、附着、多屏 DPI、原版快捷键与管理细节。
3. 补齐 F05 / F12 / F13 / F16 / F18 原版兼容项；完成外部 TCP / SMTP 与系统恢复验收。

## 待验收

- 请在实际桌面验收设置左导航 + 右侧配置、中文 / English 下拉切换、快捷键全局 / 应用内双 Tab（开关、Shift / Win / Ctrl / Alt、按键捕获），以及便签 / 堆叠拖拽边框缩放和重启后尺寸保持。
- 请继续验收新建后直接输入、管理器分隔条 / 拖放、提醒表单、图片 Escape 取消；托盘、Ghost、多屏 DPI 和原版交互一致性仍未真实桌面验收。
