using System.Collections.Generic;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Core
{
	/// <summary>
	/// 工作站(合成台)帮助类。泰拉瑞亚的配方用 requiredTile 表示「必须靠近哪些地块」,
	/// 这里把地块类型和「能放置它的物品」互相换算,用于判断某个方块里有没有装对应工作站。
	/// </summary>
	public static class RecipeStation
	{
		private static readonly Dictionary<int, string> StationNames = new Dictionary<int, string>();

		/// <summary>配方需要的工作站地块类型。返回空列表 = 徒手可做。</summary>
		public static void Reset() => StationNames.Clear();

        public static List<int> GetRequiredStations(Recipe recipe)
		{
			var tiles = new List<int>();
			if (recipe == null || recipe.requiredTile == null)
			{
				return tiles;
			}

			foreach (int tile in recipe.requiredTile)
			{
				if (tile >= 0)
				{
					tiles.Add(tile);
				}
			}

			return tiles;
		}

		public static bool IsHandCraftable(Recipe recipe)
		{
			return GetRequiredStations(recipe).Count == 0;
		}

		/// <summary>取得某个「地块类型」对应的显示名,用于提示缺哪个工作站。</summary>
		public static string DescribeStation(int tileType)
		{
			if (StationNames.TryGetValue(tileType, out string cached))
			{
				return cached;
			}

			// 反查:找能放置这个地块的第一个物品,拿它的名字
			for (int type = 1; type < ItemLoader.ItemCount; type++)
			{
				string name = Lang.GetItemNameValue(type);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				if (!Terraria.ID.ContentSamples.ItemsByType.TryGetValue(type, out var item)) continue;
				if (item.createTile == tileType)
				{
					StationNames[tileType] = name;
					return name;
				}
			}

			string fallback = ModText.Format("LblTileHash", "地块 #{0}", tileType);
			StationNames[tileType] = fallback;
			return fallback;
		}
	}
}
