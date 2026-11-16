# ARCH 分册 · 管理器与堆叠面板（manager）

> 由 ARCHITECTURE.md §0.5 路由进入；小节号与总览一致（§2 入口 / §3 链路 / §4 契约 / §7 坑）。按函数名定位，改结构即同步。

## 2. 任务 → 入口

| 任务 | 入口 |
|---|---|
| 管理布局与预览 | ManagerWindow.Refresh / DisplaySelected / OpenSelected / RestoreSelected / UpdateSelectionSummary / ConstrainPaneWidths / SavePaneWidths / ResizeOutlinePane / PaneSplitter / SelectCategory / ScrollRailToSelection / QueueThinScrollBars / IconButton |
| 管理编辑 / 菜单 / 状态 | ManagerWindow.SaveEditor / QueueEditorSave / ZoomPreview / FlushPending / EditNoteTitle / BeginInlineTitleEdit / ShowNoteProperties / Context / SaveManagerState / RestoreManagerState |
| 管理导航与集合 | ManagerWindow.BuildTree / CreateCollectionItem / BeginInlineCollectionRename / EditCollection / DeleteCollection / MoveCollectionItem / NewNoteIn / DropTo / NodeDrop |
| 管理器日历 | ManagerWindow.BuildCalendar / BuildCalendarView / CalendarDay / FocusCalendarDate / MatchesDate |
| 管理器搜索 | ManagerWindow.BuildFindBar / UpdateSearchSummary / ResetSearch / RefreshSearch / SearchResults / OpenSearchTagFilter、SearchTagWindow |
| 堆叠面板 / 前置 | StackDockWindow 构造 / Refresh / Toggle / BringForward、AppHost.Sync / BringStacksForward、Native.RaiseNoActivate、NoteStack.AutoColor、Catalog.Normalize |

## 3. 重点链路

