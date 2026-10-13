using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 怪物索引:把游戏里所有 NPC(含 Boss)列出来,并提供名字查询。
	/// </summary>
	public static class NpcIndex
	{
		private static readonly List<int> All = new List<int>();
		private static bool _built;

		public static IReadOnlyList<int> AllTypes
		{
			get
			{
				EnsureBuilt();
				return All;
			}
		}

		public static void EnsureBuilt()
		{
			if (_built)
			{
				return;
			}

			All.Clear();
			int total = NPCLoader.NPCCount;

			for (int type = 1; type < total; type++)
			{
				if (string.IsNullOrWhiteSpace(GetName(type)))
				{
					continue;
				}

				All.Add(type);
			}

			_built = true;
		}

		public static void Reset()
		{
			_built = false;
			All.Clear();
		}

		public static string GetName(int npcType)
		{
			try
			{
				return Lang.GetNPCNameValue(npcType);
			}
			catch
			{
				return string.Empty;
			}
		}

		/// <summary>用汉字名 / 英文名 / 拼音匹配。</summary>
		public static bool Matches(int npcType, string query)
		{
			if (string.IsNullOrEmpty(query))
			{
				return true;
			}

			string name = GetName(npcType);
			if (name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}

			return PinyinIndex.MatchesName(name, query);
		}
	}
}

