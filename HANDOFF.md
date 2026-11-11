# 会话接力 · DeskPins

## 状态快照

- 日期：2026-11-11。
- 状态：0.2.0 管理体验版已打包并发布 GitHub Release；Release 构建、208 项自动回归、manager 全分类离屏布局与包内进程 smoke 通过。真实桌面交互 / 视觉仍待验收。无进行中代码任务。
- 分支：main；版本：0.2.0（2026-11-11），CHANGELOG `[未发布]` 为空。

## 当前任务

- 无进行中代码任务。总目标与剩余工作见 ROADMAP 与 docs/03-功能对照.md。

## 进行中 / 确切断点

- 无。P11 覆盖 ManagerWindow（主布局 / Collections / Calendar / Search 四个 partial）、CollectionEditor / SearchTagWindow、Models 展示字段与 Settings 搜索选项、Localization 与回归。

## 最后验证

- `dotnet build DeskPins.sln -c Release`：0 警告 / 0 错误。
- `tests/DeskPins.Tests/bin/Release/net48/DeskPins.Tests.exe --ui --audit-dir .runtime/ui-audit-p11`：208 项通过；已检查 manager-category-0…10（含月历与搜索查找栏）、manager-default / manager-minimum、settings 0…8、note-text 等离屏布局图。
- 新增回归覆盖：导轨 Tags / Stored 重排、Closed 短 Id、文件夹 / 堆叠 / 标签拖拽排序、文件夹改父目录与循环拒绝、搜索范围 / 标签过滤、便签 Id 前缀搜索、搜索标签对话框、月历日格数量。
- `sh manager.sh verify` 的构建 / 208 项回归通过，但复制 `.runtime/build/dev/DeskPins.exe` 时因该文件正被运行中的开发实例占用而失败；随后单独执行 `sh check-docs.sh` 通过，0 项警告。
- 免安装包从本轮 Release 构建输出生成（同一布局）：`dist/DeskPins-0.2.0-x64.zip`，SHA256 `BC7D26AAAB712A03581EA4B2191C782E912B880ED42C2463B0E0131149D670D4`；`sh manager.sh smoke` 通过（单实例导入、隔离数据、进程退出）。
- 包与 benchmark 未在本轮重跑 benchmark；包已随 GitHub Release 上传。

## 下一步

1. 在真实桌面验收 P11：Stored / Stacks / Tags 的新建 / 删除 / 改名 / 属性、树内拖拽排序与改父目录、分类计数、月历点击与翻月、底部查找栏与标签过滤对话框、Closed 短 Id。
2. 继续验收 P10：分类三栏 / 两栏布局、Home / Closed、分隔条宽度记忆、设置 9 页、托盘精简后的 Data 操作、自定义数据目录重启与迁移。
3. 实测普通按键快捷键、全局管理快捷键、启动隐藏 / 关闭确认 / 点击显示全部便签、吸附距离、锁定防移动与卷起独立位置。
4. 继续 docs/03 剩余列与原版兼容项（F05 / F12 / F13 / F16 / F18、TCP / SMTP、系统恢复）。

## 待验收

- P11：集合增删改属性、Tag / Stack 颜色、文件夹父级、树内拖拽排序 / 改父目录、分类计数、月历、查找栏范围 / 标签过滤 / Pin、Closed 短 Id。
- P10：Manage Notes 左侧图标栏、三栏 / 两栏布局、Home 仪表盘、分隔条宽度记忆；设置-数据页与自定义数据目录重启生效。
- 快捷键按键框可录入普通按键，全局“管理便签”可启用；设置 Appearance / Content / Alerts / Desktop 的新控件与行为。
- 便签 / 堆叠缩放、联动置顶、文本行距与不透明度等既有修复仍正常。
- 真实桌面托盘、Ghost、多屏 DPI、外部发送与原版交互一致性继续验收。
