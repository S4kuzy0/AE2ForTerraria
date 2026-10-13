using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 无线桥接器的「频道 → 桥坐标」索引。
	///
	/// 以前每次 BFS 遇到桥接器都要遍历一遍**全部** TileEntity 去找同频道的桥,
	/// 桥一多、实体一多就是 O(桥数 × 实体数)。
	/// 现在这张表常驻:放/拆方块、换频道时标脏,下次用到时重建一次(一次全表扫描)。
	/// 顺便用一个「实体总数」当兜底信号 —— 万一有别的模组动了 TileEntity 而没通知我们,
	/// 数量对不上也会自动重建。
	/// </summary>
	public static class WirelessRegistry
	{
		private static readonly Dictionary<int, List<Point16>> Channels =
			new Dictionary<int, List<Point16>>();

		private static bool _dirty = true;
		private static int _builtEntityCount = -1;

		public static void Invalidate()
		{
			_dirty = true;
		}

		public static void Reset()
		{
			Channels.Clear();
			_dirty = true;
			_builtEntityCount = -1;
		}

		/// <summary>取同频道的所有桥坐标(含自己)。</summary>
		public static List<Point16> SameChannel(int channel)
		{
			EnsureBuilt();

			return Channels.TryGetValue(channel, out List<Point16> list)
				? list
				: Empty;
		}

		private static readonly List<Point16> Empty = new List<Point16>();

		private static void EnsureBuilt()
		{
			int entityCount = TileEntity.ByPosition.Count;

			if (!_dirty && entityCount == _builtEntityCount)
			{
				return;
			}

			Channels.Clear();

			foreach (KeyValuePair<Point16, TileEntity> pair in TileEntity.ByPosition)
			{
				if (pair.Value is not MEWirelessBridgeEntity bridge || !bridge.Enabled || !WorldGen.InWorld(pair.Key.X, pair.Key.Y, 0)
                    || !bridge.IsTileValidForEntity(pair.Key.X, pair.Key.Y))
				{
					continue;
				}

				if (!Channels.TryGetValue(bridge.Channel, out List<Point16> list))
				{
					list = new List<Point16>();
					Channels[bridge.Channel] = list;
				}

				list.Add(pair.Key);
			}

			_builtEntityCount = entityCount;
			_dirty = false;
		}
	}
}
