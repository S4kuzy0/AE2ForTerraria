# 设计与数据说明

适用源码：0.26.5；核对日期：2026-10-16（北京时间）。源码根目录为 `AE2ForTerraria/`。本文件记录模块边界与关键数据流，不逐个解释简单函数。历史架构记录见 [ARCHITECTURE-0.22.md](ARCHITECTURE-0.22.md)。

## 技术与模块边界

项目使用 C# 12、.NET 8 和 tModLoader 的 `Mod`、`ModSystem`、`ModPlayer`、`ModTileEntity` 生命周期。它是游戏内模组，没有独立 Web 服务、数据库或常驻后台同步进程。

| 模块 | 职责 | 主要文件 |
| --- | --- | --- |
| 入口与配置 | 快捷键、消息分派、服务器配置 | `AE2ForTerraria.cs`、`MEConfig.cs` |
| 身份与存储 | 身份、数量、容量、稳定保存、事务提交 | `Core/ItemKey.cs`、`ItemPersistence.cs`、`StorageLedger.cs`、`StorageTransaction.cs` |
| 规划与执行 | 索引、路线、条件、回调边界和预算调度 | `Core/RecipeIndex.cs`、`CraftPlanning.cs`、`RecipeExecutionPolicy.cs`、`TerminalCraftingExecutor.cs` |
| 网络与业务 | 设备原点、连通、聚合、存取、策略和绑定 | `Systems/MachineTiles.cs`、`MENetwork.cs`、`NetworkInventory.cs`、`StorageOperations.cs`、`NetworkRegistry*.cs`、`MEPlayer.cs` |
| 数据与进度 | 掉落、商店、NPC、拼音和阶段推断 | `Systems/GameDataIndex.cs`、`NpcIndex.cs`、`PinyinIndex.cs`、`Acquisition/`、`Progression/` |
| 联机协议 | 解码、授权、确认、订阅和分片快照 | `Systems/MENet*.cs`、`InventoryTransactionInputSystem.cs` |
| 游戏内容 | 物品、设备实体、贴图、配方和成就 | `Content/Items/`、`Content/Tiles/`、`Content/Achievements/` |
| 界面 | 焦点、窗口、选择、反馈和业务入口 | `Systems/UISystem.cs`、`AEUiSession.cs`、终端读状态与策略状态、`UI/` |
| 验证 | 显式启用的核心、UI 和玩家旅程夹具 | `Systems/*Regression*.cs`、`RegressionChecks.cs`、筛选后的 `validation/stage2/` |

`Core` 仍依赖 Terraria 类型，不能直接作为不依赖游戏引擎的类库。界面调用共享业务或发送意图，协议校验后进入业务；账本不承担 UI、玩家拾取或网络授权。

## 关键接口

| 契约 | 约束 |
| --- | --- |
| `IMEStorage.Ledger` / `Allows(ItemKey)` | 驱动器和存储单元提供账本与分区接收规则 |
| `IPartitionedStorage` | 提供过滤列表、白名单标记和失效通知，避免 UI 各自修改缓存 |
| `StorageOperations.DepositInventory` / `Withdraw` | 单人与服务端共用，使用真实物品，不能把显示条目当权威库存 |
| `StorageOperations.PreviewDeposit` | 使用完整身份、分区和账本模拟插入，只读返回可接收量及拒绝原因；联网显示不替代权威操作 |
| `CraftPlanner.Plan` | 对只读库存规划；返回消耗、产出、中间余料、配方、缺料和有限搜索状态，不直接扣物品 |
| `StorageTransaction.TryApply` | 模拟并复核接收、容量和状态；成功整体提交，失败保留原数据 |
| `MENet.Handle` | 校验协议和消息方向，再分派服务端或客户端对应域 |
| `TerminalReadState` / `TerminalPolicyState` | 使用终端位置和会话代次限定快照归属，避免旧窗口数据污染新窗口 |

## 数据如何保存

| 数据 | 内存表示 | 保存方式 |
| --- | --- | --- |
| 物品身份 | `ItemKey`：运行时类型、词缀、预留变体和规范化 `ItemIO` 数据 | `ItemPersistence` 使用稳定物品内容保存；数量、收藏不参与身份 |
| 存储数量 | `Dictionary<ItemKey, long>`、版本和容量限制 | 驱动器/存储单元实体 `TagCompound`；加载采用恢复逻辑，不按新容量截断旧内容 |
| 磁盘与工作站 | 实际 `Item` 槽数组 | 设备实体保存；磁盘不携带账本，工作站保留完整物品数据 |
| 任务与补货 | 持久任务标识、目标、剩余量、请求人、暂停和库存目标 | CPU 与合成终端实体；终端继承队列宿主，额外保存工作站 |
| 网络策略 | 成员关联、名称、所有者、权限、锁定和有限审计 | `NetworkRegistry` 世界数据；几何锚点负责定位，持久成员关联保持策略归属 |
| 绑定与磁铁 | 按世界区分的访问终端绑定、玩家开关 | `MEPlayer` 角色数据，兼容旧坐标字段 |
| 布局与收藏 | 本机几何与稳定内容名称 | `Main.SavePath` 下的 `AE2ForTerraria-Layout.txt`、`AE2-BrowserFavorites.json` |
| 模组配置 | `MEConfig` 与过滤定义 | tModLoader 服务器侧配置；实际个人/服务器配置文件不提交源码仓库 |
| 索引与快照 | 配方、掉落、聚合视图、终端缓存 | 可重建数据；加载、卸载、换世界和相关版本变化时失效 |

新物品保存不依赖跨会话恒定的数字编号。缺失模组使用 tModLoader 占位物品，并保留缺失过滤引用。旧纯数字格式只能兼容校验，不能承诺恢复已经失去来源的任意历史编号。

