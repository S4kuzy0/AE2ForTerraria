using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Acquisition;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 从游戏自身的数据表里抽取「来源 / 用途」:
	/// 微光转化、怪物掉落、NPC 商店、Boss 召唤物。
	/// 这些都在加载时一次性扫完并缓存,查询时是纯查表。
	/// </summary>
	public static class GameDataIndex
	{
		public static bool Built { get; private set; }

		/// <summary>物品 -> 掉落它的怪物(类型 + 掉落率)。</summary>
		private static readonly Dictionary<int, List<DropRecord>> DropSources =
			new Dictionary<int, List<DropRecord>>();

		/// <summary>怪物 -> 掉落物(类型 + 掉落率)。</summary>
		private static readonly Dictionary<int, List<DropRecord>> NpcDrops =
			new Dictionary<int, List<DropRecord>>();

		/// <summary>商店条目数(用于日志验证)。</summary>
		public static int ShopEntryCount { get; private set; }

		private static readonly Dictionary<int, List<DropRecord>> ContainerDrops = new();
        private static readonly Dictionary<int, List<DropRecord>> ContainerSources = new();
        private static readonly HashSet<string> IndexedDropTexts = new();
        internal static bool IsIndexedDropText(string text) => IndexedDropTexts.Contains(text);
        internal static IReadOnlyList<DropRecord> GetNpcDropRecords(int npc) => NpcDrops.TryGetValue(npc, out var list) ? list : Array.Empty<DropRecord>();
        internal static IReadOnlyList<DropRecord> GetContainerDrops(int item) => ContainerDrops.TryGetValue(item, out var list) ? list : Array.Empty<DropRecord>();
        internal static IReadOnlyList<DropRecord> GetContainerSources(int item) => ContainerSources.TryGetValue(item, out var list) ? list : Array.Empty<DropRecord>();
        public static void Reset()
		{
			Built = false; ContainerDrops.Clear(); ContainerSources.Clear(); IndexedDropTexts.Clear();
			DropSources.Clear();
			NpcDrops.Clear();

			// 统计量和一次性诊断标记也要清,否则重载后统计会带上上一轮的数,
			// 图鉴 / 商店的诊断日志也不会再打。
			ShopEntryCount = 0;
			SpawnConditionNpcCount = 0;
			ContainerCount = 0;
			ShopMethodName = "(未找到)";
			_bestiaryDiagnosed = false;
		}

		public static void Build(AcquisitionRegistry registry, bool force = false)
		{
			if ((Built && !force) || registry == null)
			{
				return;
			}

			DropSources.Clear(); NpcDrops.Clear(); ContainerDrops.Clear(); ContainerSources.Clear(); IndexedDropTexts.Clear();
            Built = false;
            var destination = registry;
            registry = new AcquisitionRegistry();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            void Read(string source, Action<AcquisitionRegistry> build)
            {
                try { build(registry); }
                catch (Exception ex) { AE2ForTerraria.Instance?.Logger.Warn("Acquisition index failed for " + source + "; other sources retained: " + ex); }
            }
			Read("shimmer", BuildShimmer);
			Read("NPC drops", BuildNpcDrops);
			Read("containers", BuildContainers);
			Read("spawn conditions", BuildSpawnConditions);
			ShopEntryCount = 0;
			Read("shops", BuildShopsFromDatabase);
			Read("vanilla summons", data => BuildSummonItems(data, true));
			Read("mod summons", data => BuildSummonItems(data, false));
			Read("crafting", BuildCraftKinds);
			Read("biomes", BuildBiomeKinds);
            destination.ReplaceDerived("game-data", registry);

			watch.Stop();

			int withDrops = 0;
			foreach (KeyValuePair<int, List<DropRecord>> entry in NpcDrops)
			{
				if (entry.Value.Count > 0)
				{
					withDrops++;
				}
			}

			Log("来源/用途索引" + (force ? "(补建)" : "(初建)") + "完成:新增 "
				+ registry.SourceCount + " 条来源,"
				+ "覆盖 " + withDrops + " 个有掉落的怪物,"
				+ DropSources.Count + " 个可从掉落获得的物品,"
				+ registry.ShimmerRecipeCount + " 条微光,"
				+ ShopEntryCount + " 条商店[" + ShopMethodName + "],"
				+ ContainerCount + " 个容器,耗时 " + watch.ElapsedMilliseconds + "ms");
			Log("刷新条件:" + SpawnConditionNpcCount + " 个怪物有图鉴条件");

			Built = true;
		}

		/// <summary>
		/// 常驻诊断日志的开关。默认不打 —— 这些统计每次启动都刷一遍太吵,
		/// 需要排查时在模组配置里打开「详细日志」再看。
		/// </summary>
		private static void Log(string message)
		{
			if (!MEConfig.DiagnosticsEnabled)
			{
				return;
			}

			Terraria.ModLoader.ModContent.GetInstance<AE2ForTerraria>()?.Logger.Info(message);
		}

		/// <summary>
		/// 延迟构建。不在 PostSetupContent 里建,是因为那时部分模组还没把自己的
		/// 掉落规则/商店注册完(实测会漏掉一大批模组掉落物)。
		/// 放到第一次打开界面时建,数据才是完整的。
		/// </summary>
		public static void EnsureBuilt()
		{
			var registry = StorageSystem.Acquisition;
			if (registry == null)
			{
				return;
			}

			// 只建一次,而且只在「第一次真正需要」时建:
			// 这时所有模组的掉落规则 / 商店 / NPC 名字都已经注册完,
			// 拿到的就是完整数据(以前装载阶段白建一次,进世界再整份重建)。
			if (!Built)
			{
				Build(registry, true);

				// 顺便把整张进度图重建一遍:
				// ① NPC 名字这时才齐全(Boss 锚定需要)
				// ② BossChecklist 的顺序数据这时才拿到(它要求 PostAddRecipes 之后)
				StorageSystem.Progression?.Build();
			}
		}

		// —— 微光 ——

		private static void BuildShimmer(AcquisitionRegistry registry)
		{
			try
			{
				int[] table = ItemID.Sets.ShimmerTransformToItem;
				if (table == null)
				{
					return;
				}

				for (int input = 1; input < table.Length; input++)
				{
					int output = table[input];
					if (output <= 0 || output == input)
					{
						continue;
					}

					registry.RegisterShimmer(input, output);
					registry.RegisterSource(ItemKey.Crafted(output),
						ModText.Format("SrcShimmerFrom", "微光转化自「{0}」", Name(input)));
					registry.RegisterUse(ItemKey.Crafted(input),
						ModText.Format("UseShimmerTo", "投入微光 → 「{0}」", Name(output)));
					registry.RegisterIfAbsent(ItemKey.Crafted(output), AcquisitionKind.Shimmer,
						ModText.Format("SrcShimmerFrom", "微光转化自「{0}」", Name(input)));
				}
			}
			catch
			{
				// 这个字段在某些版本/其它模组改动下可能不可用,静默跳过
			}
		}

		// —— 怪物掉落 ——

		private static void BuildNpcDrops(AcquisitionRegistry registry)
		{
			int total = NPCLoader.NPCCount;

            // Vanilla variants such as Pinky have negative net IDs and their
            // own loot rules. Enumerating positive NPC types silently loses them.
			foreach (int npcType in Enumerable.Range(1,total-1).Concat(ContentSamples.NpcsByNetId.Keys.Where(id=>id<0)))
			{
                string npcName = npcType<0 && ContentSamples.NpcsByNetId.TryGetValue(npcType,out var variant)
                    ? variant.FullName : NpcIndex.GetName(npcType);
				if (string.IsNullOrWhiteSpace(npcName))
				{
					continue;
				}

				List<DropRateInfo> drops = CollectDrops(npcType);
				if (drops.Count == 0)
				{
					continue;
				}

				foreach (DropRateInfo drop in drops)
				{
					if (drop.itemId <= 0 || drop.itemId >= ItemLoader.ItemCount)
					{
						continue;
					}

					var record = DropRecord.From(drop, npc: npcType);

					if (!NpcDrops.TryGetValue(npcType, out List<DropRecord> list))
					{
						list = new List<DropRecord>();
						NpcDrops[npcType] = list;
					}

					list.Add(record);

					if (!DropSources.TryGetValue(drop.itemId, out List<DropRecord> from))
					{
						from = new List<DropRecord>();
						DropSources[drop.itemId] = from;
					}

					from.Add(record);
                    string description = "掉落自「" + npcName + "」" + record.Summary
                        + (record.Conditions.Count > 0 ? " · " + string.Join(" / ", record.ConditionDescriptions()) : "");
                    registry.RegisterSource(ItemKey.Crafted(drop.itemId), description);
                    IndexedDropTexts.Add(description);
					registry.RegisterIfAbsent(ItemKey.Crafted(drop.itemId), AcquisitionKind.NpcDrop,
						ModText.Format("SrcDropFrom", "掉落自「{0}」", npcName));

					// 事件掉落:按掉落者的名字判断(血月 / 日食 / 南瓜月 / 入侵 …)
					string eventName = MatchEventName(npcName);
					if (eventName != null)
					{
						registry.RegisterIfAbsent(ItemKey.Crafted(drop.itemId),
							AcquisitionKind.Event,
							ModText.Format("SrcEventDrop", "事件掉落({0})", eventName));
					}
				}
			}
		}

		private static List<DropRateInfo> CollectDrops(int npcType)
		{
			var result = new List<DropRateInfo>();

			try
			{
				List<IItemDropRule> rules = Main.ItemDropsDB.GetRulesForNPCID(npcType);
				if (rules == null)
				{
					return result;
				}

				var feed = new DropRateInfoChainFeed(1f);
				foreach (IItemDropRule rule in rules)
				{
					if (rule == null)
					{
						continue;
					}

					try
					{
						rule.ReportDroprates(result, feed);
					}
					catch (Exception ex)
                    { AE2ForTerraria.Instance.Logger.Warn("Drop rule report failed: " + rule.GetType().FullName + " " + ex.Message); }
				}
			}
			catch (Exception ex)
            { AE2ForTerraria.Instance.Logger.Warn("Drop table read failed: " + ex.Message); }

			return result;
		}

        internal static IReadOnlyList<DropRecord> GetNpcSources(int type) =>
            DropSources.TryGetValue(type, out var result) ? result : Array.Empty<DropRecord>();
        public static IReadOnlyList<(int item, float rate)> GetDrops(int npcType) =>
            GetNpcDropRecords(npcType).Select(d => (d.ItemType, d.Probability.HasValue ? d.Probability.Value * 100 : float.NaN)).ToArray();

		// —— 宝藏袋 / 宝箱 ——
		// —— 怪物刷新条件 ——

		/// <summary>登记过刷新条件的怪物数量(日志用)。</summary>
		public static int SpawnConditionNpcCount { get; private set; }

		/// <summary>
		/// 图鉴信息元素里的说明文字是 LocalizedText,不同元素字段名不一样,
		/// 所以用反射找任意一个是 LocalizedText 的字段/属性读出来。
		/// </summary>
		private static string TryReadBestiaryText(object element)
		{
			if (element == null)
			{
				return null;
			}

			Type type = element.GetType();
			const System.Reflection.BindingFlags flags =
				System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.Public
				| System.Reflection.BindingFlags.NonPublic;

			try
			{
				// 注意:GetFields 不会返回基类的私有字段,所以要自己沿继承链往上走。
				for (Type level = type; level != null && level != typeof(object); level = level.BaseType)
				{
					foreach (System.Reflection.FieldInfo field in level.GetFields(flags | System.Reflection.BindingFlags.DeclaredOnly))
					{
						if (field.FieldType != typeof(Terraria.Localization.LocalizedText))
						{
							continue;
						}

						if (field.GetValue(element) is Terraria.Localization.LocalizedText text)
						{
							string value = text.Value;
							if (!string.IsNullOrWhiteSpace(value))
							{
								return value;
							}
						}
					}

					foreach (System.Reflection.PropertyInfo property in level.GetProperties(flags | System.Reflection.BindingFlags.DeclaredOnly))
					{
						if (property.PropertyType != typeof(Terraria.Localization.LocalizedText))
						{
							continue;
						}

						if (property.GetValue(element) is Terraria.Localization.LocalizedText text)
						{
							string value = text.Value;
							if (!string.IsNullOrWhiteSpace(value))
							{
								return value;
							}
						}
					}

					// 有些元素不是用 LocalizedText 存文字,而是暴露一个 GetDisplayName()
					System.Reflection.MethodInfo getter = level.GetMethod(
						"GetDisplayName", flags | System.Reflection.BindingFlags.DeclaredOnly,
						null, Type.EmptyTypes, null);

					if (getter != null && getter.ReturnType == typeof(string))
					{
						if (getter.Invoke(element, null) is string display && !string.IsNullOrWhiteSpace(display))
						{
							return display;
						}
					}
				}
			}
			catch
			{
				// 读不出来就算了
			}

			return null;
		}

		/// <summary>
		/// 怪物刷新条件。泰拉瑞亚的生成逻辑是代码不是数据表,
		/// 但图鉴(Bestiary)里每个怪物都带了一组「条件图标」(生态 / 时间 / 稀有 / 事件),
		/// 这里把它们的显示名取出来当刷新条件。
		/// </summary>
		private static void BuildSpawnConditions(AcquisitionRegistry registry)
		{
			SpawnConditionNpcCount = 0;

			if (Main.BestiaryDB == null)
			{
				LogBestiaryDiagnostic("BestiaryDB 为 null");
				return;
			}

			foreach (int npcType in NpcIndex.AllTypes)
			{
				try
				{
					Terraria.GameContent.Bestiary.BestiaryEntry entry =
						Main.BestiaryDB.FindEntryByNPCID(npcType);

					if (entry?.Info == null)
					{
						continue;
					}

					var parts = new List<string>();

					foreach (Terraria.GameContent.Bestiary.IBestiaryInfoElement element in entry.Info)
					{
						if (element == null)
						{
							continue;
						}

						// 只要「刷新条件」那一类元素,别把风味文字/属性面板/掉落图标也算进来
						if (!element.GetType().Name.Contains("SpawnCondition"))
						{
							continue;
						}

						string text = TryReadBestiaryText(element);
						if (!string.IsNullOrWhiteSpace(text) && !parts.Contains(text))
						{
							parts.Add(text);
						}
					}

					if (parts.Count > 0)
					{
						registry.RegisterSpawnCondition(npcType, string.Join(" · ", parts));
						SpawnConditionNpcCount++;
					}
				}
				catch
				{
					// 个别怪物没有图鉴条目,跳过
				}
			}

			if (SpawnConditionNpcCount == 0)
			{
				LogBestiaryDiagnostic(null);
			}
		}

		/// <summary>一次性诊断:图鉴里到底有没有可读的条件文字。</summary>
		private static bool _bestiaryDiagnosed;

		private static void LogBestiaryDiagnostic(string reason)
		{
			if (_bestiaryDiagnosed)
			{
				return;
			}

			_bestiaryDiagnosed = true;

			var lines = new List<string>();
			lines.Add("原因:" + (reason ?? "没读到任何文字"));

			int checkedNpcs = 0;

			foreach (int npcType in NpcIndex.AllTypes)
			{
				if (checkedNpcs >= 3)
				{
					break;
				}

				try
				{
					Terraria.GameContent.Bestiary.BestiaryEntry entry =
						Main.BestiaryDB?.FindEntryByNPCID(npcType);

					string npcName = NpcIndex.GetName(npcType);

					if (entry == null)
					{
						lines.Add(npcName + ": 无图鉴条目");
					}
					else
					{
						int count = entry.Info?.Count ?? 0;
						var types = new List<string>();

						if (entry.Info != null)
						{
							foreach (var element in entry.Info)
							{
								types.Add(element == null ? "null" : element.GetType().Name);
							}
						}

						lines.Add(npcName + ": 条目 " + count + " 个 -> " + string.Join(", ", types));
					}
				}
				catch (Exception ex)
				{
					lines.Add("读取异常:" + ex.GetType().Name);
				}

				checkedNpcs++;
			}

			Log("图鉴诊断:\n" + string.Join("\n", lines));
		}

		// —— Craft / Biome 两类获取途径 ——

		/// <summary>事件关键字(用于判断某个怪物的掉落算不算「事件掉落」)。</summary>
		private static readonly (string keyword, string display)[] EventKeywords =
		{
			("blood moon", "血月"), ("血月", "血月"),
			("eclipse", "日食"), ("日食", "日食"),
			("pumpkin moon", "南瓜月"), ("南瓜", "南瓜月"),
			("frost moon", "霜月"), ("霜月", "霜月"),
			("goblin", "哥布林入侵"), ("哥布林", "哥布林入侵"),
			("pirate", "海盗入侵"), ("海盗", "海盗入侵"),
			("martian", "火星暴乱"), ("火星", "火星暴乱"),
			("frost legion", "雪人军团"), ("雪人", "雪人军团"),
			("old one", "撒旦军队"), ("撒旦", "撒旦军队"),
			("slime rain", "史莱姆雨"), ("史莱姆雨", "史莱姆雨"),
			("sandstorm", "沙尘暴"), ("沙尘暴", "沙尘暴"),
		};

		private static string MatchEventName(string npcName)
		{
			if (string.IsNullOrWhiteSpace(npcName))
			{
				return null;
			}

			string lower = npcName.ToLowerInvariant();

			foreach ((string keyword, string display) in EventKeywords)
			{
				if (lower.Contains(keyword))
				{
					return display;
				}
			}

			return null;
		}

		/// <summary>
		/// Craft:能通过配方做出来的物品。
		/// 以前 AcquisitionKind.Craft 一次都没被登记过。
		/// </summary>
		private static void BuildCraftKinds(AcquisitionRegistry registry)
		{
			RecipeIndex recipes = StorageSystem.Recipes;
			if (recipes == null)
			{
				return;
			}

			int total = ItemLoader.ItemCount;
			for (int type = 1; type < total; type++)
			{
				if (recipes.GetRecipesFor(ItemKey.Crafted(type)).Count > 0)
				{
					registry.RegisterIfAbsent(ItemKey.Crafted(type), AcquisitionKind.Craft,
						ModText.Get("SrcCraftable", "可以合成"));
				}
			}
		}

		/// <summary>
		/// Biome:按名字里带的生物群系关键字登记(启发式 ——
		/// 游戏没把「生成在哪个群系」暴露成可读数据,原因见地图鉴那一段)。
		/// </summary>
		private static readonly (string keyword, string display)[] BiomeKeywords =
		{
			("jungle", "丛林"), ("丛林", "丛林"),
			("snow", "雪原"), ("ice", "雪原"), ("雪", "雪原"),
			("desert", "沙漠"), ("沙漠", "沙漠"),
			("ocean", "海洋"), ("海洋", "海洋"),
			("underworld", "地狱"), ("hellstone", "地狱"), ("地狱", "地狱"),
			("hallow", "神圣"), ("pearl", "神圣"), ("神圣", "神圣"),
			("corrupt", "腐化"), ("腐化", "腐化"),
			("crimson", "猩红"), ("猩红", "猩红"),
			("mushroom", "蘑菇"), ("蘑菇", "蘑菇"),
			("granite", "花岗岩"), ("marble", "大理石"),
		};

		private static void BuildBiomeKinds(AcquisitionRegistry registry)
		{
			int total = ItemLoader.ItemCount;

			for (int type = 1; type < total; type++)
			{
				string name = Lang.GetItemNameValue(type);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				string lower = name.ToLowerInvariant();

				foreach ((string keyword, string display) in BiomeKeywords)
				{
					if (lower.Contains(keyword))
					{
						registry.RegisterIfAbsent(ItemKey.Crafted(type), AcquisitionKind.Biome,
							ModText.Format("SrcBiome", "生物群系产出({0})", display));
						break;
					}
				}
			}
		}

		/// <summary>登记过内容的容器数量(日志用)。</summary>
		public static int ContainerCount { get; private set; }

		/// <summary>
		/// 宝藏袋 / 宝箱:用掉落表读出「打开它能开出什么」。
		/// 用实际注册的物品掉落规则识别容器,不依赖名称或翻译。
		/// </summary>
		private static void BuildContainers(AcquisitionRegistry registry)
		{
			ContainerCount = 0;
			int total = ItemLoader.ItemCount;

			for (int itemType = 1; itemType < total; itemType++)
			{
				IReadOnlyList<DropRateInfo> contents = CollectItemRules(itemType);
				if (contents.Count == 0)
				{
					continue;
				}
				string name = Lang.GetItemNameValue(itemType);
				if (string.IsNullOrWhiteSpace(name)) name = ItemLoader.GetItem(itemType)?.FullName ?? "#" + itemType;

				ContainerCount++;
				var containerKey = ItemKey.Crafted(itemType);

				// 顺便把内容填进「宝藏袋」专用结构(以前这两个方法一直没人调用)
				var contentIds = new List<int>();
				foreach (DropRateInfo entry in contents)
				{
					if (entry.itemId > 0 && entry.itemId < total && !contentIds.Contains(entry.itemId))
					{
						contentIds.Add(entry.itemId);
					}
				}

				registry.RegisterTreasureBag(itemType, contentIds);

				// 容器自身也标记一下获取类别
				registry.RegisterIfAbsent(containerKey, AcquisitionKind.TreasureBag,
					ModText.Get("SrcContainer", "可开启的袋子 / 宝箱"));

				foreach (DropRateInfo drop in contents)
				{
					if (drop.itemId <= 0 || drop.itemId >= total)
					{
						continue;
					}

					var record = DropRecord.From(drop, container: itemType);
                    if (!ContainerDrops.TryGetValue(itemType, out var records)) ContainerDrops[itemType] = records = new();
                    if (!ContainerSources.TryGetValue(drop.itemId, out var sources)) ContainerSources[drop.itemId] = sources = new();
                    records.Add(record); sources.Add(record);

                    string description = "开启「" + name + "」获得 " + record.Summary
                        + (record.Conditions.Count > 0 ? " · " + string.Join(" / ", record.ConditionDescriptions()) : "");
                    registry.RegisterSource(ItemKey.Crafted(drop.itemId), description);
                    IndexedDropTexts.Add(description);
					registry.RegisterIfAbsent(ItemKey.Crafted(drop.itemId),
						AcquisitionKind.TreasureBag,
						ModText.Format("SrcBagOpen", "开启「{0}」获得", name));
				}
			}
		}

		private static IReadOnlyList<DropRateInfo> CollectItemRules(int itemType)
		{
			List<DropRateInfo> result = null;

			try
			{
				List<IItemDropRule> rules = Main.ItemDropsDB.GetRulesForItemID(itemType);
				if (rules == null || rules.Count == 0)
				{
					return Array.Empty<DropRateInfo>();
				}
				result = new List<DropRateInfo>();

				var feed = new DropRateInfoChainFeed(1f);
				foreach (IItemDropRule rule in rules)
				{
					if (rule == null)
					{
						continue;
					}

					try
					{
						rule.ReportDroprates(result, feed);
					}
					catch (Exception ex)
                    { AE2ForTerraria.Instance.Logger.Warn("Drop rule report failed: " + rule.GetType().FullName + " " + ex.Message); }
				}
			}
			catch (Exception ex)
            { AE2ForTerraria.Instance.Logger.Warn("Drop table read failed: " + ex.Message); }

			return result ?? (IReadOnlyList<DropRateInfo>)Array.Empty<DropRateInfo>();
		}

		// —— NPC 商店 ——


		private static string FormatPrice(int copperValue)
		{
			if (copperValue <= 0)
			{
				return ModText.Get("PriceUnknown", "(价格未知)");
			}

			if (copperValue >= 10000)
			{
				return ModText.Format("PriceGold", "{0} 金",
					(copperValue / 10000f).ToString("0.##"));
			}

			if (copperValue >= 100)
			{
				return ModText.Format("PriceSilver", "{0} 银",
					(copperValue / 100f).ToString("0.##"));
			}

			return ModText.Format("PriceCopper", "{0} 铜", copperValue);
		}

		// —— Boss 召唤物 ——

		private static readonly (int item, int npc)[] VanillaSummons =
		{
			(ItemID.SlimeCrown, NPCID.KingSlime),
			(ItemID.SuspiciousLookingEye, NPCID.EyeofCthulhu),
			(ItemID.WormFood, NPCID.EaterofWorldsHead),
			(ItemID.BloodySpine, NPCID.BrainofCthulhu),
			(ItemID.Abeemination, NPCID.QueenBee),
			(ItemID.ClothierVoodooDoll, NPCID.SkeletronHead),
			(ItemID.GuideVoodooDoll, NPCID.WallofFlesh),
			(ItemID.DeerThing, NPCID.Deerclops),
			(ItemID.QueenSlimeCrystal, NPCID.QueenSlimeBoss),
			(ItemID.MechanicalEye, NPCID.Retinazer),
			(ItemID.MechanicalWorm, NPCID.TheDestroyer),
			(ItemID.MechanicalSkull, NPCID.SkeletronPrime),
			(ItemID.LihzahrdPowerCell, NPCID.Golem),
			(ItemID.TruffleWorm, NPCID.DukeFishron),
			(ItemID.CelestialSigil, NPCID.MoonLordCore),
		};

		private static void BuildSummonItems(AcquisitionRegistry registry, bool vanillaTable)
		{
			var npcNames = new List<(int npc, string name)>();
			foreach (int npcType in NpcIndex.AllTypes)
			{
				string name = NpcIndex.GetName(npcType);
				if (!string.IsNullOrWhiteSpace(name) && name.Length >= 3)
				{
					npcNames.Add((npcType, name));
				}
			}

			if (vanillaTable)
			{
				// 精确表:原版召唤物
				foreach ((int item, int npc) in VanillaSummons)
				{
					if (item > 0 && npc > 0)
					{
						registry.RegisterUse(ItemKey.Crafted(item),
							ModText.Format("UseSummon", "用于召唤「{0}」", Name(npc)));
					}
				}

				return;
			}

			// 启发式:名字里带「召唤 / Summon / Spawn」的消耗品,
			// 若名字里包含某个怪物的名字,就推测它是那个怪物的召唤物。
			int total = ItemLoader.ItemCount;
			for (int itemType = 1; itemType < total; itemType++)
			{
				string itemName = Lang.GetItemNameValue(itemType);
				if (string.IsNullOrWhiteSpace(itemName) || !LooksLikeSummon(itemName))
				{
					continue;
				}

				foreach ((int npc, string name) in npcNames)
				{
					if (itemName.Contains(name))
					{
						registry.RegisterUse(ItemKey.Crafted(itemType),
							ModText.Format("UseSummonGuess", "疑似用于召唤「{0}」(按名字推测)", name));
						break;
					}
				}
			}
		}

		private static bool LooksLikeSummon(string itemName)
		{
			return itemName.Contains("召唤")
				|| itemName.Contains("Summon")
				|| itemName.Contains("Spawn");
		}

		private static string Name(int itemType)
		{
			string name = Lang.GetItemNameValue(itemType);
			return string.IsNullOrWhiteSpace(name) ? ("#" + itemType) : name;
		}

		/// <summary>
		/// 填充 NPC 商店。不同 tModLoader 版本里这个方法位置/签名不一样
		/// (老版是 Chest.SetupShop,新版是 ShopHelper.VanillaSetupShop),
		/// 所以用反射按签名找一个能用的,找不到就跳过 —— 不影响其它来源。
		/// </summary>

		/// <summary>实际找到的商店填充方法(用于日志验证)。</summary>
		public static string ShopMethodName { get; private set; } = "(未找到)";


		/// <summary>一次性诊断:列出程序集里所有和商店有关的类型与方法,便于确定正确 API。</summary>

		/// <summary>
		/// 从 tModLoader 的商店数据库读取商品。
		/// 旧版的 Chest.SetupShop 已被 NPCShopDatabase.AllShops 取代。
		/// </summary>
		private static void BuildShopsFromDatabase(AcquisitionRegistry registry)
		{
			var samples = new List<string>();

			// 第三方商店的条件表达式在「没有 NPC 上下文」时会抛异常
			// (例如 Fargo 的商人要读 boss/worm 字典),这类商店直接跳过,
			// 但不能让它们把整张商店表带崩 —— 所以只计数 + 记一次日志。
			int skippedShops = 0;

			try
			{
				foreach (AbstractNPCShop shop in NPCShopDatabase.AllShops)
				{
					int added = 0;

					try
					{
                        // Read declarations, not FillShop: evaluating a shop here permanently omits
                        // locked wares and may call player-dependent conditions with no player loaded.
                        if (shop is not NPCShop npcShop) { skippedShops++; continue; }
                        foreach (var entry in npcShop.Entries)
                        {
                            Item item = entry.Item;
                            if (entry.Disabled || item == null || item.type <= 0) continue;
                            ShopEntryCount++; added++;
                            string conditions = string.Join("、", entry.Conditions.Select(c => c.Description.Value));
                            string label = Lang.GetNPCNameValue(shop.NpcType) + " / " + shop.Name;
                            registry.RegisterSource(ItemKey.Crafted(item.type),
                                ModText.Format("SrcShopSells", "商店「{0}」出售 {1}", label, FormatPrice(item.shopCustomPrice ?? item.value))
                                + (conditions.Length == 0 ? "" : "；条件：" + conditions));
                            registry.RegisterIfAbsent(ItemKey.Crafted(item.type), AcquisitionKind.Shop, ModText.Get("SrcShopShort", "商店出售"));
                        }
					}
					catch (Exception ex)
					{
                        AE2ForTerraria.Instance.Logger.Warn("Cannot index shop " + shop.FullName + ": " + ex.Message);
						// Keep other shops available.
						skippedShops++;
					}

					if (samples.Count < 10)
					{
						samples.Add(shop.Name + "=" + added);
					}
				}

				if (ShopEntryCount > 0)
				{
					ShopMethodName = "NPCShopDatabase.AllShops";
				}
			}
			catch (Exception ex)
			{
				samples.Add("FAIL " + ex.Message);
			}

			Log("商店扫描样本:" + string.Join(", ", samples));

			if (skippedShops > 0)
			{
				Log("商店扫描:跳过 " + skippedShops + " 个动态商店(未提供静态商品声明)");
			}
		}

	}
}
