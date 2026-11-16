# ARCH 分册 · 状态 / 存储 / 备份 / 迁移（storage）

> 由 ARCHITECTURE.md §0.5 路由进入；小节号与总览一致（§2 入口 / §3 链路 / §4 契约 / §7 坑）。按函数名定位，改结构即同步。

## 2. 任务 → 入口

| 任务 | 入口 |
|---|---|
| 状态转换 / 加密 / 重载 | NoteLibrary.Move / Sleep / Load / Save / SetFolderEncrypted / Reload |
| 备份 / 合并 / 选择恢复 | AppHost.RestoreBackup / RestoreBackupData / Exit、NoteRepository.Backup / ImportBackup、NoteLibrary.Reload、BackupBrowserWindow |
| 原版迁移 / 传输 | StickiesImporter.Read、AppHost.ImportStickies / ImportTransfer、Transfer |

## 3. 重点链路

- Move / Sleep → PreparingChange → AppHost.FlushNote（含管理器）→ 持久化新状态 → 排队 Sync；非桌面窗口移除时不再序列化旧编辑器。
- 加密：Encrypted 表示磁盘行状态；ContentDecrypted 只表示当前 Body 已解密。Save 总是重新保护已加载的加密内容并卸载，锁定清除已解密缓存。
- Atomic 将数据库、内存元数据、目录一起回滚，提交完成后只通知一次。传输接收与 receipt 在同一事务；消费文件在事务成功后原子替换，重试不重复创建便签。
- 设置数据页即时维护始终作用于当前运行目录，不提交设置草稿。RestoreBackup 只读预检、说明覆盖数并确认 → RestoreBackupData 保存全部编辑、创建唯一保护备份 → ImportBackup 事务提交 → Reload 完整读取后交换内存 → 无保存关闭旧便签、同步界面并分项应用设置。成功后 ReloadDraft 深拷贝新设置、丢弃草稿与密码输入；语言加载失败回退中文仍重建控件。
- 恢复提交后的 Reload 失败不回滚已提交数据库：AppHost 标记 storageFailed / Exiting、停计时器并关闭 Repository，阻断旧数据写入；Exit 走无保存退出分支，管理器关闭 / 自动保存通过 StorageFailed 跳过旧内存保存。

## 4. 接口契约

- Note：序列化元数据与非序列化 Body / PlainText 分离；Id 是 GUID 字符串，创建新身份用 Clone。State 只有 Desktop / Closed / Stored / Sleeping / Recurring，附着与堆叠为独立字段。
- Body：文本为 RTF，图片为 PNG。Body=null 表示内容未加载，元数据更新不能改写内容。元数据读取失败不重置数据库。
- DeskPins 文件：`.dpn` 包含元数据、内容和版本；`.dpx` 是带接收身份的包列表；原版 `.sti` / transfer.ini 的完整兼容未验证。
- AppHost.ValidateDatabase 用只读 Sqlite 检查 quick_check、user_version、便签 Id / 元数据和 Catalog；不证明目标目录可写。RestoreBackup 返回用户确认后的恢复结果；RestoreBackupData 不弹确认，返回唯一保护备份路径，合并 / 选择恢复同 Id 会覆盖当前版本，完整恢复替换便签与设置。
- NoteLibrary.Reload 先在局部完整读取 Catalog 与按 Id 建立元数据字典，再更新 Catalog / Notes 并清 SessionPassword；读取或重复 Id 失败保持旧内存。ImportBackup 已提交后 Reload 失败必须关闭 Repository，旧内存不能继续写回。

## 7. 易踩的坑 / 设计取舍

- SQLite 使用系统 DLL，开启 FULL synchronous 和 secure_delete；只对当前库中的记录提供保护，旧备份不随之后的加密设置改变。
- 恢复前必须确认同 Id 覆盖并创建唯一保护备份。Reload 在完整读取前不能清内存；提交后重载失败必须 storageFailed / Exiting、关闭 Repository 阻断旧写，不能把应用失败说成恢复未提交。
- StorageFailed 是恢复提交后重载失败的写阻断状态：Repository 关闭阻断所有数据库写入，ManagerWindow.Closing / SaveEditor 跳过保存；AppHost.Dispose 以 CloseInternal(false) 关闭便签，Exit 仍允许无保存退出。