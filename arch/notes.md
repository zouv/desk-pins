# ARCH 分册 · 便签与公共 UI（notes）

> 由 ARCHITECTURE.md §0.5 路由进入；小节号与总览一致（§2 入口 / §3 链路 / §4 契约 / §7 坑）。按函数名定位，改结构即同步。

## 2. 任务 → 入口

| 任务 | 入口 |
|---|---|
| 编辑 / 格式 / 窗口 | NoteWindow.Run / HandleKeyDown / Zoom / TextMenu / Flush、TextEditor.Format / FontDialog / Scale / Read / Write、TextFormatWindow.ApplyChanges |
| 图片算法 | ImageEditor.BeginStroke / ContinueStroke / CommitStroke / CancelEdit / Transform / Crop / ResizeImage、RasterCanvas |
| 屏幕截取 | AppHost.Capture、CaptureWindow 构造 / Result |
| 关于窗口 | AboutWindow 构造 / VersionText、AppHost.BuildTray 的“关于 DeskPins”菜单项 |
| 公共弹框 / 按钮 / 菜单 / 滚动条 | UiTheme.Apply / ApplyThinScrollBars / QueueThinScrollBars / ButtonStyle / ComboBoxStyle、Ui.DialogContent / FitDialog / ActiveWindow / PrimaryButton / DangerButton / Item；NoteWindow.Menu / TextMenu / ImageMenu |
| 窗口边缘缩放 | WindowResize.Attach / Calculate、NoteWindow 构造、StackDockWindow 构造 |
| 拖拽吸附 | AppHost.SnapAfterDrag、NoteWindow.DragTitle / MoveSelected |

## 3. 重点链路

- 文本编辑 → QueueSave（600 ms 合并）→ Flush → RTF / plain → NoteLibrary.Save → SQLite。失去焦点与退出也保存；保存失败保持 dirty，阻止依赖保存成功的状态迁移。
- 图片鼠标事件调用 BeginStroke / ContinueStroke / CommitStroke；提交才建立撤销并通知保存，CancelEdit 恢复笔画前图像。选区操作只更新显示，Escape 由 NoteWindow 转发。
- 无边框窗口缩放由 WindowResize 监听 Window 的 Preview 鼠标事件，在四边 6px 内手动改 Left / Top / Width / Height；NoteWindow 完成时写回 Note，StackDockWindow 只允许横向缩放（verticalEnabled=false），完成时写回 Catalog.StackDockWidth，高度按行数自动适配。缩放期间 NoteWindow 抑制多选拖动与组移动，最小尺寸由调用方传入。
- 便签标题拖动期间 MoveSelected 只同步组内窗口；DragMove 返回后才由 SnapAfterDrag 按屏幕 / 其他便签边缘吸附，避免连续吸附把小幅拖动拉回原位。SnapAfterDrag 移动全部选中窗口；按住 Alt 反向切换吸附。
- TextEditor 在 FlowDocument.Resources 注入 Paragraph Margin=0 隐式样式，并在 Read / TextChanged 归一化段落边距，避免 Paragraph 自动边距形成多余空行。
- NoteWindow.Zoom 与 ManagerWindow.ZoomPreview 分别统一桌面便签 / 管理器预览的 Ctrl+滚轮缩放：有选区只缩放选区，无选区缩放全文；TextEditor.Scale 按连续文本段读取原字号，ScaledSize 每段固定增减 1px、分别限制范围，保留混合字号的相对大小，逆序应用后恢复选区。Read 在 RTF 加载后把各段字号归一化为整数 px。右上角 Tip 显示 1200ms，统一字号显示整数、混合字号显示整数 px 范围，不抢焦点或鼠标；管理器缩放走 QueueEditorSave 自动保存，图片仍使用原比例范围。
- 新建便签统一经过 NoteLibrary.Create：先用 Styles[0] 应用颜色等其他样式，再以 Settings.DefaultFontName / DefaultFontSize 覆盖字体和字号，因此桌面、API 与管理器入口一致，已有便签不受影响。
- TextEditor.FontDialog 打开 TextFormatWindow；无选区目标为全文，有选区只处理选区。对话框以 dirty 标志保证只有显式修改的属性才应用，混合值和第三态复选框保持不变；下划线 / 删除线共用 Inline.TextDecorationsProperty，ApplyDecoration 按文本段保留另一种装饰。NoteWindow 在右键位于现有选区内时拦截 RichTextBox 的右键按下默认行为，避免选区折叠后再生成格式菜单。
- 便签窗口透明度恒为 Note.Opacity（右键菜单 / 样式 / API 显式设置），不随焦点变化；聚焦只靠标题栏底色区分（活动 #476F94 / 非活动 #6B6B6B，皮肤窗口沿用皮肤标题色）。Secret 便签失焦仍隐藏内容，属隐私行为不受此影响。
- 公共弹框：Ui.DialogContent 提供滚动正文与固定按钮页脚，FitDialog 限工作区并为非任务栏窗口补活动 Owner；PrimaryButton / DangerButton 区分主要 / 危险动作，确认默认 No。输入框在 Loaded 后 Dispatcher 聚焦 / 全选；集合、联系人、提醒、附着验证失败定位字段，危险动作独立并明确后果。ScheduleWindow 仅对已有休眠 / 重复或闹钟显示“现有提醒”清除区。
- 菜单按需生成：Ui.Item 接收 check / enabled；NoteWindow.Menu 首层保留常用操作，将整理、显示行为、导出传输分别收进二级菜单并保留快捷键，仍标明关闭可恢复、永久删除及传输后关闭；Alt+F4 只关闭当前便签（进入 Closed），不退出应用。TextMenu 按选区动态显示所选文字 / 全文格式，段落、插入和高亮分组，按锁定、选择、剪贴板状态启停；ImageMenu 按锁定、选择、剪贴板和 CanUndo / CanRedo 启停。BuildTray 分新建、显隐 / 管理、工具与退出组；数据维护只在设置页。

