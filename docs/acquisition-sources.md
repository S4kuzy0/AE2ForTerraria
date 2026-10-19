# 最早获取来源的核对方法

核对日期：2026-10-19（北京时间）。适用游戏基线：Terraria 1.4.4.9 / tModLoader。阶段排序以当前已加载配方、Boss 顺序和已建模来源为准；查询时不联网，也不执行模拟钓鱼、任务领奖或世界进度回调。

## 判定规则

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