## 主要操作流程

**存取：**界面产生槽位、身份与数量意图。单人进入共享业务；多人由服务器检查玩家与网络权限，再按服务端背包和账本执行。部分成功留下余量，拒绝保留源物品。服务器回传权威槽位、完成序号和网络快照；客户端只接受匹配事务，等待期间保护参与槽位与鼠标物品。

**合成：**终端持有队列与工作站，同网络由规范终端统一调度。手动订单统一进入 `TerminalCraftingExecutor.Submit`，在主线程直接调用既有规划、复核和原子提交；不依赖下一次实体更新。新单独立保留身份，不合并进旧暂停或大额任务。不能立即完成的任务仍由轮转调度和每帧预算处理。规划先走常见低成本路线，失败后有限回溯，支持共享库存、配方组和批量余料。新增制作不被已有成品抵消，保持库存计入现有与在途数量。

提交前重新检查所属任务、真实 CPU、连接、材料、工作站、环境条件与权限，再经事务同时扣料和入库。只能容纳一批时走逐批路径，失败不先扣料。旧 CPU、装配室与单目标结构继续兼容；连接新版终端后统一调度，不强制丢弃旧工作站。旧装配室计时配置不能直接解释为新终端队列的速度上限。

**总线：**原版容器接触格解析到容器原点，按过滤、模式和锁定导入/导出；先准备目标物品再提交源扣除，避免重建异常丢失原内容。第三方存储容器不属于该接口。

**拓扑：**网络通过物理相邻和启用的同频道桥接连接。多格设备使用规范类型与原点。放置、拆除、频道和启停变化使连接缓存失效；合并、拆分同时处理库存视图及策略归属。

## 联机、界面与生命周期

当前 `MENet.ProtocolVersion` 为 \(7\)。协议域包括账本、存取、槽位、合成、设备、权限、玩家、库存页和分片。包体变化必须更新协议，并统一升级服务器和客户端。

权限快照按接收者权限发送；完整解码和校验后统一发布，撤权清理陈旧管理状态。远程终端采用汇总与设备快照，不能依赖附近存在全部本地实体。

窗口分主层、设备层和覆盖层，`AEUiSession` 控制输入、绘制层与鼠标物品占用，`WindowLayout` 保存布局，详情面板提供导航和说明。查询索引、规划预览和显示快照都不能绕过服务端检查。

0.26.2 将窗口底板改为平面边框，物品网格、槽位及图标共用黑色细描边，状态通过底色和角标表达。NPC 目录使用原生背包格底图及现有 `NpcVisuals` 立绘，不显示常驻名称，默认按已加载的图鉴索引排序。合成目录取消底部两行标签，工作台槽位仍由 `UIStationGrid` 管理；只有读取失败时保留底部权限提示。此次只调整界面，不改变配方条件、库存、存档结构或网络协议。

0.26.3 移除合成目录标题并左对齐筛选。`UIBrowserDetails` 使用有界页面历史，保存身份或 NPC、页签、配方、数量及各自滚动位置；历史不落盘，重载时重建。终端消费同一页面数量，NPC 页面清除操作目标。`UIStatus` 合并相同文字和颜色的活动提示，持续触发只刷新时间，不重复挤满屏幕。未改变存档结构或协议。

0.26.4 驱动器去掉 `UIPlayerInventoryPanel`，开窗同时打开原版背包，右侧内容和分区面板延伸到底部。手动订单返回真实执行状态；后台具名任务完成时，通过现有 `ServerNotice` 仅通知请求人，单人使用同一条状态提示。部分完成不冒充整单完成，匿名保持库存不弹完成通知。没有增加保存字段或修改协议包体。

`StorageSystem` 创建索引与规划器，最终配方和阶段数据在 `PostSetupRecipes` 重建；来源索引等待内容注册完整后建立。卸载和换世界时清理相关静态状态。客户端核心夹具运行在加载完成的菜单绘制线程，避免与后台加载并发借用全局状态。

## 扩展原则

0.26.5 将窗口默认几何集中到 `WindowGeometry.Compact()`；`.compact1` 布局键在首次打开时忽略旧尺寸，保留旧记录供回滚，后续仍读取新键。尺寸统一基于 `PlayerInput.OriginalScreenSize / Main.UIScale`，避免在已缩放的 UI 上下文中再次缩放 Main 屏幕尺寸。窄分类栏、驱动器和管理页根据实际宽度重排。

`MovableWindowState.RequestLayout` 在子控件更新期间只标记待重排，等完整子树更新结束后调用 `ApplyLayout`；下拉菜单自我移除也使用 `AfterChildUpdate`。这是为修复父级枚举期间移除、追加控件的异常。手柄保存初始抓取偏移，限制窗口尺寸，并在尺寸未变时跳过重算。静态排版检查必须与连续完整更新和真实输入旅程搭配。

尺寸参考为 [Recipe Browser 的配置默认值](https://github.com/JavidPack/RecipeBrowser/blob/1.4/RecipeBrowserClientConfig.cs)及[顶部按钮定义](https://github.com/JavidPack/RecipeBrowser/blob/1.4/RecipeBrowserUI.cs)，2026-10-16 核对提交 `f23758a6397898701149d42c51de64314cb4447a`。仅参考尺寸和交互，使用项目自身代码；仓库未发现明确许可证，不复制其实现或资源，不新增运行依赖。

新增设备优先实现现有存储/分区契约，物品身份沿用统一 codec，操作接入共享业务、权限、保存和协议。新增执行路径同时说明回调安全、原子性及预算。接口、包体、保存字段、缓存失效或 UI 行为改变时，修订本文件和[需求与验收](requirements.md)。