## 4. 接口契约

- Note 的卷起独立位置使用 ExpandedLeft / ExpandedTop / RolledLeft / RolledTop 可空字段；只有 Settings.RolledDifferentPosition 打开时才读写。
- TextEditor.PlainText / SelectedPlainText 从当前文档读取；在保存合并延迟内，复制、自动宽度和替换判定不能依赖 Note.PlainText 缓存。ShortcutFor 同时供菜单和键盘分发读取。
- UiTheme.Apply 设置目标窗口 Resources，并在 Loaded 后统一实际 ScrollBar 尺寸；动态内容用 QueueThinScrollBars 合并调度。Ui.DialogContent 的按钮不随正文滚动。UiTheme.ComboBoxStyle 提供平面边框、居中文本和自绘 Popup/ComboBoxItem，供管理器排序使用。Ui.Item 的 check=null 表示非勾选菜单，enabled 决定可用性；ImageEditor.CanUndo / CanRedo 同时检查 Locked 与各自历史栈。

## 7. 易踩的坑 / 设计取舍

- 便签 / 堆叠使用 WindowStyle=None + ResizeMode.NoResize，没有原生非客户区缩放；不要再通过 WM_NCLBUTTONDOWN 模拟，改用 WindowResize 的应用内命中与拖拽。
- 自动吸附只在 DragMove 返回后执行；不要在 MouseMove 中实时吸附加回窗口，否则小幅拖动会看起来“拖不动”。
- Paragraph 的 Margin 可能是 Auto 或由 RTF 显式带入；TextEditor 必须用隐式样式和归一化保持 0，否则会出现多余空行。
- RichTextBox 右键按下可能折叠现有选区；若点击位置在选区内，NoteWindow 必须先保留选区再打开 TextMenu，否则“设置所选文字格式”会错误退化为全文格式。TextFormatWindow 不要直接覆盖整个 TextDecorationsProperty，否则设置下划线会清除删除线，反之亦然。
- WPF 字号在此功能中直接按 px 使用，不再做 pt × 4/3 换算；RTF 的半磅量化会在读回时产生 .333 / .667 等小数字号，只在 Tip 格式化不够，Read 后必须按文本段取整。
- UiTheme 只覆盖工具窗口，便签正文保留用户色彩，堆叠面板用中性底 + 分组色块；不要向 Application 注入隐式输入样式。FitDialog 对非任务栏窗口补 Owner，设置窗口必须保持 ShowInTaskbar=true / 无 Owner；按钮 / 菜单的启停与明确文案不能代替处理函数的安全检查。
- 自定义便签背景的前景色由 Ui.TextOn 按 WCAG 相对亮度分别比较黑 / 白对比度，不能用简单亮度阈值；标题栏固定配色也要分别验证活动 / 失焦背景上的文字对比度。
- ScrollViewer 默认模板会给 ScrollBar 写入系统 17px 宽度，单靠隐式 Style 的 Width setter 不生效；UiTheme.ApplyThinScrollBars 把实际 ScrollBar 的 Width / Height 设为 UiTheme.ThinScrollBarSize（当前 6px），由 UiTheme.Apply 在窗口 Loaded 接入，NoteWindow 创建编辑器和 SettingsWindow 切页后补充调度，ManagerWindow.QueueThinScrollBars 在 Loaded / Refresh / DisplaySelected 后合并调度。不要把“细滚动条”只做成 UiTheme 样式而不验证可见 ScrollBar 的 ActualWidth / ActualHeight。
- 便签窗口必须同时设置 `WindowStyle=None` 与 `AllowsTransparency=true`，`Window.Opacity` 才会通过 WPF 分层窗口生效；不要再通过 `SetLayeredWindowAttributes` 手动改非透明窗口的 alpha（WPF 会重置 `WS_EX_LAYERED`）。`Native.SetGhost` 只负责 `WS_EX_TRANSPARENT`。
- 便签透明度不参与焦点状态：OpaqueEditing / RestoreOpacityAfterEditing（编辑强制不透明 + 失焦淡出）已按用户要求移除，聚焦只靠标题栏高亮区分，不要再引入随焦点变化的透明度或动画。