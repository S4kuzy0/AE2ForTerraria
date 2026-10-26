# 最早获取来源的核对方法

核对日期：2026-10-26（北京时间）。适用游戏基线：Terraria 1.4.4.9 / tModLoader。阶段排序以当前已加载配方、Boss 顺序和已建模来源为准；查询时不联网，也不执行模拟钓鱼、任务领奖或世界进度回调。

## 判定规则

**0.27.14 适配规则：**下文旧版本记录中的精确包版本限制已被当前实现替代。普通布尔条件从实际配方、掉落条件和 BossChecklist 击败回调中只读解析；不执行未知谓词，也不临时改世界进度。支持已识别字段的与、或、非和透明委托包装，保留相互矛盾条件及 Boss 前限制。容器、采矿、配方路线在输入可得阶段重新核对条件，不能先忽略材料再单独判定。

无法化为布尔规则的灾厄生成、钓鱼与转换路线按相关方法结构分别核验，忽略包版本和元数据编号。只有相关方法变化的路线暂停并记录警告，其余来源继续推导。此检查仍可能受实际控制流、注册入口或编译器生成方法变化影响；它用于避免错误沿用旧证据，不能保证未知复杂代码自动理解。直接钓鱼、世界生成和任意运行时钩子仍需要可核查的来源声明。条件解析器依据当前安装程序集和 tML 的 [Condition](https://raw.githubusercontent.com/tModLoader/tModLoader/stable/patches/tModLoader/Terraria/Condition.cs) 结构实现。

先列出物品的独立获取方式：天然采集与生长、世界宝箱、直接钓鱼、宝匣／容器、掉落、商店、任务、采矿、微光、提炼和制作。不能因为模组补了一条晚期配方或商店，就丢掉原先更早的路线。

一条路线同时要求的条件取最晚阶段，多条可选路线取最早阶段：

\[
t(\text{物品})=\min_{r\in\text{已确认路线}}\max_{g\in\text{路线 }r\text{ 的必需条件}}t(g)
\]

钓鱼的稀有度、渔夫任务次数、天气和时间增加收集成本，不自动构成 Boss 门槛。困难模式、骷髅王后的地牢钓鱼等显式限制必须保留。熔岩钓鱼可以通过 Boss 前渔夫奖励金虫网捕捉的熔岩鱼饵开始；不能把热线钓竿当成唯一途径。

容器内容必须同时满足容器来源、钥匙与已知开启条件。采矿必须同时满足矿脉出现与实际可得工具镐力；工具数值读取已加载游戏样本，不能沿用旧版攻略。材料、工具或容器有更早来源后，继续传播到后续制作路线，不能只改悬停文字。

## 0.27.8 钓鱼来源交叉核对

下表是用于建立规则的代表样本；完整已声明的钓鱼来源见 `ProgressionFishingSources`，实际宝匣内容继续读取游戏掉落索引。

| 样本 | 最早可确认的来源与阶段 | 依据 |
| --- | --- | --- |
| 蛙腿 | 直接在有效水域钓鱼，Boss 前；血月掉落并非最早路线 | [蛙腿](https://terraria.wiki.gg/wiki/Frog_Leg) |
| 气球河豚 | 任意生物群系水域钓鱼，Boss 前 | [气球河豚](https://terraria.wiki.gg/wiki/Balloon_Pufferfish) |
| 掠夺鲨、锯齿鲨、剑鱼 | 海洋钓鱼，Boss 前；矿物仍受实际工具能力约束 | [海洋探索](https://terraria.wiki.gg/wiki/Guide:Walkthrough)、[掠夺鲨版本记录](https://terraria.wiki.gg/wiki/1.4) |
| 岩鱼、装甲洞穴鱼、镜面鱼、蜂蜜鱼 | 对应水域钓鱼；生物群系或液体要求不等于 Boss 条件 | [钓鱼捕获物](https://terraria.wiki.gg/wiki/Fishing_catches) |
| 黑曜石鱼 | 熔岩钓鱼，Boss 前可得 | [黑曜石鱼](https://terraria.wiki.gg/wiki/Obsidifish)、[熔岩钓鱼](https://terraria.wiki.gg/wiki/Fishing_Level) |
| 黑曜石剑鱼 | 本机版本明确要求困难模式；不能从黑曜石鱼的门槛类推 | [黑曜石剑鱼及历史](https://terraria.wiki.gg/wiki/Obsidian_Swordfish) |
| 地牢匣、炼药桌 | 地牢钓鱼需击败骷髅王；围栏匣还需困难模式 | [地牢匣及历史](https://terraria.wiki.gg/wiki/Dungeon_Crate) |
| 天空、丛林、冰冻、绿洲、海洋宝匣 | 困难模式前对应生物群系可钓；内容再经过容器路线推导 | [宝匣](https://terraria.wiki.gg/wiki/Crate) |
| 水晶蛇、毒弹枪、舌锋剑、鳞片松露 | 直接钓鱼但保留困难模式限制 | [水晶蛇](https://terraria.wiki.gg/wiki/Crystal_Serpent)、[鳞片松露](https://terraria.wiki.gg/wiki/Scaly_Truffle)、[困难模式探索](https://terraria.wiki.gg/wiki/Guide:Game_progression) |
| 金钓竿、金虫网、无底水桶、超级吸水棉、无底蜂蜜桶 | 渔夫奖励存在 Boss 前路线；任务数量仍可写在来源中 | [渔夫奖励](https://terraria.wiki.gg/wiki/Quest_rewards)、[无底桶历史](https://terraria.wiki.gg/wiki/Bottomless_Water_Bucket)、[吸水棉历史](https://terraria.wiki.gg/wiki/Ultra_Absorbant_Sponge) |
| 热线钓竿、鳍翅 | 渔夫奖励仍有困难模式条件 | [渔夫奖励](https://terraria.wiki.gg/wiki/Quest_rewards) |
| 当前灾厄的附魔日晷 | 本机灾厄 2.2.4 的第 \(26\) 次渔夫任务，无 Boss 门槛；旧历史中热线钓竿的提前奖励不适用于此版本 | [灾厄奖励变更历史](https://calamitymod.wiki.gg/wiki/History:Vanilla_changes)与本机 `AddMoreGuaranteedAnglerRewards` 只读对照 |
| 生命水晶、魔镜 | 天然生成／世界宝箱为 Boss 前来源，不能只查合成 | [生命水晶](https://terraria.wiki.gg/wiki/Life_Crystal)、[魔镜](https://terraria.wiki.gg/wiki/Magic_Mirrors) |

## 版本与数据边界

Wiki 当前主页面包含较新的 Terraria 内容。核对时阅读历史记录，并与本机 1.4.4.9 的 `FishingCheck_RollItemDrop`、`GetAnglerReward_MainReward` 进行只读对照；例如黑曜石剑鱼的困难模式分支、地牢钓鱼的骷髅王判断、无底水桶的 Boss 前奖励均已核实。不会把新版新增鱼类加入旧版索引，也不会混入旧主机／3DS 规则。钓鱼垃圾 `FishingSeaweed` 与同名宠物 `Seaweed` 是不同身份，不能按显示名混用。

本机灾厄版本为 2.2.4，其渔夫奖励 Hook 只对特定任务替换奖励，其余调用原版逻辑。附魔日晷提前来源仅对该已核实版本启用；热线钓竿继续保留原版困难模式门槛。不能把 Wiki 的历史奖励表直接套到当前安装版。

tModLoader 的 [CatchFish / AnglerQuestReward](https://github.com/tModLoader/tModLoader/blob/1.4.4/patches/tModLoader/Terraria/ModLoader/ModPlayer.cs) 是运行时回调，不能安全当作静态掉落表执行。第三方钓鱼回调、特殊种子、任意微光分解、自定义转换钩子及额外提炼产物等未完整建模的路线，需要单独查证；已有来源表不能证明全模组的所有获取方式均已覆盖。补新来源时记录版本、条件和来源链接，并同时增加“应提前”和“不应提前”的代表回归。

## 0.27.9 非钓鱼来源自查

先导出实际加载内容的最早阶段与掉落条件，再核对可疑物品；导出不会加载／保存角色和世界。已确认的错误分为三类：遗漏独立来源、遗漏负数 NPC 变体，以及掉落表被扁平化后丢失条件。阶段修正参加统一工作队列，继续影响配方、容器、转换和采矿工具。

| 样本 | 自查前 | 修正后的来源及阶段 | 依据 |
| --- | --- | --- | --- |
| 鲨鱼鳍 | 后期酸雨 | 普通鲨鱼掉落，Boss 前 | [鲨鱼](https://terraria.wiki.gg/wiki/Shark)与实际掉落记录 |
| 粉凝胶 | 史莱姆皇后 | 粉史莱姆掉落，Boss 前；其负数变体编号须独立查询 | [粉史莱姆](https://terraria.wiki.gg/wiki/Pinky)与本机 NPCID／掉落数据库 |
| 生命果 | 世纪之花 | 任意机械 Boss 后自然生长 | [机械 Boss](https://terraria.wiki.gg/wiki/Mechanical_boss)的版本历史；当前样本为双子魔眼阶段 |
| 陨石 | 世纪之花后的商店 | 邪恶 Boss 后的陨石矿脉，并要求可得工具镐力 | [灾厄困难模式前流程](https://calamitymod.wiki.gg/wiki/Guide:Class_setups/Pre-Hardmode) |
| 工匠作坊、火箭靴 | 未知 | 哥布林军队后工匠商店，沿用可编辑事件顺序 | [哥布林军队](https://terraria.wiki.gg/wiki/Goblin_Invasion)与实际商店 |
| 生物群落钥匙 | 特洛伊松鼠后 | 困难模式普通敌怪掉落；开地牢箱另需世纪之花 | [生物群落钥匙](https://terraria.wiki.gg/wiki/Biome_Keys)与本机条件类型 |
| 魔菇、毒蘑菇、丛林草种子、普通树果 | 未知或较晚替代路线 | 对应自然采集，Boss 前 | 本机物品标识及自然来源声明 |
| 蚊子琥珀 | 未知 | 泥沙／雪泥／沙漠化石与提炼机共同可得，Boss 前 | [蚊子琥珀](https://terraria.wiki.gg/wiki/Amber_Mosquito)、[提炼机](https://terraria.wiki.gg/wiki/Extractinator)与本机 ExtractinatorUse |
| 活力水晶、奥术水晶、珍馐、黏性蠕虫、银河珍珠 | 未知 | 直接微光转换，继承生命水晶／魔力水晶／树果／金蠕虫／粉珍珠的早期来源 | [微光](https://terraria.wiki.gg/wiki/Shimmer)与本机直接转换表 |
| 埃癸斯果 | 未知 | 生命果直接微光转换，继承任意机械 Boss 门槛 | 同上 |
| 和谐杖、泰拉改造枪、无底微光桶 | 未知 | 同时满足输入物品和月亮领主门槛 | 本机 CanShimmer／GetShimmered；[泰拉改造枪](https://terraria.wiki.gg/wiki/Terraformer) |
| 骨头 | Boss 前 | 原地牢愤怒骷髅怪来源改为骷髅王后 | 本机普通地牢怪物生成门槛；实际更早有效来源仍可覆盖 |

事件的完整 NPC 列表不能证明每个成员都能在最早阶段生成。困难模式哥布林召唤师、血月后期怪物、撒旦军队高阶段成员另有生成门槛；独立 Boss 优先使用更具体的 BossChecklist 检查点，不能让恐惧鹦鹉螺或双足翼龙掉落继承早期整场事件的时间。

直接微光转换只读取已加载的原版输入转换表，特殊月亮领主升级另加门槛；没有执行 CanShimmer、分解可用性或任意模组回调。提炼只声明已核验的标准原矿、宝石、蚊子琥珀、坚固化石输出，必须同时取得输入材料和提炼机；牡蛎珍珠使用开容器路线。没有声称已覆盖叶绿提炼机的所有额外转换或全部模组新产物。

BossChecklist 的物品清单缺少逐条条件：实际掉落表已有记录时，不能用清单兜底绕过困难模式、机械 Boss 或未知谓词。原版已核验的难度、节日、种子和月亮事件波数条件不凭空增加永久 Boss 门槛；Fargo 1.7.3.9 的少数已审阅模式条件单独适配。未知动态条件继续显示未知，不能把此前未经条件验证的阶段视为可靠来源。排序是已建模路线的最早阶段，不是全模组所有物品逐条攻略核验。

## 0.27.10：继续核对常用缺料与依赖

0.27.9 的简短交付表仅为代表。其完整运行结果中，已有阶段之间变化的物品为 \(751\) 项，原未知变为可推导阶段为 \(1088\) 项，另有 \(500\) 项失去未经条件核验的旧锚点而回到未知。这些统计包含配方传播，不能称为逐一查证的纠错数量。未知项仍需按证据继续核对。

继续核对发现飞翔之魂、灵液等实际掉落被 Fargo 1.7.3.9 的 `EModeEarlyBirdLockDropCondition` 包裹。本机只读 IL 确认其表达式为困难模式或非永恒模式；该模组可让部分原版困难模式敌怪提前生成，但永恒模式下普通奖励仍被锁至困难模式。适配仅限该精确版本及已审阅的原版掉落者，不对未知同类条件笼统放行。新增阶段断言在修复前因飞翔之魂未知而失败。

| 物品 | 0.27.9 的状态 | 本轮规则 | 依据 |
| --- | --- | --- | --- |
| 飞翔之魂 | 未知 | 飞龙掉落，困难模式 | 本机 Fargo 条件 IL、[飞翔之魂](https://terraria.wiki.gg/wiki/Soul_of_Flight) |
| 蛇发女妖头 | 未知 | 同一受限奖励条件，困难模式 | 本机实际掉落表与 Fargo 条件 |
| 龟壳、冰冻海龟壳 | 未知 | 对应陆龟的小怪来源，困难模式 | [龟壳](https://terraria.wiki.gg/wiki/Turtle_Shell)、[冰冻海龟壳](https://terraria.wiki.gg/wiki/Frozen_Turtle_Shell) |
| 火羽 | 未知 | 红魔鬼在任意机械 Boss 后出现 | [火羽](https://terraria.wiki.gg/wiki/Fire_Feather)、[红魔鬼](https://terraria.wiki.gg/wiki/Red_Devils) |
| 恶魔之心 | 未知 | 血肉墙宝藏袋；“未使用过”不增加 Boss 门槛 | 本机 `NotUsedDemonHeart.CanDrop` 只检查角色的额外饰品栏标志 |
| 日耀、星旋、星云、星尘翅膀 | 未知 | 实际配方需要飞翔之魂；材料来源恢复后继续传播，取已知完整路线 | 本机已加载配方、材料与工作站索引 |

同时补齐小怪 World Feeder、猪龙、愤怒雨云及陆龟类的普通生成门槛和实际掉落。这里的物品阶段仍与世界生成、商店及其他已建模配方比较取最早，不以恢复旧阶段为目标，也不把 Boss 前被模组提前生成的小怪直接当成所有普通掉落均可提前取得。

`AcquisitionAudit` 增加实际配方的材料、工作站和条件，以及容器逐条掉落条件，便于从未知成品追查最先缺失的来源。新增回归使用会抛异常的未知掉落谓词，确认查询不执行它，也不会用扁平清单兜底捏造阶段。源码适配保持有限声明，没有执行获取／生成／配方回调。

## 0.27.11：捕捉来源与重复检查点

木魔石的错误由松鼠的捕捉来源缺失引起：Boss 掉落表中的特洛伊松鼠成为唯一已知来源，进而延后整条配方。现在以实际原版 NPC 样本的 `catchItem` 和 `CountsAsCritter` 建立捕虫网路线，按最早路线传播；不把所有有 `catchItem` 的 NPC 都当成普通小动物，避免混入 Fargo 可捕捉城镇居民的动态入住门槛。普通蝴蝶共用一种 NPC，额外声明其不同捕捉品种。

原版松露虫保留困难模式门槛，七彩草蛉的原版捕捉路线保留世纪之花门槛，熔岩小动物要求防熔岩捕虫网或金捕虫网；其他已加载模组提供的更早掉落、配方仍可取最早。例如当前 Fargo 环境的七彩草蛉可由困难模式妖精掉落，不能强行推迟到原版捕捉阶段。落木附近可捕捉的仙灵可在 Boss 前取得，区别于不可捕捉的地下引路仙灵。

同批检查补齐白蜡木与摇树辣椒的自然来源，修正灰烬木魔石及其依赖。参考[小动物](https://terraria.wiki.gg/wiki/Critter)、[蝴蝶](https://terraria.wiki.gg/wiki/Butterfly)、[捕虫网](https://terraria.wiki.gg/wiki/Bug_Net)、[仙灵](https://terraria.wiki.gg/wiki/Fairies)、[白蜡树](https://terraria.wiki.gg/wiki/Trees#Ash_tree)，并与实际加载的物品、NPC、配方索引对照。未扩展为任意模组的生成或捕捉回调推演。

火把神同时存在 BossChecklist 记录和名称推断记录。推断补全现在先按 NPC 身份排除已经覆盖的记录，再进行名称兜底；旧偏好中已经消失的重复标识自动忽略，真实阶段及其元数据保留。阶段重排后重新计算所有来源；上调对应更小的排名，下调对应更大的排名。

## 0.27.13：合法提前探索与遗漏材料链

本轮先对照实际 0.27.11 导出的物品阶段，再查 Wiki、版本历史与本机 Terraria 1.4.4.9／Calamity 2.2.4 的只读 IL。Wiki 部分主页面已经描述新版，不能直接套用：例如皮鞭在本机要求原版图鉴达到 \(10\%\)，不是新版的 \(3\%\)；tML 图鉴完成度在此处只统计原版条目。公开灾厄源码的版本也与本机不同，涉及门槛的结论以安装版交叉核对。

| 物品或材料链 | 合法最早路线 | 依据 |
| --- | --- | --- |
| 陨石锭、陨石装备、太空枪 | 深层金箱可出陨石锭，Boss 前；金属锭分解另外检查本机实际配方 | [陨石锭](https://terraria.wiki.gg/wiki/Meteorite_Bar)、[金箱](https://terraria.wiki.gg/wiki/Gold_Chest)，本机 AddBuriedChest |
| 大自然的恩赐、丛林玫瑰、向日葵与染料植物 | 天然花草采集，Boss 前；树妖商店只是替代路线 | [恩赐](https://terraria.wiki.gg/wiki/Nature%27s_Gift)、[植物](https://terraria.wiki.gg/wiki/Plants)，本机 KillTile_GetItemDrops |
| 沙岩、硬化沙、灰烬 | 世界原生地形，Boss 前正常采集 | [地下沙漠](https://terraria.wiki.gg/wiki/Underground_Desert)、[地狱](https://terraria.wiki.gg/wiki/The_Underworld) |
| 狱炎箭、照明弹 | 地狱罐子／照明弹枪同箱弹药；蓝照明弹另需先有枪才能从商人购买 | [狱炎箭](https://terraria.wiki.gg/wiki/Hellfire_Arrow)、[照明弹](https://terraria.wiki.gg/wiki/Flare)，本机箱子、罐子与商店分支 |
| 书、水蜡烛、金钥匙 | 地牢入口天然摆设、普通未锁箱；钥匙还可出自地下地牢罐子 | [地牢](https://terraria.wiki.gg/wiki/Dungeon)、[金钥匙](https://terraria.wiki.gg/wiki/Golden_Key) |
| 水矢、炼药桌、施法桌、地牢金箱与暗影箱物品 | 可正常移动冒险躲避守卫取物；金箱／暗影箱仍需对应钥匙 | [地牢](https://terraria.wiki.gg/wiki/Dungeon)、[暗影钥匙](https://terraria.wiki.gg/wiki/Shadow_Key)；本机原生和关键 Fargo／灾厄地块钩子只读核对 |
| 组织样本、暗影鳞片、邪恶镐与狱石装备 | 击杀飞眼怪或部分虫体，不必击败完整 Boss；正常召唤和分段战斗可在 Boss 前完成 | [飞眼怪](https://terraria.wiki.gg/wiki/Creeper)、[暗影鳞片](https://terraria.wiki.gg/wiki/Shadow_Scale)、[实用技巧](https://terraria.wiki.gg/wiki/Guide:Practical_tips#Skipping_ahead)，实际部件掉落记录 |
| 苔藓、邪恶石块、桶装液体、镒块 | 漆铲采苔藓、正常炸弹采石、空桶取液体、水与天然微光接触 | [苔藓](https://terraria.wiki.gg/wiki/Moss)、[桶](https://terraria.wiki.gg/wiki/Buckets)、[微光](https://terraria.wiki.gg/wiki/Shimmer)；工具／液体作为路线输入 |
| 稽古衣、武士刀、水枪 | 普通世界旅商随机池，无 Boss 门槛；武士刀在颠倒世界有不同替代规则 | [旅商](https://terraria.wiki.gg/wiki/Traveling_Merchant)、[稽古衣](https://terraria.wiki.gg/wiki/Gi)、[武士刀](https://terraria.wiki.gg/wiki/Katana)，本机 SetupTravelShop_GetItem |
| 皮鞭、花园侏儒、雨天帽衣 | Boss 前图鉴探索后购买、侏儒日照石化、雨衣僵尸／雨伞史莱姆掉落 | [皮鞭](https://terraria.wiki.gg/wiki/Leather_Whip)、[花园侏儒](https://terraria.wiki.gg/wiki/Garden_Gnome)、[雨衣僵尸](https://terraria.wiki.gg/wiki/Raincoat_Zombie) |
| 杨桃 | 珍珠木摇树；神圣种子或珍珠沙作为环境输入，随最早来源传播 | [杨桃](https://terraria.wiki.gg/wiki/Star_Fruit)、[树](https://terraria.wiki.gg/wiki/Trees)，本机 ShakeTree |
| 乌兹 | 困难模式愤怒捕手掉落，早于世纪之花后的商店 | [乌兹](https://terraria.wiki.gg/wiki/Uzi)、[愤怒捕手](https://terraria.wiki.gg/wiki/Angry_Trapper) |
| 钨钢材料、风暴之颚、虚空石 | 增幅器、风暴蚁狮、发光石首鱼的已核验普通掉落，Boss 前 | [钨钢增幅器](https://calamitymod.wiki.gg/wiki/Wulfrum_Amplifier)、[风暴蚁狮](https://calamitymod.wiki.gg/wiki/Stormlion)、[发光石首鱼](https://calamitymod.wiki.gg/wiki/Luminous_Corvina) |
| 沉沦之海材料 | 天然采矿；海棱晶和碎片要求实际镐力，旧荒漠灾虫锁已移除 | [沉沦渊石历史](https://calamitymod.wiki.gg/wiki/History:Navystone)、[海棱晶](https://calamitymod.wiki.gg/wiki/Sea_Prism)，本机 ModTile |
| 天蓝矿 | 腐巢意志或血肉宿主后注魔矿脉／天蓝史莱姆，早于钴矿微光替代路线 | [天蓝矿](https://calamitymod.wiki.gg/wiki/Aerialite_Ore)，本机生成与 SpawnChance |
| 寒元矿 | 极地之灵首杀生成矿脉，再要求实际可得镐力；寒元史莱姆另需两个机械 Boss | [寒元矿](https://calamitymod.wiki.gg/wiki/Cryonic_Ore)、[寒元史莱姆](https://calamitymod.wiki.gg/wiki/Cryo_Slime)，本机 Cryogen.OnKill 与 SpawnChance |
| 熔渣矿 | 天然深渊矿脉要求 \(210\) 镐力；另有石巨人后混乱刺鲀掉落，无需推到月亮领主 | [熔渣矿](https://calamitymod.wiki.gg/wiki/Scoria_Ore)，本机 MinPick、CanExplode 及掉落条件 |
| 流明晶、深渊细胞 | 本机版本为利维坦和阿娜希塔后深渊怪掉落，不能沿用旧版门槛 | [流明晶](https://calamitymod.wiki.gg/wiki/Lumenyl)、[深渊细胞](https://calamitymod.wiki.gg/wiki/Depth_Cells)，本机 PostLevi |
| 霜印剑、冰雪弓、寒霜之花 | 本机灾厄普通世界冰宝箱怪替代掉落仍只需困难模式，保留非雕像条件 | [冰雪宝箱怪](https://terraria.wiki.gg/wiki/Ice_Mimic)，本机 CalamityGlobalNPC 的精确分支 |
| 血珠 | 克苏鲁之眼后，夜间邪恶粉末将天然猪猪变为恐惧之猪再击杀；无需先等血月 | [血珠](https://calamitymod.wiki.gg/wiki/Blood_Orb)、[恐惧之猪](https://calamitymod.wiki.gg/wiki/Horrible_Hog)，本机转化投射物与 PostEoC 分支 |

这些早期材料会继续沿实际配方传播到魔石、武器和工具；没有给成品逐个写死阶段。路线不使用 hoik、穿墙、复制、无限液体或跨世界带入。地牢危险不构成拾取锁，但地牢鱼匣仍有明确骷髅王条件；深渊宝箱另有灾厄骷髅王锁，本轮没有当成普通暗影箱。施法桌取得不等于 Fargo 的永久增益功能已经解锁。

同时修正反向错误：机械召唤物的 `MechanicalBossesDummyCondition` 只是掉落报表占位符，真正 `MechBossSpawnersDropRule.CanDrop` 仍要求困难模式，因此不能按占位谓词恒真判作 Boss 前。独立 Boss 条件优先选择其具体检查点，避免双足翼龙等被早期整场事件抢占；原生事件条件与全部机械 Boss 条件保留真实联合门槛。

金属锭分解仅覆盖明确审核的原版金属锭，按实际候选顺序、禁分解标记、定制输出和已知分解条件处理；直接转换优先，遇到不能解释的早期候选停止，不调用动态 `DecraftAvailable`。含骨头／神庙砖的分解保护不被绕过，狱炎箭的直接转化也不能反推成狱石锭。依据 [tML Recipe API](https://docs.tmodloader.net/docs/stable/class_recipe.html) 与本机 ShimmerTransforms；任意模组分解回调、特殊种子、其余未建模商店／NPC／容器仍需单独核查。

### 本轮运行中发现的版本差异

- 天然普通箱、洞穴金箱中的投刀是 Boss 前来源，血月商店仅为另一种方式；与旅商出售的和服一起修正后，忍者魔石沿真实材料门槛推导。[投刀 Wiki](https://terraria.wiki.gg/wiki/Throwing_Knife)
- 灾厄 2.2.4 的 `AdjustShimmerRequirements` 将混沌传送杖的转换条件改为至尊灾厄与星流巨械全部击败；和谐杖不能使用原版月领主门槛。未适配的灾厄版本不猜测该钩子。[灾厄微光](https://calamitymod.wiki.gg/wiki/Shimmer)
- 实际启用包中的熵已更新到 4.3，混沌精原有掉落被包进虚空驱逐舰前后条件。本机 IL 确认前置分支只有完成状态上界，正常混沌精掉落仍在肉山后；目前只适配经核实的混沌精分支，不把所有模组条件视为无门槛。
- 荧光虫依赖神圣生态；需要捕虫网及可建立神圣环境的材料，不再和森林萤火虫一并算作开局来源。

撒旦军团完成条件另外读取本机 `ReadyToFindBartender`、`ReadyForTier2/3` 和 `WinInvasionInternal`：保留邪恶 Boss／困难模式等真实前提，高难度通关同时满足低难度完成标志。自定义排序不能移除这些条件。