- 管理器按分类、筛选、集合属性与计数快照决定是否重建导航并恢复有效筛选；DropTo 先保存编辑再事务移动，子文件夹 Drop 在验证前标记 Handled，避免冒泡再次移动。
- 全部堆叠合并在一个 StackDockWindow 竖排面板，外观与便签统一：22px 标题栏式头部（活动 #4A4A4A / 失焦 #6B6B6B，与便签共用 UiTheme.TitleActiveColor / TitleInactiveColor）、直角、DimGray 1px 边、同款阴影；头部左侧 6px 色块显示面板置顶（浅蓝 #9CC3E5，同便签 Pin 语义），无常驻 Pin 按钮，置顶只走右键菜单。每行色块 + 标题 + 数量 + ▶/▼，单击行 Toggle；展开行用组色掺白浅底（TintHex，非法颜色退回白色）+ 半粗标题强化状态，极简模式（Catalog.StackDockMinimal，右键切换）隐藏行标题与头部文字。面板位置 / 宽度 / 置顶 / 极简存 Catalog.StackDockLeft / Top / Width / StackDockTopmost / StackDockMinimal，头部拖动写回位置，横向缩放最小 40px。Sync 按 Stacks.Count 创建或关闭面板；BringStacksForward 用 SetWindowPos(NOACTIVATE) 前置，ShowActivated=false 不抢焦点。便签左侧 indicator 只表示置顶（6px 浅蓝 #9CC3E5）；所属堆叠颜色显示在标题栏右侧关闭按钮底色（模板内层 Border 圆角 4，Ui.TextOn 自动对比文字）。
- 管理便签为 88px 图标 + 小字导轨与动态工作区：30px 图标下是居中 11px 说明，英文长标签保留至少 68px 文字宽度，分类 10 使用本地化“搜索结果”；Tag 保存稳定分类 id。桌面排除附着 / 堆叠，附着分类显示全部 Attachment != null（含当前可见项）。Home 每行保留分类文字，合计按唯一便签 Id 去重。卡片日期固定为“修改于 + ModifiedUtc”。Stored / Stacks / Tags 显示大纲 + 卡片列表 + 内容三栏，其余分类显示卡片列表 + 内容两栏，Home 为仪表盘；预览分栏 MinWidth=245，保证最小窗口仍有内容空间。导轨视觉顺序由 CategoryOrder 决定，不要用 SelectedIndex 当分类 id。列表头部第 1 行是分类标题 + 右对齐计数，第 2 行是图标操作组 + 右对齐排序下拉（宽度按最长文案 80–96px）；预览元信息与操作按钮同一行、按钮靠右。大纲列首次访问使用星号 Fill，之后按分类保存的像素宽度恢复；窗口变宽时未拖拽的首次布局与预览分担剩余空间。三条集合分类的大纲选中行使用 Selected 浅色背景。管理器滚动条统一为 6px，Loaded / Refresh / DisplaySelected 后由 QueueThinScrollBars 合并调度 UiTheme.ApplyThinScrollBars，不再用 LayoutUpdated 全树扫描。树 / 列表计数写入快照，避免预览保存时反复清空选择。ScrollRailToSelection 在筛选、搜索栏展开或导轨尺寸变化后把当前分类滚入可见。
- 第一分隔条使用自定义 Thumb + ResizeOutlinePane，只同时调整大纲和列表且保持两者总宽不变，第二分隔条与预览不动；大纲最小 100px、列表最小 260px。第二分隔条仍用 GridSplitter 调整列表与预览。不要换回两个 GridSplitter 的 PreviousAndNext 组合，否则拖动第一条时第二条会跟着移动。
- 集合大纲由 Catalog 列表顺序驱动，三个集合分类都有 Tag="all" 的“全部”节点，选中后清空筛选且禁用属性 / 删除。拖拽排序移动 Folders / Stacks / Tags 列表项；文件夹节点上 / 下 1/4 排序，中间 1/2 改 ParentId，禁止自身 / 后代。DeskPins.Collection 与 DeskPins.Notes 分流；属性走 CollectionEditor，行内改名走 BeginInlineCollectionRename，删除后清筛选并重建树。
- 日历由 ManagerWindow.Calendar 自绘，月份 / 星期按 Ui.CurrentLanguage 本地化，计数和列表共用 MatchesDate。月历固定为紧凑 6 周并直接放入日历宿主，不嵌套 ScrollViewer；CalendarDay 支持方向键、PageUp / PageDown 和 Home，FocusCalendarDate 恢复焦点。
- 搜索由查找栏驱动：SearchResults 先保存预览，再按 SearchContent 选择全文或标题 / Id 匹配，最后应用任一标签与六范围筛选。输入 250 ms 防抖，范围 / 标签变化立即刷新；UpdateSearchSummary 显示有效条件，ResetSearch 清关键词 / 标签并开启六范围与正文，保留 Pin / 折叠状态。
- 管理器 OpenSelected 只查看 / 编辑历史便签，不迁移状态；恢复按钮单独走 RestoreSelected。UpdateSelectionSummary 和 Context 根据选择数量 / 状态启停并标明批量后果。SaveEditor 成功才清 dirty，失败保留修改和重试反馈；ConstrainPaneWidths 随工作区尺寸约束栏宽，再按分类保存。
- 管理器的集合与预览操作使用 IconButton：固定 32×30、Segoe MDL2 图标、ToolTip 与 AutomationProperties.Name；列表 / 大纲 / 预览各占一行水平排列。空态 CTA 与搜索按钮保留文字；查找栏的关闭 X 已改为重置搜索图标。排序下拉使用 UiTheme.ComboBoxStyle：平面边框、居中文本、自绘下拉项。

## 4. 接口契约

- Note 的管理器展示字段：ManagerTitle 在 Closed 分类显示 `id <ShortId> <标题>`（ShortId 取 Id 前 8 位，完整 Id 在 Identifier / 卡片 ToolTip）；DisplayTags 是 Refresh 时从 Catalog.Tags 解析出的 Tag 列表，仅供 WPF 绑定。DateLabel 固定使用 ModifiedUtc，DateLabelPrefix 由 App 层注入本地化“修改于”，Core 不引用 Ui。WPF 绑定只认属性，不认字段。
- 集合顺序即 Catalog.Folders / Stacks / Tags 的列表顺序；ManagerOrder 等字典仍按稳定分类 id 保存。Folder.ParentId 决定层级，拖动排序时先调整列表顺序再写 ParentId。
- NoteStack：Left / Top / Width / Height / Topmost 仅保留兼容（旧逐堆叠窗口已合并为面板），不再参与运行；面板状态在 Catalog.StackDockLeft / Top / Width / StackDockTopmost / StackDockMinimal，宽度下限 40。颜色默认自动分配：NoteStack.AutoColor 取调色板首个未用色，Catalog.Normalize 把空色或旧默认 #FFFFB4 视为未分配并补色，手动改色不受影响。
- Settings.Search*：SearchDesktop / SearchAttached / SearchStored / SearchSleeping / SearchClosed / SearchRecurring 是搜索范围，SearchContent 控制是否查正文，SearchPinFindBar 让查找栏在其他分类也可见，SearchBarExpanded 记住折叠状态，SearchTagFilter 保存标签 id（任一匹配）。
- ManagerOutlineWidths / ManagerListWidths：按分类索引保存管理器的三栏 / 两栏宽度；Home 不显示列表，Stored / Stacks / Tags 保存大纲和列表宽度，其他分类保存列表宽度。切换分类前 SaveCategoryState 写入当前实际宽度，返回时 ApplyLayout 优先恢复该像素值（无记录才首次 Fill）。
- Settings.ManagerSelectedNoteIds / ManagerFilters：按分类保存选中的便签 Id 列表和 folder / stack / tag 筛选；切换分类、刷新和关闭管理器都保留。ManagerCategory / ManagerFolderFilter / ManagerStackFilter / ManagerTagFilter / ManagerSelectedNoteId / ManagerCalendarDate 继续保存最后活动分类的兼容状态，`RestoreManagerState` 在构造时恢复；失效的 Id 或已删除便签直接忽略。

