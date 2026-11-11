# 会话接力 · DeskPins

> 维护规则见 AGENTS §5.1：只留还在路上的内容；任务完结时删掉开工写下的计划痕迹。

## 状态快照

- 最近更新：2026-11-11。
- 整体状态：✅ 无进行中的代码任务；0.2.0 已发布，真实桌面交互 / 视觉待验收。
- 工作分支：main。
- 最后验证状态：0.2.0 Release 构建、208 项回归与离屏布局通过（细节见 CHANGELOG 0.2.0）；当前 Release 构建 0 警告 / 0 错误、210 项回归（含关于窗口）通过；本轮 `sh check-docs.sh --strict` 通过，`build.bat` / `verify.bat` / `ensure-closed.ps1` 已实测，`package.ps1` 全量出包与 `smoke-test.ps1` 通过。文档体系已升级到 vibe-coding-starter 2026-11-11.1；模板五种桌面类型的出包脚本已分别用 net8 / net48、WebView2、Tauri、Electron、Rust 实际构建验证。

## 当前任务

- 无进行中代码任务。总目标与剩余工作见 ROADMAP 与 docs/03-功能对照.md。

## 进行中

- 无。

## 待验收

- P10 / P11：Manage Notes 三栏 / 两栏与 Home 仪表盘、集合增删改属性、Tag / Stack 颜色、文件夹父级与树内拖拽排序、分类计数、月历、查找栏范围 / 标签过滤 / Pin、Closed 短 Id；设置-数据页与自定义数据目录重启生效。
- 快捷键按键框可录入普通按键，全局“管理便签”可启用；设置 Appearance / Content / Alerts / Desktop 的新控件与行为。
- 便签 / 堆叠缩放、联动置顶、文本行距与不透明度等既有修复仍正常。
- 真实桌面托盘（含“关于 DeskPins”窗口）、Ghost、多屏 DPI、外部发送与原版交互一致性继续验收。
- 双击脚本与出包布局：未安装 Git Bash 的机器可双击 build / dev / run / test / package；`dev.bat` 会强制关闭运行中的 DeskPins，`package.bat` 会先询问并支持取消；`dist/dev` 输出 Debug 调试包，`dist/release` 输出可运行 `unpack/`、免安装 zip 与 SHA256SUMS；`verify.bat` 能找到 Git Bash 并跑完文档检查。

## 阻塞 / 待决策

- 无。

## 下一步

1. 在真实桌面验收 P10 / P11 的管理器布局、集合操作、月历与查找栏。
2. 实测普通按键快捷键、全局管理快捷键、启动隐藏 / 关闭确认 / 点击显示全部便签、吸附距离、锁定防移动与卷起独立位置。
3. 继续 docs/03 剩余列与原版兼容项（F05 / F12 / F13 / F16 / F18、TCP / SMTP、系统恢复）。
4. 若要用 test.bat / verify.bat，先退出运行中的 DeskPins，避免 `.runtime/build/dev/DeskPins.exe` 被占用；dev.bat / package.bat 已自动处理运行实例。
5. 决定是否清理 `dist/` 根目录下旧布局的 `DeskPins-0.1.x` / `0.2.0` 历史包；新布局只在 `dist/dev` 与 `dist/release`。

## 本任务验证

- 本轮改文档体系、双击脚本、ARCH 图谱与出包布局；`sh check-docs.sh --strict` 通过，`build.bat` 已实测，`verify.bat` 的构建 / 208 项回归通过，bash 分支单独实测通过。`ensure-closed.ps1` 用临时 `DeskPinsTest.exe` 验证了 `-Force`、输入 `n` 取消、输入 `y` 确认三条路径；`package.bat` 对真实运行实例输入 `n` 后正确退出且未关闭应用。`package.ps1` 全量运行通过：Debug / Release 构建、208 项回归、zip 校验与事务发布；`smoke-test.ps1` 从 `dist/release` 的 zip 解压启动并完成单实例导入。未对真实实例执行强制关闭。

## 本任务决策

- 出包布局按用户参考项目 explorer-tabs / git-extensions 采用 `dist/dev` + `dist/release`；模板 dotnet 类型默认只输出单个 zip，未采用模板默认布局。该约定已写入 ARCH §6。

## 待解问题

- 无。
