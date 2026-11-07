# DeskPins · 桌钉

参考 Stickies 的轻量 Windows 桌面便签，支持桌面便签工作流和自定义扩展。

## 用途 / 动机

保留熟悉的 Stickies 交互和功能，掌握源码以便后续定制。重点关注启动速度、常驻占用、可靠保存和恢复。

## 当前状态

已有可构建的开发版：独立便签、托盘、富文本和图片编辑、收藏与加密、提醒、附着、备份、联系人与扩展接口。完整复刻仍在进行；实际视觉验收、原版文件与外部互通验证尚未完成，见 [功能对照](docs/03-功能对照.md) 和 [路线图](ROADMAP.md)。

## 快速开始

在 Windows x64 双击 `build.bat` 构建，随后双击 `run.bat` 运行；`dev.bat` 合并这两步。双击 `verify.bat` 执行构建、回归与协作文档体检，`package.bat` 生成 `dist/` 下的免安装包。

程序默认将数据存于当前用户的 `%LOCALAPPDATA%/DeskPins`。免安装包直接运行 `DeskPins.exe`；指定独立或便携数据目录可使用 `DeskPins.exe --data-dir "C:\path\to\notes"`。不要与原版 Stickies 共享数据库。

## manager.sh 命令一览

在 Git Bash 中执行 `sh manager.sh <命令>`；Windows 双击脚本调用同一命令源。

| 命令 | 说明 |
|---|---|
| `build` | 构建 WPF Release 应用 |
| `test` | 构建并运行核心与 WPF 回归 |
| `verify` | 构建、回归和文档检查 |
| `dev` | 构建并启动开发版 |
| `run` | 启动已构建应用 |
| `package` | 生成 x64 免安装包与 SHA256 |
| `benchmark` | 用独立数据测量启动与内存 |
| `smoke` | 用独立数据验证免安装包启动和单实例导入 |
| `check-docs` | 检查协作文档 |

## 依赖与前置条件

运行需要 Windows 10 / 11 x64 和 .NET Framework 4.8。开发需要 .NET SDK、.NET Framework 4.8 Developer Pack 和 Git for Windows（双击脚本使用其中的 Git Bash）。无需 Node、浏览器组件或第三方运行时。

## 技术栈

C# / WPF / .NET Framework 4.8 / x64；面向 Windows 10 / 11，免安装，优先框架和系统组件。

## 协作文档

从 [AGENTS.md](AGENTS.md) 开始，代码位置见 [ARCHITECTURE.md](ARCHITECTURE.md)，当前进度见 [HANDOFF.md](HANDOFF.md)。文档体系来源于 vibe-coding-starter 2026-10-31.4，使用 docs-only 类型初始化，再按纯 WPF 项目填写约束。

运行 `sh manager.sh check-docs` 检查文档。具体操作和扩展例子见 [使用说明](docs/02-使用说明.md)。
