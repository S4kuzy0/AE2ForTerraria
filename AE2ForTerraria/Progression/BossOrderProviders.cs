using System.Collections.Generic;
using System;
using System.Collections;
using System.Linq;
using AE2ForTerraria.Acquisition;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Progression
{
	/// <summary>
	/// Boss 顺序来源。数值越小优先级越高,ProgressionGraph 会取第一个成功构建的 provider。
	/// </summary>
	public interface IBossOrderProvider
	{
		int Priority { get; }
		string Name { get; }
		bool TryBuild(List<ProgressionTier> output);
	}

	/// <summary>
	/// 优先级 0:玩家手动覆盖(最终兜底,永远最可信)。
	/// 数据来自模组配置里的「自定义 Boss 顺序」(每行一个阶段名),其次是代码里注册的覆盖项。
	/// </summary>
	public sealed class PlayerOverrideProvider : IBossOrderProvider
	{
		public readonly List<ProgressionTier> Custom = new List<ProgressionTier>();

		public int Priority => 0;
		public string Name => "player";

		public bool TryBuild(List<ProgressionTier> output)
		{
			// 玩家在配置里手填的顺序优先级最高
			MEConfig config = MEConfig.Instance;
			if (config?.CustomBossOrder != null && config.CustomBossOrder.Count > 0)
			{
				int order = 0;
				foreach (string name in config.CustomBossOrder)
				{
					if (!string.IsNullOrWhiteSpace(name))
					{
						output.Add(new ProgressionTier(order, "player_" + order, name.Trim(), Name));
						order++;
					}
				}

				if (output.Count > 0)
				{
					return true;
				}
			}

			// 其次是代码里注册的覆盖项
			if (Custom.Count == 0)
			{
				return false;
			}

			output.AddRange(Custom);
			return true;
		}
	}

	/// <summary>
	/// 优先级 10:BossChecklist(若已安装)。
	/// 走官方接口 Mod.Call("GetBossInfoDictionary", Mod, "1.6"):
	/// 读 progression(排序)、npcIDs(精确锚定)、treasureBag、spawnItems、spawnInfo、loot、dropRateInfo。
	/// 接口拿不到数据时返回 false,让下层 provider 接手。
	/// </summary>
	public sealed class BossChecklistProvider : IBossOrderProvider
	{
		public const string ModName = "BossChecklist";

		private static bool _loggedCallResult;

		private static void LogCallResult(object raw)
		{
			if (_loggedCallResult || !MEConfig.DiagnosticsEnabled)
			{
				return;
			}

			_loggedCallResult = true;

			string desc = raw == null ? "null" : raw.GetType().FullName;
			string sample = string.Empty;

			if (raw is System.Collections.IDictionary dict && dict.Count > 0)
			{
				foreach (object key in dict.Keys)
				{
					object value = dict[key];
					sample = " | 首个键=" + key + ", 值类型="
						+ (value == null ? "null" : value.GetType().FullName);
					break;
				}
			}

			Terraria.ModLoader.ModContent.GetInstance<AE2ForTerraria>()?.Logger.Info(
				"BossChecklist GetBossInfoDictionary 返回类型: " + desc + sample);
		}

		/// <summary>
		/// 从任意对象里反射读一个数字字段 / 属性,按名字片段匹配。
		/// BossChecklist 的 dropRateInfo 元素结构随版本变(有的叫 itemId,有的叫 id),
		/// 所以按名字片段找,读不到就保持原值。
		/// </summary>
		private static void ReadNumber(object target, string fragment, ref int value)
		{
			object raw = ReadMember(target, fragment);
			if (raw == null)
			{
				return;
			}

			try
			{
				value = System.Convert.ToInt32(raw);
			}
			catch
			{
				// 类型不对就当没读到
			}
		}

		private static void ReadNumber(object target, string fragment, ref float value)
		{
			object raw = ReadMember(target, fragment);
			if (raw == null)
			{
				return;
			}

			try
			{
				value = System.Convert.ToSingle(raw);
			}
			catch
			{
				// 类型不对就当没读到
			}
		}

		private static object ReadMember(object target, string fragment)
		{
			if (target == null || string.IsNullOrEmpty(fragment))
			{
				return null;
			}

			const System.Reflection.BindingFlags flags =
				System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.Public
				| System.Reflection.BindingFlags.NonPublic
				| System.Reflection.BindingFlags.IgnoreCase;

			System.Type type = target.GetType();

			foreach (System.Reflection.FieldInfo field in type.GetFields(flags))
			{
				if (field.Name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0
					&& !field.FieldType.IsClass)
				{
					try
					{
						return field.GetValue(target);
					}
					catch
					{
						// 读不到就继续找
					}
				}
			}

			foreach (System.Reflection.PropertyInfo property in type.GetProperties(flags))
			{
				if (property.Name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0
					&& !property.PropertyType.IsClass
					&& property.GetIndexParameters().Length == 0)
				{
					try
					{
						return property.GetValue(target);
					}
					catch
					{
						// 读不到就继续找
					}
				}
			}

			// 有些版本直接用字典存
			if (target is System.Collections.IDictionary dictionary)
			{
				foreach (object key in dictionary.Keys)
				{
					if (key is string name
						&& name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return dictionary[key];
					}
				}
			}

			return null;
		}

		public int Priority => 10;
		public string Name => "bosschecklist";

		public bool IsInstalled => ModLoader.HasMod(ModName);

		public bool TryBuild(List<ProgressionTier> output)
		{
			if (!IsInstalled)
			{
				return false;
			}

			// 官方接口:BossChecklist 的 Mod.Call("GetBossInfoDictionary", Mod, version)
			// 返回字典[Boss 键] = 属性字典；兼容早期嵌套资料。
			// 属性里有 progression(排序值)、npcIDs、downed 等。
			try
			{
				Mod bossChecklist = ModLoader.GetMod(ModName);
				object raw = bossChecklist?.Call("GetBossInfoDictionary", AE2ForTerraria.Instance, "1.6");
                return TryBuildFromData(raw, output, StorageSystem.Acquisition);
            }
            catch (Exception ex)
            {
                StorageSystem.Acquisition?.ReplaceDerived("bosschecklist", null);
                AE2ForTerraria.Instance?.Logger.Warn("BossChecklist query unavailable; using other progression sources: " + ex.Message);
                return false;
            }
        }

        internal bool TryBuildFromData(object raw, List<ProgressionTier> output, AcquisitionRegistry destination)
        {
            var registry = new AcquisitionRegistry();
            try
            {
                var perMod = ReadDictionary(raw);
				if (perMod == null)
				{
					LogCallResult(raw);
					destination?.ReplaceDerived("bosschecklist", null);
					return false;
				}

				var ordered = new List<(float order, string key, List<int> npcs, Dictionary<string, object> info)>();

                void AddEntry(string key,Dictionary<string,object> info)
                {
                    try
                    {
                        if (!info.TryGetValue("progression", out object rank) || rank == null) return;
                        float order = Convert.ToSingle(rank, System.Globalization.CultureInfo.InvariantCulture);
                        if (!float.IsFinite(order)) return;
                        var npcs = info.TryGetValue("npcIDs", out object ids) ? ReadIds(ids, NPCLoader.NPCCount) : new List<int>();
                        ordered.Add((order, key, npcs, info));
                    }
                    catch (Exception ex) { WarnEntry(key, ex); }
                }
                foreach(var entry in perMod)
                {
                    try
                    {
                        var info = ReadDictionary(entry.Value);
                        if (info == null) continue;
                        if (info.ContainsKey("progression")) AddEntry(entry.Key, info);
                        else foreach (var nested in info)
                        {
                            var boss = ReadDictionary(nested.Value);
                            if (boss != null) AddEntry(nested.Key, boss);
                        }
                    }
                    catch (Exception ex) { WarnEntry(entry.Key, ex); }
                }
				if (ordered.Count == 0)
				{
					destination?.ReplaceDerived("bosschecklist", null);
					return false;
				}

				ordered.Sort((a, b) => a.order.CompareTo(b.order));

				int index = 0;

				foreach ((float order, string key, List<int> npcs, Dictionary<string, object> info) in ordered)
				{
                    try
                    {
					// 优先用 NPC 的显示名,拿不到就用 Boss 键。
					// 多 NPC 事件以前只显示 npcs[0],现在把前两个都列出来。
					bool declaredName=info.TryGetValue("displayName",out object label) && label is Terraria.Localization.LocalizedText;
                    string display=declaredName ? ((Terraria.Localization.LocalizedText)label).Value : label as string ?? (npcs.Count>0 ? NpcIndex.GetName(npcs[0]) : key);
                    declaredName |= label is string;
					if (string.IsNullOrWhiteSpace(display))
					{
						display = key;
					}

					if (npcs.Count > 1 && !declaredName)
					{
						string second = NpcIndex.GetName(npcs[1]);
						if (!string.IsNullOrWhiteSpace(second) && second != display)
						{
							display = display + " / " + second
								+ (npcs.Count > 2 ? ModText.Get("LblAndMore", " 等") : "");
						}
					}

					var tier = new ProgressionTier(index++, "bc_" + key, display, Name);
					tier.NpcIds.AddRange(npcs);
                    if(info.TryGetValue("downed",out object downed) && downed is Func<bool> downedCheck) tier.IsDowned=downedCheck;

					// 用 npcIDs 反查这个 Boss 的掉落,让物品锚定走精确路径
					// (不再靠阶段名的字符串包含匹配)
					string spawnInfo = null;
					if (info.TryGetValue("spawnInfo", out object spawnInfoRaw)
						&& spawnInfoRaw is string spawnInfoText
						&& !string.IsNullOrWhiteSpace(spawnInfoText))
					{
						spawnInfo = spawnInfoText;
						tier.SpawnInfo = spawnInfoText;
					}

                    if(info.TryGetValue("loot",out object declaredLoot)) tier.LootItems.AddRange(ReadIds(declaredLoot is IDictionary lootMap ? lootMap.Keys : declaredLoot, ItemLoader.ItemCount));
					output.Add(tier);

					// —— 官方接口还给了宝藏袋和召唤物,顺手接上 ——
					if (registry == null)
					{
						continue;
					}

					// 宝藏袋:击败这个 Boss 获得
					if (info.TryGetValue("treasureBag", out object bagObject)
						&& bagObject is int bagId && bagId > 0 && bagId < ItemLoader.ItemCount)
					{
						tier.TreasureBag = bagId;
						registry.RegisterSource(ItemKey.Crafted(bagId),
							ModText.Format("SrcDefeatBag", "击败「{0}」获得", display));
						registry.RegisterIfAbsent(ItemKey.Crafted(bagId),
							AcquisitionKind.TreasureBag,
							ModText.Format("SrcDefeatBag", "击败「{0}」获得", display));
					}

					// 召唤物:用于召唤这个 Boss
					if (info.TryGetValue("spawnItems", out object spawnObject))
					{
						foreach (int spawnId in ReadIds(spawnObject, ItemLoader.ItemCount))
						{
							if (spawnId > 0)
							{
								tier.SpawnItems.Add(spawnId);
								registry.RegisterUse(ItemKey.Crafted(spawnId),
									ModText.Format("UseSummon", "用于召唤「{0}」", display)
									+ (spawnInfo == null ? string.Empty : "  [" + spawnInfo + "]"));
							}
						}
					}

					// loot:BossChecklist 给的「掉落池」表(物品 -> 概率)。
					// 这张表比我们从掉落规则反推的更准,而且包含事件/多 NPC 的情况。
					if (info.TryGetValue("loot", out object lootObject)
						&& lootObject is Dictionary<int, float> loot)
					{
						foreach (KeyValuePair<int, float> drop in loot)
						{
							if (drop.Key <= 0 || drop.Key >= ItemLoader.ItemCount || !float.IsFinite(drop.Value))
							{
								continue;
							}

							float percent = drop.Value <= 1f ? drop.Value * 100f : drop.Value;
							registry.RegisterSource(ItemKey.Crafted(drop.Key),
								ModText.Format("SrcDefeatDrop", "击败「{0}」掉落 {1}%",
									display, percent.ToString("0.#")));
							registry.RegisterIfAbsent(ItemKey.Crafted(drop.Key),
								AcquisitionKind.NpcDrop,
								ModText.Format("SrcDefeatDropShort", "击败「{0}」掉落", display));
						}
					}

					// dropRateInfo:老版本接口,是一串带 itemId / dropRate 字段的对象。
					// 字段名用反射读,读不到就跳过(不影响其它来源)。
					if (info.TryGetValue("dropRateInfo", out object rateObject)
						&& rateObject is System.Collections.IEnumerable rateList
						&& rateObject is not string)
					{
						foreach (object element in rateList)
						{
							if (element == null)
							{
								continue;
							}

							int itemId = 0;
							float rate = 0f;
							ReadNumber(element, "itemId", ref itemId);
							ReadNumber(element, "itemID", ref itemId);
							ReadNumber(element, "dropRate", ref rate);
							ReadNumber(element, "rate", ref rate);

							if (itemId <= 0)
							{
								int asFloat = 0;
								ReadNumber(element, "id", ref asFloat);
								itemId = asFloat;
							}

							if (itemId <= 0 || itemId >= ItemLoader.ItemCount || !float.IsFinite(rate))
							{
								continue;
							}

							float percent = rate <= 1f ? rate * 100f : rate;
							string text = percent > 0f
								? ModText.Format("SrcDefeatDrop", "击败「{0}」掉落 {1}%",
									display, percent.ToString("0.#"))
								: ModText.Format("SrcDefeatDropShort", "击败「{0}」掉落", display);

							registry.RegisterSource(ItemKey.Crafted(itemId), text);
							registry.RegisterIfAbsent(ItemKey.Crafted(itemId),
								AcquisitionKind.NpcDrop,
								ModText.Format("SrcDefeatDropShort", "击败「{0}」掉落", display));
						}
					}
				    }
                    catch (Exception ex) { WarnEntry(key, ex); }
				}
                destination?.ReplaceDerived("bosschecklist", registry);
				return output.Count > 0;
			}
			catch (Exception ex)
			{
                destination?.ReplaceDerived("bosschecklist", registry);
                AE2ForTerraria.Instance?.Logger.Warn("BossChecklist data partially unavailable: " + ex.Message);
				return output.Count > 0;
			}
		}

        private static void WarnEntry(string key, Exception error) =>
            AE2ForTerraria.Instance?.Logger.Warn("BossChecklist entry skipped or incomplete: " + key + " " + error.Message);

        private static List<int> ReadIds(object value, int maximum)
        {
            var result = new List<int>();
            if (value is not IEnumerable values || value is string) return result;
            foreach (object entry in values)
            {
                try
                {
                    int id = Convert.ToInt32(entry, System.Globalization.CultureInfo.InvariantCulture);
                    if (id > 0 && id < maximum && !result.Contains(id)) result.Add(id);
                }
                catch (Exception) { /* An unsupported ID does not invalidate its other siblings. */ }
            }
            return result;
        }

        private static Dictionary<string, object> ReadDictionary(object value)
        {
            if (value is Dictionary<string, object> direct) return direct;
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    if (entry.Key is string key) result[key] = entry.Value;
                return result;
            }
            // IReadOnlyDictionary and immutable dictionaries need not implement non-generic IDictionary.
            bool stringMap = value?.GetType().GetInterfaces().Any(type => type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) || type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                && type.GetGenericArguments()[0] == typeof(string)) == true;
            if (!stringMap || value is not IEnumerable entries) return null;
            foreach (object entry in entries)
            {
                if (entry == null) continue;
                var type = entry.GetType();
                if (type.GetProperty("Key")?.GetValue(entry) is string key) result[key] = type.GetProperty("Value")?.GetValue(entry);
            }
            return result;
        }

	}

	/// <summary>
	/// 优先级 20:内置知名模组顺序表(灾厄 / Fargo)。
	/// 这是「装了灾厄/Fargo 也能开箱即对」的关键。
	/// 表里没有的 Boss 由推断引擎(AppendInferredBosses)补在后面。
	/// </summary>
	public sealed class BuiltinModOrderProvider : IBossOrderProvider
	{
		public int Priority => 20;
		public string Name => "builtin";

		public bool TryBuild(List<ProgressionTier> output)
		{
			string[] order;
			string source;

			// 装了灾厄就用灾厄的顺序(它本身就包含原版 Boss),这样阶段能延伸到月总之后。
			if (ModLoader.HasMod("CalamityMod"))
			{
				order = CalamityOrder;
				source = "builtin:calamity";
			}
			else if (ModLoader.HasMod("FargowiltasSouls") || ModLoader.HasMod("Fargowiltas"))
			{
				order = FargoOrder;
				source = "builtin:fargo";
			}
			else
			{
				return false; // 都没有就让原版里程碑接手
			}

			for (int i = 0; i < order.Length; i++)
			{
				output.Add(new ProgressionTier(i, "boss_" + i, order[i], source));
			}

			return true;
		}

		public static readonly string[] CalamityOrder =
		{
			"Desert Scourge", "Eye of Cthulhu", "Crabulon", "Eater of Worlds", "Brain of Cthulhu",
			"Hive Mind", "Perforators", "Skeletron", "Slime God", "Wall of Flesh",
			"Cryogen", "Brimstone Elemental", "Aquatic Scourge", "Sulphur Elemental (Calamitas Clone)",
			"Plantera", "Calamitas", "Leviathan and Anahita", "Astrum Aureus", "Golem",
			"Plaguebringer Goliath", "Ravager", "Lunatic Cultist", "Astrum Deus", "Moon Lord",
			"Profaned Guardians", "Providence", "Storm Weaver", "Ceaseless Void", "Signus",
			"Polterghast", "Devourer of Gods", "Yharon", "Exo Mechs", "Supreme Calamitas"
		};

		public static readonly string[] FargoOrder =
		{
			"Trojan Squirrel", "King Slime", "Eye of Cthulhu", "Eater of Worlds", "Brain of Cthulhu",
			"Deerclops", "Queen Bee", "Skeletron", "Wall of Flesh", "Queen Slime",
			"The Destroyer", "The Twins", "Skeletron Prime", "Plantera", "Golem",
			"Duke Fishron", "Empress of Light", "Lunatic Cultist", "Moon Lord",
			"Mutant (Fargo)", "Abominationn (Fargo)", "Deviantt (Fargo)", "Eridanus (Fargo)"
		};
	}

	/// <summary>
	/// 优先级 30:推断引擎 —— 没有外部资料时,从掉落/召唤依赖推断顺序。
	/// 底子是原版检查点,模组自己的 Boss 按评分追加:
	///   评分 = 生命值对数 + 防御 + **掉落价值(loot tier)**
	/// 召唤依赖用真正的 DAG 拓扑排序:Boss Y 的召唤物需要 Boss X 的掉落 => Y 必在 X 之后。
	/// </summary>
	public sealed class InferenceProvider : IBossOrderProvider
	{
		public int Priority => 30;
		public string Name => "inferred";

		public bool TryBuild(List<ProgressionTier> output)
		{
			// 底子是原版检查点
			var vanilla = new List<ProgressionTier>();
			new VanillaCheckpointProvider().TryBuild(vanilla);
			output.AddRange(vanilla);

			// 模组自己的 Boss 按推断分数追加在后面。
			// 判定 Boss 走图鉴(便宜),不再 new NPC().SetDefaults() —— 那个会让加载卡死。
			List<(int npc, float score)> extras = ProgressionGraph.ComputeInferredBosses();

			int index = output.Count;
			foreach ((int npc, float score) in extras)
			{
				output.Add(new ProgressionTier(index++, "inf_" + (NPCLoader.GetNPC(npc)?.FullName ?? "Terraria/" + Terraria.ID.NPCID.Search.GetName(npc)), NpcIndex.GetName(npc), Name));
			}

			return extras.Count > 0;
		}
	}

	/// <summary>
	/// 优先级 100:原版里程碑。永远可用,是整个时间点系统的地基。
	/// 用的是原版自带的 downed* 标志,不依赖任何外部模组。
	/// </summary>
	public sealed class VanillaCheckpointProvider : IBossOrderProvider
	{
		public int Priority => 100;
		public string Name => "vanilla";

		private sealed class Checkpoint
		{
			public string Id;
			public System.Func<bool> Reached;

			public Checkpoint(string id, System.Func<bool> reached)
			{
				Id = id;
				Reached = reached;
			}

			/// <summary>显示名走本地化(键:Vanilla_{Id})。</summary>
			public string DisplayName => ModText.Get("Vanilla_" + Id, FallbackName(Id));

			private static string FallbackName(string id)
			{
				switch (id)
				{
					case "start": return "开局";
					case "king_slime": return "史莱姆王后";
					case "eye": return "克苏鲁之眼后";
					case "evil": return "世界吞噬者/克苏鲁之脑后";
					case "skeletron": return "骷髅王后";
					case "queen_bee": return "蜂后后";
					case "hardmode": return "困难模式(血肉墙后)";
					case "mech": return "机械三王后";
					case "plantera": return "世纪之花后";
					case "golem": return "石巨人后";
					case "duke_fishron": return "猪龙鱼公爵后";
					case "empress": return "光之女皇后";
					case "moonlord": return "月亮领主后";
					default: return id;
				}
			}
		}

		private static readonly Checkpoint[] Table =
		{
			// 显示名在 TryBuild 里按 Id 取本地化文案。
			// Id 必须保持稳定 —— 它是阶段锚定用的键,不能跟着语言变。
			new Checkpoint("start", () => true),
			new Checkpoint("king_slime", () => NPC.downedSlimeKing),
			new Checkpoint("eye", () => NPC.downedBoss1),
			new Checkpoint("evil", () => NPC.downedBoss2),
			new Checkpoint("skeletron", () => NPC.downedBoss3),
			new Checkpoint("queen_bee", () => NPC.downedQueenBee),
			new Checkpoint("hardmode", () => Main.hardMode),
			new Checkpoint("mech", () => NPC.downedMechBoss1),
			new Checkpoint("plantera", () => NPC.downedPlantBoss),
			new Checkpoint("golem", () => NPC.downedGolemBoss),
			new Checkpoint("duke_fishron", () => NPC.downedFishron),
			new Checkpoint("empress", () => NPC.downedEmpressOfLight),
			new Checkpoint("moonlord", () => NPC.downedMoonlord),
		};

		public bool TryBuild(List<ProgressionTier> output)
		{
			for (int i = 0; i < Table.Length; i++)
			{
				Checkpoint checkpoint = Table[i];
				output.Add(new ProgressionTier(i, checkpoint.Id, checkpoint.DisplayName, Name));
			}

			return true;
		}

		/// <summary>当前世界已经打到第几个检查点 —— 用于「你现在处于哪个阶段」。</summary>
		public static int CurrentCheckpointIndex()
		{
			int reached = 0;
			for (int i = 0; i < Table.Length; i++)
			{
				if (Table[i].Reached())
				{
					reached = i;
				}
			}

			return reached;
		}
	}
}
