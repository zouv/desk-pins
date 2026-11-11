# 会话接力 · DeskPins

## 状态快照

- 日期：2026-11-11。
- 状态：P10 Manage Notes / 设置数据页 / 自定义数据目录 / 新设置页代码完成；Release 构建、195 项自动回归、设置 9 页与分类三栏 / 两栏管理器、Home 仪表盘的离屏布局检查通过。真实桌面视觉、拖拽与数据目录迁移仍待验收。无进行中代码任务。
- 分支：main；版本：0.1.1 基线，本轮改动尚未发版（见 CHANGELOG `[未发布]`，已超过 15 条，建议评估 0.2.0）。

## 当前任务

- 无进行中代码任务。总目标与剩余工作见 ROADMAP 与 docs/03-功能对照.md。

## 进行中 / 确切断点

- 无。本轮改动覆盖 ManagerWindow（图标栏 / Stored-Stacks-Tags 三栏 / 其他分类两栏 / Home 仪表盘 / 分隔条宽度保存）、SettingsWindow（9 类、数据页、Appearance / Content / Alerts / Desktop 重排）、DataLocation、快捷键解析与全局管理键、Note / Settings 模型扩展及回归。

## 最后验证

- `dotnet build DeskPins.sln -c Release`：0 警告 / 0 错误。
- `tests/DeskPins.Tests.exe --ui --audit-dir .runtime/ui-audit-new`：195 项通过；已检查 manager-default / manager-minimum、settings-0…8、note-text 等离屏布局图。
- 新增回归覆盖：普通按键快捷键与录入、全局快捷键格式校验、数据目录指针解析、设置迁移、JPEG 图片保存、管理器集合计数导航、数据页入口。
- `sh manager.sh verify` 在构建 / 回归通过后，最后复制 `.runtime/build/dev/DeskPins.exe` 时因该文件正被运行中的开发实例占用而失败；随后直接执行 `dotnet build`、195 项回归和 `sh check-docs.sh` 均通过。
- 包与 benchmark 未在本轮重跑。

## 下一步

1. 在真实桌面验收分类三栏 / 两栏 Manage Notes、Home / Closed 布局、分隔条宽度记忆、设置 9 页、托盘精简后的 Data 操作、自定义数据目录重启和数据迁移。
2. 实测普通按键快捷键、全局管理快捷键、启动隐藏 / 关闭确认 / 点击显示全部便签、吸附距离、锁定防移动与卷起独立位置。
3. 继续 docs/03 剩余列与原版兼容项（F05 / F12 / F13 / F16 / F18、TCP / SMTP、系统恢复）。

## 待验收

- Manage Notes 的左侧图标栏、文件夹 / 堆叠 / 标签计数、便签卡片列表和“⋯ → 预览”面板。
- 托盘已移除导入 / 备份 / 恢复 / 救回入口；设置-数据页可完成这些操作，自定义数据目录在重启后生效。
- 快捷键按键框可录入普通按键，全局“管理便签”可启用；设置 Appearance / Content / Alerts / Desktop 的新控件与行为。
- 便签 / 堆叠缩放、联动置顶、文本行距与不透明度等既有修复仍正常。
- 真实桌面托盘、Ghost、多屏 DPI、外部发送与原版交互一致性继续验收。
