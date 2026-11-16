# ARCH 分册 · 托盘 / 启动 / 通信 / 附着 / 提醒（shell）

> 由 ARCHITECTURE.md §0.5 路由进入；小节号与总览一致（§2 入口 / §3 链路 / §4 契约 / §7 坑）。按函数名定位，改结构即同步。

## 2. 任务 → 入口

| 任务 | 入口 |
|---|---|
| 启动 / 单实例 / 命令行 | Program.Main、ApiService.Identity / Client |
| 新建 / 显隐 / 托盘 | AppHost.Start / New / Sync / ToggleAll / ShowAllNotes / BuildTray / BuildTrayMenu / SetHidden / ToggleManage |
| 定时 / 休眠恢复 | NoteLibrary.Tick / NextDue、RepeatSchedule.Next、AppHost.Schedule / Tick |
| 附着显隐 | AppHost.UpdateAttachments / EnsureAttachmentHook、AttachWindow |
| 插件 / 通信 / 设置 | ApiService.Execute / AddMenus、AppHost.ReceiveLoop / Send / ApplySettings |

## 3. 重点链路

- 计时器按最近到期时间唤醒，不做秒级常驻轮询；系统恢复事件重新计算错过的提醒。附着使用 WinEvent hook 与 180 ms 合并。
- AppHost.ToggleAll / ShowAllNotes 共用 visibilityChanging 重入保护。隐藏关闭活动便签时，下一张便签的 Activated 不会借 ClickMakesVisible 撤销本次隐藏。

## 4. 接口契约

- 网络：CArchive 长度帧 + UTF-8 `#sticky` 属性；最大 16 MiB。包含内容、颜色、位置、回复端口；原版 Unicode 与 FORM 等高级字段仍待核验。
- IPC：按数据目录和当前用户 SID 派生的 Named Pipe，仅当前用户访问；支持部分 Stickies 风格命令和菜单事件，不宣称完整原版 API 兼容。

## 7. 易踩的坑 / 设计取舍

- 不把历史便签全部创建为窗口，仅为需要显示的便签创建界面。
- 不在 UI 线程播放完整唤醒声音；网络 I/O 异步，保存与短事务在 UI 线程串行执行。
- 托盘不再承载导入 / 备份 / 恢复 / 救回；这些操作只在设置-数据页，避免两套入口行为漂移。BuildTrayMenu 独立构造、不显示图标，供 testMode 回归；菜单只能向 ShortcutFor 查询 DefaultShortcuts 已登记动作，截屏无快捷键不查询，新建提示读取 GlobalNewKey。
- 批量显隐必须覆盖整个 Sync / 窗口关闭过程，而不是只保护显示方向：活动便签关闭会同步激活下一张，ClickMakesVisible 的 Activated → ShowAllNotes 会抵消首次隐藏；ToggleAll / ShowAllNotes 共用 visibilityChanging，不能在 Activated 里另加延时绕过。