## 7. 易踩的坑 / 设计取舍

- 堆叠面板 ShowActivated=false，前置用 SetWindowPos(NOACTIVATE)，不能抢走便签焦点；逐堆叠置顶已废弃，置顶是面板级（Catalog.StackDockTopmost），不要再让 NoteStack.Topmost 参与运行行为。便签左侧 indicator 与堆叠分组色是两个语义：indicator 只表示置顶，分组色只放在关闭按钮底色，不要再混用。
- 管理便签的内容区是独立第三栏（Home 除外），不要恢复旧的“列表占满 + 隐藏预览”结构；BuildTree 必须把计数纳入快照，否则预览保存会清空选择。
- 管理便签的工作区是分类相关布局：不要在所有分类常驻 Stored folders / Stacks / Tags 大纲；只有 2 / 8 / 9 显示大纲，Home 显示仪表盘，其余显示列表 + 内容。第一分隔条必须保持大纲+列表总宽不变（自定义 Thumb），第二分隔条独立调整列表与预览；大纲最小 100px、列表最小 260px。分隔条宽度按分类读取 / 保存 ManagerOutlineWidths / ManagerListWidths，保存时不要覆盖其他分类。
- 管理便签的导轨可重排，但分类 id 必须稳定：CategoryOrder 只决定显示顺序，所有筛选、设置字典、布局判断都用 ListBoxItem.Tag；改成 SelectedIndex 会让已保存的列宽 / 排序错位。
- TreeViewItem 拖拽高亮用本地 Background，清理时用 ClearValue 而不是赋 Transparent，否则会压掉默认选中样式。文件夹 Drop 的 1/4、1/2、1/4 分区分别对应排序前、改父目录、排序后；集合排序不要重建 Catalog 字典。
- WPF 绑定不读公共字段。管理器卡片新增展示字段时用属性（如 ManagerTitle / Identifier / DisplayTags、Tag.DisplayName / ColorHex），字段只用于序列化或内部计算。
- 日历与搜索是同一套筛选的两处呈现：日格计数与日历列表必须共用 MatchesDate；搜索的范围 / 标签 / 正文开关必须同时作用于 SearchResults，不要在 Refresh 的 switch 里另外拼一套条件。月历不要套内部 ScrollViewer：用 26px 日格和 32px 月份栏固定 6 周，最小窗口仍要让便签列表 ≥120px。
- 管理器操作按钮统一用 IconButton（32×30、图标 + ToolTip + 自动化名称），不要恢复尺寸不一的文字按钮组；空态 CTA 与搜索按钮保留文字，查找栏重置/展开使用图标。排序下拉必须使用 UiTheme.ComboBoxStyle（平面边框、居中文本、自绘 Popup 和菜单项），否则会退回系统灰色下拉样式；新增操作时同时检查列表、大纲、预览三行是否各自单行对齐。
- 分类宽度 / 选中 / 筛选在切换前由 SaveCategoryState 写入 Settings，切换后再 RestoreCategoryState / RefreshCore 恢复；只靠关闭时 SaveManagerState 会导致切回分类重置。首次访问无保存宽度才 Fill，之后恢复像素值。
- 管理器的集合大纲使用自定义 TreeViewItem 模板：默认模板的系统选中色会覆盖样式 Background，必须由模板的 Bd 触发器绘制 Selected 浅色背景；展开按钮绑定 IsExpanded，叶子节点隐藏。显式创建的 TreeViewItem 也要赋 Style（只设 ItemContainerStyle 不够），否则子目录会用默认模板导致计数列不对齐；缩进通过每层 Padding 实现，ItemsPresenter 不再加左边距，计数才能统一贴右。不要只改 ItemContainerStyle 的 Background 就宣称选中色已改。