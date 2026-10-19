# 最早获取来源的核对方法

核对日期：2026-10-19（北京时间）。适用游戏基线：Terraria 1.4.4.9 / tModLoader。阶段排序以当前已加载配方、Boss 顺序和已建模来源为准；查询时不联网，也不执行模拟钓鱼、任务领奖或世界进度回调。

## 判定规则

先列出物品的独立获取方式：天然采集、世界宝箱、直接钓鱼、宝匣／容器、掉落、商店、任务、采矿和制作。不能因为模组补了一条晚期配方或商店，就丢掉原先更早的路线。

一条路线同时要求的条件取最晚阶段，多条可选路线取最早阶段：

\[
t(\text{物品})=\min_{r\in\text{已确认路线}}\max_{g\in\text{路线 }r\text{ 的必需条件}}t(g)
\]

钓鱼的稀有度、渔夫任务次数、天气和时间增加收集成本，不自动构成 Boss 门槛。困难模式、骷髅王后的地牢钓鱼等显式限制必须保留。熔岩钓鱼可以通过 Boss 前渔夫奖励金虫网捕捉的熔岩鱼饵开始；不能把热线钓竿当成唯一途径。

容器内容必须同时满足容器来源、钥匙与已知开启条件。采矿必须同时满足矿脉出现与实际可得工具镐力；工具数值读取已加载游戏样本，不能沿用旧版攻略。材料、工具或容器有更早来源后，继续传播到后续制作路线，不能只改悬停文字。

## 本轮交叉核对

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

tModLoader 的 [CatchFish / AnglerQuestReward](https://github.com/tModLoader/tModLoader/blob/1.4.4/patches/tModLoader/Terraria/ModLoader/ModPlayer.cs) 是运行时回调，不能安全当作静态掉落表执行。第三方钓鱼回调、特殊种子、微光转换、提炼机等尚未完整建模的路线，需要单独查证；已有来源表不能证明全模组的所有获取方式均已覆盖。补新来源时记录版本、条件和来源链接，并同时增加“应提前”和“不应提前”的代表回归。
