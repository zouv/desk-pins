# ARCH 分册 · 样式地图 / 运维 / 全局约束（ops）

> 由 ARCHITECTURE.md §0.5 路由进入；小节号与总览一致（§5 样式 / §6 运维 / §7 坑）。做构建 / 打包 / 文档检查任务只读本分册与对应脚本，不用打开业务代码。

## 5. 样式 / 资源分段地图

| 区段 | 锚点 / 路径 | 内容 |
|---|---|---|
| 程序图标 | `assets/icons/DeskPins.ico`、`DeskPins.App.csproj` 的 `ApplicationIcon` | 程序与窗口图标 |
| JSON 皮肤 | `assets/skins/*.json`、`AppHost.Skin` / `ValidateSkin` | 标题栏颜色、按钮动作；示例见 `classic-plus.json` |
| 颜色 / 阴影 | `Ui.Brush` / `Ui.NoteShadow` | 便签与管理器共用的画刷、阴影生成 |
| 语言资源 | `Localization.cs`、`Ui.Text` | 内置中英文映射；自定义 JSON 由 `Ui.LoadLanguage` 读取 |

## 6. 运维 / 脚本（不碰业务即可独立维护）

> 具体操作见 docs/02。

- **命令唯一来源**：`manager.sh` 分发到 `scripts/*.ps1`；`check-docs` 直接调用 `check-docs.sh` 并透传参数（如 `--strict`）。
- **双击脚本**：`build.bat` / `dev.bat` / `run.bat` / `test.bat` / `package.bat` 直接调用对应 `scripts/*.ps1`，不依赖 Git Bash；`dev.bat` 构建前用 `scripts/ensure-closed.ps1 -Force` 关闭运行中的 DeskPins，`package.bat` 用同一脚本先询问。`verify.bat` 先构建 + 回归，再用 Git Bash 跑 `check-docs.sh`。失败时暂停，`DESKPINS_NO_PAUSE=1` 可跳过。
- **脚本分工**：`ensure-closed.ps1` 检测 / 关闭 DeskPins（`-Force` 不询问，否则 `Read-Host` 确认）；`build.ps1` Release 构建（`-Test` 追加回归）并复制到 `.runtime/build/dev/`；`run.ps1` 启动（`-Development` 先构建）；`package.ps1` 构建 Debug / Release、跑回归，按 `dist/dev` 与 `dist/release` 出包；`PackageTransaction.ps1` 负责 zip 校验与发布回滚；`benchmark.ps1` 独立数据测量；`smoke-test.ps1` 从 `dist/release` 的 zip 解压并以隔离数据启动、验证单实例导入。
- **出包布局**：模板 `dotnet-native-desktop` 的默认 `package.ps1` 只把单个 zip 写到 `dist/`；本项目按 explorer-tabs / git-extensions 的约定改为 `dist/dev/DeskPins_<版本>_x64-unpack-debug.zip`（Debug + PDB + 构建日志 + `build-info.txt`）与 `dist/release/unpack/` + `dist/release/DeskPins_<版本>_x64-unpack.zip` + `SHA256SUMS.txt`（另含 `unpack-manifest.json`）。发布前校验 zip；失败时保留旧包并回滚，不删除历史 `dist/` 内容。
- **版本号**：只在 `Directory.Build.props`；打包脚本从该文件读取。
- **已知限制**：开发实例运行时 `.runtime/build/dev/DeskPins.exe` 被占用，`build.ps1` / `test.bat` / `verify.bat` 的复制步骤会失败；`dev.bat` / `package.bat` 会先处理运行实例。

## 7. 易踩的坑 / 设计取舍（全局与流程）

- 纯 WPF 是用户明确选择；不引入 WebView / Electron / React 界面。
- 功能参考与实现证据分别记录；功能名称存在不代表行为已经等价。
- 性能基准是程序入口到 Dispatcher 空闲，并非已经确认的首帧；截图失效时禁止把离屏对象测试当成视觉验收。
- 离屏布局用显式 Measure / Arrange 和 RenderTargetBitmap.Render(root)；VisualBrush 的自动布局可能重新测量根节点，不能用缩放后的图像验证实际控件尺寸。测试图只用示例内容，清除路径字段后生成。
- UI 测试使用 `new AppHost(..., testMode: true)`；`Focus` / `BringForward` / `ToggleAll` / `ShowAllNotes` / `BringStacksForward` 等会 `Show()` 窗口的路径必须尊重 `testMode`，否则 `package.bat` 跑 UI 回归时会闪一下便签窗口。