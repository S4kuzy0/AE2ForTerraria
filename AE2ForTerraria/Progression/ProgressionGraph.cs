using System.Collections.Generic;
using System.Text;
using System;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Progression
{
	/// <summary>
	/// 进度图:把物品锚定到检查点(阶段),支撑「标注时间点」和「按时间点排序」。
	/// 数据来源按优先级分层:玩家覆盖 > BossChecklist > 内置知名模组表 > 推断 > 原版里程碑。
	/// 物品时间点用两步得到:① 显式锚定(掉落/Boss/事件) ② 配方图推导(瓶颈继承)。
	/// </summary>
	public sealed class ProgressionGraph
	{
		public const int UnknownTier = -1;
		private const int MaxDepth = 32;

		private readonly RecipeIndex _recipes;
		private readonly List<IBossOrderProvider> _providers = new List<IBossOrderProvider>();
		private readonly Dictionary<ItemKey, int> _explicitItemTier = new Dictionary<ItemKey, int>();
		/// <summary>由「Boss 阶段 + 该 Boss 的掉落物」推出来的物品时间点。</summary>
		private readonly Dictionary<ItemKey, int> _bossAnchored = new Dictionary<ItemKey, int>();
		private readonly Dictionary<ItemKey, int> _inferredCache = new Dictionary<ItemKey, int>();
        private readonly Dictionary<int,List<int>> _stationItems = new();
        private bool _tierCacheReady; private int _tierRecipeRevision;
        private bool[] _effectiveTiers = Array.Empty<bool>();
        public IReadOnlyList<ProgressionTier> DefaultOrder { get; private set; } = Array.Empty<ProgressionTier>();
        private readonly Dictionary<int,string> _sourceReasons = new();
        private readonly Dictionary<int,string> _reasons = new();
        internal void SourceReason(int type, string reason) => _sourceReasons[type] = reason;
        public int Revision { get; private set; }
        public double TierBuildMilliseconds { get; private set; }
        public int RankedItemCount { get; private set; }

		public readonly List<ProgressionTier> Tiers = new List<ProgressionTier>();
		public string ActiveProviderName { get; private set; } = "none";

		public ProgressionGraph(RecipeIndex recipes)
		{
			_recipes = recipes;
			// Personal order is applied to full provider records, preserving NPC and drop metadata.
			_providers.Add(new BossChecklistProvider());
			_providers.Add(new BuiltinModOrderProvider());
			_providers.Add(new InferenceProvider());
			_providers.Add(new VanillaCheckpointProvider());
			_providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
		}

		public void Build()
		{
			Tiers.Clear();
			ActiveProviderName = "none";

			foreach (IBossOrderProvider provider in _providers)
			{
				var candidate = new List<ProgressionTier>();
				if (provider.TryBuild(candidate) && candidate.Count > 0)
				{
					Tiers.AddRange(candidate);
					ActiveProviderName = provider.Name;
					break;
				}
			}

            if (!Tiers.Exists(t=>t.Id=="start")) Tiers.Insert(0,new ProgressionTier(0,"start","开局","vanilla"));
			for (int i = 0; i < Tiers.Count; i++)
			{
				Tiers[i].Index = i;
                ProgressionSources.AssignCheckpointNpcs(Tiers[i]);
			}

			_inferredCache.Clear(); _tierCacheReady=false;

			AppendInferredBosses();
            DefaultOrder = Tiers.ToArray();
            var savedOrder = !Main.dedServ ? global::AE2ForTerraria.UI.BossOrderPreferences.Order : Array.Empty<string>();
            if(savedOrder.Count > 0) ApplyOrder(savedOrder);
            else
            {
                var legacy = MEConfig.Instance?.CustomBossOrder;
                ApplyOrder(legacy == null ? Array.Empty<string>() : legacy.Select(name =>
                    Tiers.FirstOrDefault(t => t.Id == name || t.DisplayName == name)?.Id).Where(id => id != null).ToArray());
            }
            _stationItems.Clear();
            foreach (var item in ContentSamples.ItemsByType.Values)
                if (!item.IsAir && item.createTile>=0)
                {
                    if (!_stationItems.TryGetValue(item.createTile,out var list)) _stationItems[item.createTile]=list=new();
                    list.Add(item.type);
                }
            AnchorItemsFromBosses();
            ProgressionSources.Anchor(this, _bossAnchored);
		}

		/// <summary>
		/// 用推断引擎**补全**阶段表。
		///
		/// 以前推断引擎的优先级是 30,只要装了 BossChecklist(10)或灾厄/Fargo(20)
		/// 就永远轮不到它。现在改成:先让 provider 链给出基础阶段表,
		/// 再把「没被任何阶段覆盖的模组 Boss」按推断分数追加到后面。
		/// 这样它一定有活干。
		/// </summary>
		private void AppendInferredBosses()
		{
			// ComputeInferredBosses() 已经保证返回的是副本,这里直接改它不会碰到静态缓存。
			// (下面的 ApplySummonDependencies 会原地改写分数并排序)
			List<(int npc, float score)> extras = ComputeInferredBosses();

			if (extras.Count == 0)
			{
				return;
			}

			ApplySummonDependencies(extras);
			extras.Sort((a, b) => a.score.CompareTo(b.score));

			int added = 0;

			foreach ((int npc, float score) in extras)
			{
				string name = NpcIndex.GetName(npc);
				if (string.IsNullOrWhiteSpace(name) || Tiers.Any(t => t.NpcIds.Contains(npc)) || MatchedByExistingTier(name))
				{
					continue;
				}

                var modNpc = NPCLoader.GetNPC(npc);
                string stableNpc = modNpc == null ? "Terraria/" + NPCID.Search.GetName(npc) : modNpc.Mod.Name + "/" + modNpc.Name;
                var inferred = new ProgressionTier(Tiers.Count,"inf_"+stableNpc,name,"inferred");
                inferred.NpcIds.Add(npc); Tiers.Add(inferred);
				added++;
			}

			if (added > 0)
			{
				for (int i = 0; i < Tiers.Count; i++)
				{
					Tiers[i].Index = i;
				}

				if (MEConfig.DiagnosticsEnabled)
				{
					Terraria.ModLoader.ModContent.GetInstance<AE2ForTerraria>()?.Logger.Info(
						"推断引擎补全了 " + added + " 个模组 Boss 阶段(总共 " + Tiers.Count + " 个阶段)");
				}
			}
		}

		private bool MatchedByExistingTier(string npcName)
		{
			string want = Normalize(npcName);
			if (want.Length < 4)
			{
				return false;
			}


			foreach (ProgressionTier tier in Tiers)
			{
				string have = Normalize(tier.DisplayName);
				if (have.Length >= 4 && (have.Contains(want) || want.Contains(have)))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// 召唤依赖:Boss B 的召唤物如果需要 Boss A 的掉落,那 B 一定晚于 A。
		///
		/// 这里是**真正的 DAG 拓扑排序**(带优先级的 Kahn 算法):
		/// 每个「可以放行」的 Boss 取当前分数最低的那个先排,
		/// 于是「按拓扑序排列」和「同层内按难度排」同时成立。
		/// 出现环时(模组互相依赖很常见)取剩下里分数最低的强行破环,
		/// 保证一定能排完。
		/// </summary>
		private static void ApplySummonDependencies(List<(int npc, float score)> bosses)
		{
			int count = bosses.Count;
			if (count == 0)
			{
				return;
			}

			// npc -> 它在列表里的下标
			var indexOf = new Dictionary<int, int>();
			for (int i = 0; i < count; i++)
			{
				indexOf[bosses[i].npc] = i;
			}

			// npc -> 它掉落的物品集合
			var drops = new Dictionary<int, HashSet<int>>();
			foreach ((int npc, float score) in bosses)
			{
				var set = new HashSet<int>();
				foreach ((int item, float rate) in GameDataIndex.GetDrops(npc))
				{
					set.Add(item);
				}

				drops[npc] = set;
			}

			// npc -> 候选召唤物
			var summons = new Dictionary<int, int>();
			int total = ItemLoader.ItemCount;

			foreach ((int npc, float score) in bosses)
			{
				string name = Normalize(NpcIndex.GetName(npc));
				if (name.Length < 4)
				{
					continue;
				}

				for (int itemType = 1; itemType < total; itemType++)
				{
					string itemName = Lang.GetItemNameValue(itemType);
					if (string.IsNullOrWhiteSpace(itemName)
						|| !(itemName.Contains("召唤") || itemName.Contains("Summon") || itemName.Contains("Spawn")))
					{
						continue;
					}

					if (Normalize(itemName).Contains(name))
					{
						summons[npc] = itemType;
						break;
					}
				}
			}

			if (summons.Count == 0)
			{
				return;
			}

			// 建边:A -> B 表示「B 必须排在 A 之后」
			var edges = new List<HashSet<int>>(count);
			for (int i = 0; i < count; i++)
			{
				edges.Add(new HashSet<int>());
			}

			foreach (KeyValuePair<int, int> summon in summons)
			{
				if (!indexOf.TryGetValue(summon.Key, out int b))
				{
					continue;
				}

				IReadOnlyList<int> recipes =
					StorageSystem.Recipes?.GetRecipesFor(ItemKey.Crafted(summon.Value)) ?? Array.Empty<int>();

				if (recipes.Count == 0)
				{
					continue;
				}

				Recipe recipe = Main.recipe[recipes[0]];
				if (recipe?.requiredItem == null)
				{
					continue;
				}

				foreach (Item ingredient in recipe.requiredItem)
				{
					if (ingredient == null || ingredient.type <= 0)
					{
						continue;
					}

					foreach (KeyValuePair<int, HashSet<int>> pair in drops)
					{
						if (pair.Key != summon.Key
							&& pair.Value.Contains(ingredient.type)
							&& indexOf.TryGetValue(pair.Key, out int a))
						{
							edges[a].Add(b);
						}
					}
				}
			}

			// 入度
			var indegree = new int[count];
			foreach (HashSet<int> outgoing in edges)
			{
				foreach (int target in outgoing)
				{
					indegree[target]++;
				}
			}

			var placed = new bool[count];
			var order = new List<int>(count);

			for (int step = 0; step < count; step++)
			{
				int picked = -1;

				// 在「入度已经归零」的里面挑分数最低的 —— 这就是带优先级的拓扑排序
				for (int i = 0; i < count; i++)
				{
					if (placed[i] || indegree[i] != 0)
					{
						continue;
					}

					if (picked < 0 || bosses[i].score < bosses[picked].score)
					{
						picked = i;
					}
				}

				if (picked < 0)
				{
					// 有环:取剩下里分数最低的破环
					for (int i = 0; i < count; i++)
					{
						if (placed[i])
						{
							continue;
						}

						if (picked < 0 || bosses[i].score < bosses[picked].score)
						{
							picked = i;
						}
					}
				}

				if (picked < 0)
				{
					break;
				}

				placed[picked] = true;
				order.Add(picked);

				foreach (int target in edges[picked])
				{
					indegree[target]--;
				}
			}

			// 按拓扑位次重写分数:位次优先,原分数只用来破同层的平局
			for (int rank = 0; rank < order.Count; rank++)
			{
				int i = order[rank];
				bosses[i] = (bosses[i].npc, rank * 1000f + bosses[i].score);
			}
		}

		// —— 推断引擎 ——

		private static List<(int npc, float score)> _inferredBossCache;

		/// <summary>
		/// 推断引擎:没有任何内置顺序表可用时(装的是未知模组包),
		/// 把模组自己的 Boss 按「难度评分」排出一条大概的顺序,追加到原版检查点后面。
		///
		/// 评分用两个信号:
		///   - 生命值(取对数):Boss 越硬,通常出现得越晚
		///   - 防御力
		/// 这是近似,但能让"按时间点排序"在未知模组包里也可用。
		/// </summary>
		public static List<(int npc, float score)> ComputeInferredBosses()
		{
			if (_inferredBossCache != null)
			{
				// 返回**副本**。
				// 调用方(AppendInferredBosses)会拿它去跑召唤依赖放松和排序,
				// 直接返回缓存实例等于让调用方改缓存 —— Build() 一次游戏里跑好几遍,
				// 分数会被反复累加(虽然目前排序看着还稳定,但那是隐患)。
				return new List<(int npc, float score)>(_inferredBossCache);
			}

			var result = new List<(int npc, float score)>();

			NpcIndex.EnsureBuilt();

			foreach (int npcType in NpcIndex.AllTypes)
			{
				try
				{
					// 用图鉴判定「是不是 Boss」,不再 new NPC().SetDefaults()。
					// 那是让它慢到不可用的原因:1200 个 NPC 逐个建实例会把加载拖成几分钟。
					// 图鉴条目里有一个元素类型名带 "Boss",跑起来只是查字典。
					Terraria.GameContent.Bestiary.BestiaryEntry entry =
						Terraria.Main.BestiaryDB?.FindEntryByNPCID(npcType);

					if (entry?.Info == null)
					{
						continue;
					}

					bool isBoss = false;
					int life = 0;
					int defense = 0;

					foreach (Terraria.GameContent.Bestiary.IBestiaryInfoElement element in entry.Info)
					{
						if (element == null)
						{
							continue;
						}

						string typeName = element.GetType().Name;

						if (typeName.Contains("Boss"))
						{
							isBoss = true;
						}
						else if (typeName.Contains("Stats"))
						{
							// NPCStatsReportInfoElement 里存着生命/防御,字段名随版本变,按名字找
							ReadIntField(element, "life", ref life);
							ReadIntField(element, "hp", ref life);
							ReadIntField(element, "defense", ref defense);
						}
					}

					if (!isBoss)
					{
						continue;
					}

					// 已被原版检查点覆盖的 Boss 不重复加
					if (MatchedByVanillaCheckpoint(NpcIndex.GetName(npcType)))
					{
						continue;
					}

					float score = (float)Math.Log10(Math.Max(10, life > 0 ? life : 100));
					score += Math.Min(1.5f, defense / 60f);

					// loot tier:掉落物越贵重 / 越稀有,这个 Boss 通常越晚。
					// 取「掉落里最贵重的那件」当信号 —— 单个 Boss 的普通掉落一大堆,
					// 平均会被拉平,取最大值更能反映它在流程里的位置。
					score += ComputeLootTierScore(npcType);

					result.Add((npcType, score));
				}
				catch
				{
					// 个别 NPC 读不出来就跳过
				}
			}

			result.Sort((a, b) => a.score.CompareTo(b.score));
			_inferredBossCache = result;

			return result;
		}

		/// <summary>
		/// 掉落价值评分。用物品的基础售价(取对数)+ 稀有度,
		/// 权重压得比生命值低,只当辅助信号,免得把「掉一堆破烂的后期 Boss」评低。
		/// </summary>
		private static float ComputeLootTierScore(int npcType)
		{
			float best = 0f;

			try
			{
				foreach ((int item, float rate) in GameDataIndex.GetDrops(npcType))
				{
					if (item <= 0 || item >= ItemLoader.ItemCount)
					{
						continue;
					}

					Item sample;
					try
					{
						sample = ContentSamples.ItemsByType[item];
					}
					catch
					{
						continue;
					}

					if (sample == null)
					{
						continue;
					}

					float value = (float)Math.Log10(Math.Max(10, sample.value));
					float rarity = Math.Min(1.5f, sample.rare / 4f);
					float combined = value + rarity;

					if (combined > best)
					{
						best = combined;
					}
				}
			}
			catch
			{
				// 掉落表读不到时不影响生命值/防御那部分评分
			}

			// 0.35 的权重:满值大概 +3 分,能和生命值拉开档次但仍以生命值为主
			return best * 0.35f;
		}

		public static void ResetInference()
		{
			_inferredBossCache = null;
		}

		/// <summary>
		/// 反射读一个 int 字段:沿着继承链找名字里含 <paramref name="fragment"/> 的 int 字段。
		/// 图鉴统计元素的字段名随版本/本地化变,按名字找最稳。
		/// </summary>
		private static void ReadIntField(object target, string fragment, ref int value)
		{
			if (target == null || value > 0)
			{
				return;
			}

			const System.Reflection.BindingFlags flags =
				System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.Public
				| System.Reflection.BindingFlags.NonPublic;

			for (Type level = target.GetType(); level != null && level != typeof(object); level = level.BaseType)
			{
				foreach (System.Reflection.FieldInfo field in level.GetFields(flags | System.Reflection.BindingFlags.DeclaredOnly))
				{
					if (field.FieldType != typeof(int)
						|| field.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}

					if (field.GetValue(target) is int number && number > 0)
					{
						value = number;
						return;
					}
				}
			}
		}

		/// <summary>这个怪物是不是已经被原版检查点覆盖了(按名字包含判断)。</summary>
		private static bool MatchedByVanillaCheckpoint(string npcName)
		{
			string normalized = Normalize(npcName);
			if (normalized.Length < 4)
			{
				return false;
			}

			foreach (string keyword in VanillaCheckpointKeywords)
			{
				if (normalized.Contains(keyword))
				{
					return true;
				}
			}

			return false;
		}

		private static readonly string[] VanillaCheckpointKeywords =
		{
			"kingslime", "史莱姆王",
			"eyeofcthulhu", "克苏鲁之眼",
			"eaterofworlds", "世界吞噬",
			"brainofcthulhu", "克苏鲁之脑",
			"skeletron", "骷髅王",
			"queenbee", "蜂后",
			"wallofflesh", "血肉墙",
			"destroyer", "毁灭者",
			"retinazer", "双子魔眼",
			"skeletronprime", "机械骷髅王",
			"plantera", "世纪之花",
			"golem", "石巨人",
			"dukefishron", "猪龙鱼",
			"hallowboss", "光之女皇",
			"moonlord", "月亮领主",
			"lunaticcultist", "拜月教",
		};

		/// <summary>
		/// 把「Boss 阶段」和「该 Boss 的掉落物」连起来:
		/// 某个 Boss 的掉落物,时间点就是这个 Boss 所在的阶段。
		/// 这样装了灾厄/Fargo 之后,它们的物品也能落到月总之后的阶段上。
		/// </summary>
		private void AnchorItemsFromBosses()
		{
            _sourceReasons.Clear();
			_bossAnchored.Clear();

			int matchedBosses = 0;
			int anchoredItems = 0;
			var unmatched = new List<string>();

            var npcCheckpoints=Tiers.Where(t=>t.Source!="inferred").SelectMany(t=>t.NpcIds.Select(npc=>(npc,t)))
                .GroupBy(pair=>pair.npc).ToDictionary(group=>group.Key,
                    group=>group.OrderBy(pair=>pair.t.NpcIds.Count).ThenBy(pair=>pair.t.Index).First().t.Index);
			foreach (ProgressionTier tier in Tiers)
			{
				if (tier.Id == "start" || tier.NpcIds.Count==0 && !(tier.Id?.StartsWith("boss_") ?? false))
				{
					continue;
				}

				// 精确路径:BossChecklist 直接给了 npcIDs,不用再拿阶段名去猜。
				// 名字匹配只是没有外部资料时的兜底。
				bool precise = tier.NpcIds != null && tier.NpcIds.Count > 0;
				List<int> npcs = precise
					? new List<int>(tier.NpcIds)
					: MatchNpcsByName(tier.DisplayName);
                if (!precise) tier.NpcIds.AddRange(npcs);

				if (npcs.Count == 0)
				{
					if (unmatched.Count < 8)
					{
						unmatched.Add(tier.DisplayName);
					}

					continue;
				}

				matchedBosses++;
                void AnchorDrop(int type, int gate, int checkpoint=-1)
                {
                    if(type <= 0 || gate < 0) return;
                    var key=ItemKey.Crafted(type);
                    if(checkpoint<0) checkpoint=tier.Index;
                    int available=Math.Max(checkpoint,gate);
                    if(_explicitItemTier.ContainsKey(key)
                        || _bossAnchored.TryGetValue(key,out int old) && old<=available) return;
                    _bossAnchored[key]=available;
                    SourceReason(type,"Boss / 事件掉落：" + Tiers[checkpoint].DisplayName
                        + (gate>checkpoint ? "；附加解锁条件："+Tiers[gate].DisplayName : ""));
                    anchoredItems++;
                }
                var reported=new HashSet<int>();
                if(tier.TreasureBag>0)
                {
                    AnchorDrop(tier.TreasureBag,0);
                    foreach(var drop in GameDataIndex.GetContainerDrops(tier.TreasureBag))
                    {
                        reported.Add(drop.ItemType);
                        AnchorDrop(drop.ItemType,ProgressionSources.DropTier(this,drop,tier.Index));
                    }
                }
                foreach(int npcType in npcs)
                    foreach(var drop in GameDataIndex.GetNpcDropRecords(npcType))
                    {
                        reported.Add(drop.ItemType);
                        int spawn=ProgressionSources.EventNpcGate(this,npcType);
                        int checkpoint=npcCheckpoints.GetValueOrDefault(npcType,tier.Index);
                        int gate=spawn<0 ? -1 : ProgressionSources.DropTier(this,drop,Math.Max(checkpoint,spawn));
                        AnchorDrop(drop.ItemType,gate<0 || spawn<0 ? -1 : Math.Max(gate,spawn),
                            checkpoint);
                    }
                // Checklist metadata lacks per-drop conditions. It must not bypass
                // a gate (or an opaque predicate) already reported by the real loot table.
                foreach(int type in tier.LootItems.Where(type=>!reported.Contains(type))) AnchorDrop(type,0);

			}

			if (matchedBosses > 0)
			{
				if (MEConfig.DiagnosticsEnabled)
				{
					Terraria.ModLoader.ModContent.GetInstance<AE2ForTerraria>()?.Logger.Info(
						"Boss 阶段锚定:" + Tiers.Count + " 个阶段里匹配到 " + matchedBosses
						+ " 个 Boss,锚定 " + anchoredItems + " 件掉落物；NPC 总数="
						+ NpcIndex.AllTypes.Count + "；未匹配示例:" + string.Join(", ", unmatched));
				}
			}
		}

		/// <summary>按名字把阶段名匹配到怪物。中文/英文名和内部分名都试。</summary>
		/// <summary>阶段名和怪物名对不上时的别名(键 = 规范化后的阶段名)。</summary>
		private static readonly Dictionary<string, string[]> BossAliases =
			new Dictionary<string, string[]>
			{
				{ "perforators", new[] { "perforator" } },
				{ "sulphurelementalcalamitasclone", new[] { "calamitasclone", "sulphurelemental" } },
				{ "calamitasclone", new[] { "calamitasclone" } },
				{ "lunaticcultist", new[] { "cultist" } },
				{ "profanedguardians", new[] { "profanedguardian" } },
				{ "exomechs", new[] { "exomech" } },
				{ "leviathanandanahita", new[] { "leviathan", "anahita" } },
				{ "queenslime", new[] { "queenslime" } },
				{ "mutantfargo", new[] { "mutant" } },
				{ "abominationnfargo", new[] { "abominationn" } },
				{ "devianttfargo", new[] { "deviantt" } },
			};

		private static List<int> MatchNpcsByName(string target)
		{
			var result = new List<int>();
			string want = Normalize(target);

			if (want.Length < 4)
			{
				return result;
			}

			// 候选键:整个名字 + 拆开的部分(「A and B」「A & B」「A / B」「A, B」)+ 别名
			var keys = new List<string> { want };

			foreach (string part in target.Split(
				new[] { " and ", " & ", "/", "," }, StringSplitOptions.RemoveEmptyEntries))
			{
				string normalized = Normalize(part);
				if (normalized.Length >= 4 && !keys.Contains(normalized))
				{
					keys.Add(normalized);
				}
			}

			if (BossAliases.TryGetValue(want, out string[] aliases))
			{
				foreach (string alias in aliases)
				{
					if (!keys.Contains(alias))
					{
						keys.Add(alias);
					}
				}
			}

			NpcIndex.EnsureBuilt();

			foreach (int npcType in NpcIndex.AllTypes)
			{
				string localized = Normalize(NpcIndex.GetName(npcType));
				string internalName = Normalize(InternalNpcName(npcType));

				foreach (string key in keys)
				{
					if (key == localized || key == internalName
						|| (localized.Length >= 4 && localized.Contains(key))
						|| (internalName.Length >= 4 && internalName.Contains(key)))
					{
						result.Add(npcType);
						break;
					}
				}
			}

			return result;
		}

		private static string InternalNpcName(int npcType)
		{
			try
			{
				// 模组 NPC 的内部名要从 NPCLoader 取;NPCID.Search 只覆盖原版。
				var modNpc = Terraria.ModLoader.NPCLoader.GetNPC(npcType);
				if (modNpc != null && !string.IsNullOrEmpty(modNpc.Name))
				{
					return modNpc.Name;
				}

				return NPCID.Search.GetName(npcType) ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}

		private static string Normalize(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}

			var sb = new StringBuilder();
			foreach (char c in text)
			{
				if (char.IsLetterOrDigit(c))
				{
					sb.Append(char.ToLowerInvariant(c));
				}
			}

			return sb.ToString();
		}

		public ProgressionTier GetTier(int index)
		{
			return index >= 0 && index < Tiers.Count ? Tiers[index] : null;
		}

        public void ApplyOrder(IEnumerable<string> ids)
        {
            var priorIds = Tiers.Select(t => t.Id).ToArray();
            var byId = Tiers.ToDictionary(t => t.Id);
            var ordered = new List<ProgressionTier>();
            if (byId.Remove("start", out var start)) ordered.Add(start);
            foreach (string id in ids)
                if (byId.Remove(id, out var tier)) ordered.Add(tier);
            foreach (var tier in Tiers)
                if (byId.Remove(tier.Id)) ordered.Add(tier);
            Tiers.Clear(); Tiers.AddRange(ordered);
            for (int i = 0; i < Tiers.Count; i++) Tiers[i].Index = i;
            foreach (var key in _explicitItemTier.Keys.ToArray())
            {
                int old = _explicitItemTier[key];
                if (old >= 0 && old < priorIds.Length) _explicitItemTier[key] = Tiers.FindIndex(t => t.Id == priorIds[old]);
            }
            _tierCacheReady = false; Revision++;
            AnchorItemsFromBosses(); ProgressionSources.Anchor(this, _bossAnchored);
        }
        public bool HasUnlocks(int tier)
        {
            EnsureTierCache();
            return tier >= 0 && tier < _effectiveTiers.Length && _effectiveTiers[tier];
        }
        public int GetDisplayTierIndex(int tier)
        {
            if(tier < 0 || tier >= Tiers.Count) return UnknownTier;
            EnsureTierCache();
            while(tier > 0 && !_effectiveTiers[tier]) tier--;
            return tier;
        }
        public string GetIntervalName(int index)
        {
            if (index < 0 || index >= Tiers.Count) return "阶段未知";
            EnsureTierCache();
            string Clean(string name) => name.EndsWith("后") ? name[..^1] : name;
            string current = index == 0 ? "Boss 前" : Clean(Tiers[index].DisplayName) + "后";
            int next = index + 1;
            while(next < Tiers.Count && !_effectiveTiers[next]) next++;
            if(next < Tiers.Count) current += "（" + Clean(Tiers[next].DisplayName) + "前）";
            if (Tiers[index].Source == "inferred") current += " · 推测";
            return current;
        }
        public string GetExplanation(int type)
        {
            int tier = GetTierIndex(ItemKey.Crafted(type));
            return GetIntervalName(tier) + "\n" + (tier < 0 ? "尚无可确认的公开解锁依据" : _reasons.GetValueOrDefault(type, "显式阶段设置"));
        }

		public string GetTierName(int index)
		{
			ProgressionTier tier = GetTier(index);
            if(tier==null) return "未确定阶段";
            string name=tier.DisplayName+(tier.Source=="inferred" ? "（推测）" : "");
            try { if(tier.IsDowned?.Invoke()==true) name+=" · 已击败"; }
            catch(Exception ex) { if(MEConfig.DiagnosticsEnabled) AE2ForTerraria.Instance.Logger.Warn("Boss stage status failed: "+tier.Id+" "+ex.Message); }
            return name;
		}

		/// <summary>显式锚定:知道物品从哪来(某 Boss 掉落、某事件)时直接指定阶段。</summary>
		public void AnchorItem(ItemKey key, string tierId)
		{
			int index = Tiers.FindIndex(t => t.Id == tierId);
			if (index < 0)
			{
				return;
			}

			_explicitItemTier[key] = index;
			_inferredCache.Clear(); _tierCacheReady=false;
		}

		/// <summary>
		/// 取物品时间点。优先显式锚定,否则走配方图推导(瓶颈继承):
		/// tier(物品) = min over 配方( max over 原料( tier(原料) ) )
		/// 也就是「随便哪条配方,只要它的原料都到手了,这物品就能拿到」的最早时点。
		/// 推导不出返回 UnknownTier。
		/// </summary>
        public int GetTierIndex(ItemKey key)
        {
            EnsureTierCache();
            return _inferredCache.GetValueOrDefault(ItemKey.Crafted(key.Type),UnknownTier);
        }

        private void EnsureTierCache()
        {
            if(_tierCacheReady && _tierRecipeRevision==_recipes.Revision) return;
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            _inferredCache.Clear(); _reasons.Clear(); _tierCacheReady=false;
            foreach(var entry in _bossAnchored)
            {
                _inferredCache[entry.Key]=entry.Value;
                _reasons[entry.Key.Type] = _sourceReasons.GetValueOrDefault(entry.Key.Type,
                    entry.Value == 0 ? "基础资源" : "Boss / 事件掉落：" + Tiers[entry.Value].DisplayName);
            }
            foreach(var entry in _explicitItemTier) _inferredCache[entry.Key]=entry.Value;
            var stationUsers=new Dictionary<int,List<int>>();
            var queue=new Queue<int>();var queued=new bool[Recipe.numRecipes];
            void Enqueue(int index)
            {if(index>=0 && index<queued.Length && !queued[index]){queued[index]=true;queue.Enqueue(index);}}
            for(int index=0;index<Recipe.numRecipes;index++)
            {
                var recipe=Main.recipe[index];
                if(recipe==null || recipe.Disabled || recipe.createItem.IsAir) continue;
                foreach(int tile in recipe.requiredTile)
                    if(tile>=0)
                    {if(!stationUsers.TryGetValue(tile,out var list)) stationUsers[tile]=list=new();list.Add(index);}
                Enqueue(index);
            }
            var acquisitionRoutes = ProgressionWorldSources.Routes(this);
            var sourceUsers = new Dictionary<int,List<ProgressionWorldSources.AcquisitionRoute>>();
            foreach(var route in acquisitionRoutes)
                foreach(int input in route.Inputs.Distinct())
                {
                    if(!sourceUsers.TryGetValue(input,out var users)) sourceUsers[input]=users=new();
                    users.Add(route);
                }
            var sourceQueue = new Queue<int>(); var sourceQueued = new HashSet<int>();
            void EnqueueSource(int type)
            {
                if(sourceUsers.ContainsKey(type) && sourceQueued.Add(type)) sourceQueue.Enqueue(type);
            }
            void Notify(int type)
            {
                EnqueueSource(type);
                foreach(int dependent in _recipes.GetRecipesUsing(type)) Enqueue(dependent);
                if(ContentSamples.ItemsByType.TryGetValue(type,out var item)
                    && stationUsers.TryGetValue(item.createTile,out var consumers))
                    foreach(int dependent in consumers) Enqueue(dependent);
            }
            var mining = ProgressionSources.MiningRoutes(this);
            var picks = ContentSamples.ItemsByType.Values.Where(i => !i.IsAir && i.pick > 0).ToArray();
            void UpdateMining()
            {
                foreach(var route in mining)
                {
                    if (route.Gate < 0 || _explicitItemTier.ContainsKey(ItemKey.Crafted(route.Item))) continue;
                    int best = -1, tool = 0;
                    foreach(var pick in picks)
                        if(pick.pick >= route.Pick && _inferredCache.TryGetValue(ItemKey.Crafted(pick.type),out int value)
                            && (best < 0 || value < best)) { best = value; tool = pick.type; }
                    if(best < 0) continue;
                    best = Math.Max(best,route.Gate);
                    if(route.Conditions != null) best = AdaptiveProgressionConditions.Earliest(this,route.Conditions,best);
                    if(best < 0) continue;
                    var key = ItemKey.Crafted(route.Item);
                    if(_inferredCache.TryGetValue(key,out int previous) && previous <= best) continue;
                    _inferredCache[key] = best;
                    _reasons[route.Item] = route.Reason + "；镐力要求 " + route.Pick + "，可用 " + Lang.GetItemNameValue(tool);
                    Notify(route.Item);
                }
            }
            UpdateMining();
            foreach(var key in _inferredCache.Keys.ToArray()) EnqueueSource(key.Type);
            while(queue.Count>0 || sourceQueue.Count>0)
            {
                if(sourceQueue.Count>0)
                {
                    int source=sourceQueue.Dequeue();sourceQueued.Remove(source);
                    foreach(var route in sourceUsers[source])
                    {
                        var key=ItemKey.Crafted(route.Output);
                        if(_explicitItemTier.ContainsKey(key) || route.Gate<0) continue;
                        int available=route.Gate;
                        foreach(int input in route.Inputs)
                        {
                            if(!_inferredCache.TryGetValue(ItemKey.Crafted(input),out int inputTier)) { available=-1; break; }
                            available=Math.Max(available,inputTier);
                        }
                        if (available >= 0 && route.Conditions != null)
                            available=ProgressionSources.ConditionsTier(this,route.Conditions,available);
                        if (available >= 0 && route.Drop != null)
                            available=ProgressionSources.DropTier(this,route.Drop,available);
                        if(available<0 || _inferredCache.TryGetValue(key,out int prior) && prior<=available) continue;
                        _inferredCache[key]=available;
                        _reasons[key.Type]=route.Reason+"："+string.Join("、",route.Inputs.Select(Lang.GetItemNameValue));
                        Notify(key.Type);
                        if(ContentSamples.ItemsByType.TryGetValue(key.Type,out var outputItem) && outputItem.pick>0) UpdateMining();
                    }
                    continue;
                }
                int index=queue.Dequeue();queued[index]=false;var recipe=Main.recipe[index];
                var output=ItemKey.Crafted(recipe.createItem.type);
                if(_explicitItemTier.ContainsKey(output)) continue;
                int candidate=RecipeTier(recipe);
                if(candidate<0 || _inferredCache.TryGetValue(output,out int old) && old<=candidate) continue;
                _inferredCache[output]=candidate;
                _reasons[output.Type] = RecipeReason(recipe, candidate);
                Notify(output.Type);
                if(ContentSamples.ItemsByType.TryGetValue(output.Type,out var crafted) && crafted.pick > 0) UpdateMining();
            }
            _effectiveTiers = new bool[Tiers.Count];
            if(_effectiveTiers.Length > 0) _effectiveTiers[0] = true;
            foreach(int tier in _inferredCache.Values)
                if(tier >= 0 && tier < _effectiveTiers.Length) _effectiveTiers[tier] = true;
            // A new recipe route still matters when its output was obtainable earlier by exploration or drops.
            foreach(var recipe in Main.recipe.Take(Recipe.numRecipes))
            {
                if(recipe == null || recipe.Disabled || recipe.createItem.IsAir) continue;
                int tier = RecipeTier(recipe);
                if(tier >= 0 && tier < _effectiveTiers.Length) _effectiveTiers[tier] = true;
            }
            _tierCacheReady=true;_tierRecipeRevision=_recipes.Revision;
            TierBuildMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            RankedItemCount=_inferredCache.Count;
        }

        private string RecipeReason(Recipe recipe, int tier)
        {
            foreach(var material in recipe.requiredItem)
            {
                if(material.IsAir || !_inferredCache.TryGetValue(ItemKey.Crafted(material.type),out int value) || value != tier) continue;
                string source = _reasons.GetValueOrDefault(material.type, "材料阶段");
                if(source.Length > 140) source = source[..140] + "…";
                return "配方材料：" + Lang.GetItemNameValue(material.type) + "；" + source;
            }
            return "配方材料、工作站及环境条件的最早共同阶段";
        }

        private int RecipeTier(Recipe recipe)
        {
            int tier=0;
            foreach(int tile in recipe.requiredTile)
            {
                if(tile<0) continue;
                int station=ProgressionSources.NaturalStationTier(this,tile);
                if(_stationItems.TryGetValue(tile,out var items))
                    foreach(int type in items)
                        if(_inferredCache.TryGetValue(ItemKey.Crafted(type),out int value) && (station<0 || value<station)) station=value;
                if(station<0)return UnknownTier;
                tier=Math.Max(tier,station);
            }
            foreach(var ingredient in recipe.requiredItem)
            {
                if(ingredient.IsAir) continue;
                int material=_inferredCache.GetValueOrDefault(ItemKey.Crafted(ingredient.type),UnknownTier);
                foreach(int groupId in recipe.acceptedGroups)
                    if(RecipeGroup.recipeGroups.TryGetValue(groupId,out var group) && group.ValidItems.Contains(ingredient.type))
                        foreach(int type in group.ValidItems)
                            if(_inferredCache.TryGetValue(ItemKey.Crafted(type),out int value) && (material<0 || value<material)) material=value;
                if(material<0)return UnknownTier;
                tier=Math.Max(tier,material);
            }
            return ProgressionSources.ConditionsTier(this,recipe.Conditions,tier);
        }

		/// <summary>当前世界进度所处的阶段序号。</summary>
		public int CurrentTierIndex()
		{
            int highest=0;
            foreach(var tier in Tiers)
                try {if(tier.IsDowned?.Invoke()==true) highest=Math.Max(highest,tier.Index);}
                catch(Exception ex) {if(MEConfig.DiagnosticsEnabled) AE2ForTerraria.Instance.Logger.Warn("Boss downed predicate failed: "+tier.Id+" "+ex.Message);}
            return highest;
		}

		/// <summary>
		/// 补做一次 Boss 锚定。
		/// 加载阶段模组 NPC 的名字有时还拿不到(服务端尤其明显),
		/// 所以首次打开界面时把 NPC 列表重建一次再锚一遍。
		/// </summary>
		public void ReanchorBosses()
		{
			NpcIndex.Reset();
			NpcIndex.EnsureBuilt();
			AnchorItemsFromBosses();
            ProgressionSources.Anchor(this,_bossAnchored);
			_inferredCache.Clear(); _tierCacheReady=false;
		}
	}
}
