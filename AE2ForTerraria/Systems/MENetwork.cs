using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Achievements;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// ME 网络:把相邻的 ME 方块连成一个网络(广度优先遍历),
	/// 并提供跨方块的存入 / 取出 / 内容汇总。
	/// 参与网络的方块:磁盘驱动器、存储单元、合成终端、分子装配室、合成 CPU、访问终端、掉落收集器。
	/// </summary>
	public static class MENetwork
	{
		private const int MaxBlocks = 4096;

		/// <summary>
		/// 连通性缓存。
		/// 以前每次存取/每 tick 都从起点全量 BFS(上限 4096 格),
		/// 装配室每 30 tick、收集器每 15 tick 都要走一遍,代价被放大很多倍。
		/// 现在按起点位置缓存几十 tick —— 短暂延迟换来大幅减少遍历。
		/// 方块增删会走 MEBlockWatcher 立刻清缓存,所以这里只需要挡住
		/// 「方块自己变了但没触发 GlobalTile」的情况,8 tick 足够。
		/// </summary>
		private const int CacheTicks = 8;

		private static readonly Dictionary<Point16, (List<Point16> Blocks, ulong Stamp)> Cache =
			new Dictionary<Point16, (List<Point16>, ulong)>();

		/// <summary>
		/// 方块 → 它所属网络的缓存锚点。
		/// 有了它,同一个网络**从任何一格查都能命中同一份缓存**
		/// (以前是按起点坐标缓存,一格一份,256 份满了还整表清空)。
		/// </summary>
		private static readonly Dictionary<Point16, Point16> BlockOwner =
			new Dictionary<Point16, Point16>();

		/// <summary>每个锚点最后一次被用到的时间,用来做 LRU 淘汰。</summary>
		private static readonly Dictionary<Point16, ulong> LastUse =
			new Dictionary<Point16, ulong>();

		private static readonly int[] OffsetX = { 1, -1, 0, 0 };
		private static readonly int[] OffsetY = { 0, 0, 1, -1 };

		public static bool IsMEBlock(int x, int y)
		{
			if (!WorldGen.InWorld(x, y, 5))
			{
				return false;
			}

			Tile tile = Main.tile[x, y];
			if (!tile.HasTile)
			{
				return false;
			}

			return MEBlocks.IsMEBlockType(tile.TileType);
		}

		public static List<Point16> Collect(int startX, int startY)
		{
			var key = new Point16(startX, startY);
			ulong now = Main.GameUpdateCount;

			if (Cache.TryGetValue(key, out (List<Point16> Blocks, ulong Stamp) cached)
				&& now - cached.Stamp < CacheTicks)
			{
				// 起点方块被拆了就作废(拆方块不一定走 GlobalTile 的时机)
				if (IsMEBlock(startX, startY))
				{
					LastUse[key] = now;
					return cached.Blocks;
				}

				Evict(key);
			}

			// 同一网络的其它起点已经有缓存就直接复用,不再重跑 BFS
			if (BlockOwner.TryGetValue(key, out Point16 owner)
				&& !owner.Equals(key)
				&& Cache.TryGetValue(owner, out (List<Point16> Blocks, ulong Stamp) shared)
				&& now - shared.Stamp < CacheTicks
				&& IsMEBlock(startX, startY))
			{
				LastUse[owner] = now;
				return shared.Blocks;
			}

			List<Point16> blocks = BfsCollect(startX, startY);

			// LRU 淘汰:以前是 count > 256 就整表清空,所有网络一起重建
			if (Cache.Count > 256)
			{
				TrimOldest();
			}

			Evict(key);
            Cache[key] = (blocks, now);
			LastUse[key] = now;

			foreach (Point16 block in blocks)
			{
				BlockOwner[block] = key;
			}

			CanonicalAnchor(blocks);
            return blocks;
		}

		private static void Evict(Point16 key)
		{
			if (Cache.TryGetValue(key, out (List<Point16> Blocks, ulong Stamp) entry))
			{
				foreach (Point16 block in entry.Blocks)
				{
					// 只删指向自己的映射,别的锚点可能也登记了同一格
					if (BlockOwner.TryGetValue(block, out Point16 owner) && owner.Equals(key))
					{
						BlockOwner.Remove(block);
					}
				}
			}

			Cache.Remove(key);
			LastUse.Remove(key);
		}

		/// <summary>淘汰掉最久没用过的那一批(约四分之一)。</summary>
		private static void TrimOldest()
		{
			var byAge = new List<KeyValuePair<Point16, ulong>>(LastUse);
			byAge.Sort((a, b) => a.Value.CompareTo(b.Value));

			int remove = Math.Max(1, byAge.Count / 4);
			for (int i = 0; i < remove && i < byAge.Count; i++)
			{
				Evict(byAge[i].Key);
			}
		}

		/// <summary>网络结构变化时清缓存(放新方块 / 拆方块时调用)。</summary>
		public static void InvalidateCache()
		{
			Cache.Clear();
            NetworkRegistry.InvalidateComponents();
			BlockOwner.Clear();
			LastUse.Clear();
			// 无线桥的频道索引也跟着失效(拆掉桥 / 放下新桥都会影响连通性)
			WirelessRegistry.Invalidate();
            NetworkInventory.Reset();
		}

		private static List<Point16> BfsCollect(int startX, int startY)
		{
			var result = new List<Point16>();
			var visited = new HashSet<Point16>();
			var queue = new Queue<Point16>();
			var start = new Point16(startX, startY);

			queue.Enqueue(start);
			visited.Add(start);

			while (queue.Count > 0 && visited.Count <= MaxBlocks)
			{
				Point16 point = queue.Dequeue();
				if (!IsMEBlock(point.X, point.Y))
				{
					continue;
				}

				result.Add(point);

				for (int d = 0; d < 4; d++)
				{
					int nx = point.X + OffsetX[d];
					int ny = point.Y + OffsetY[d];
					var next = new Point16(nx, ny);

					if (!visited.Contains(next) && IsMEBlock(nx, ny))
					{
						visited.Add(next);
						queue.Enqueue(next);
					}
				}

				// 无线桥接:同频道的另一个桥接器也算"相邻",于是断开的两块网络会连成一个
				if (MachineTiles.CanonicalType(Main.tile[point.X, point.Y].TileType) == MEBlocks.WirelessBridge)
				{
					LinkWirelessBridge(point, visited, queue);
				}
			}

			return queue.Count > 0 ? new List<Point16>() : result;
		}

		/// <summary>把同频道的桥接器当成相邻格(走常驻的频道索引,不再每次遍历全部实体)。</summary>
		private static void LinkWirelessBridge(Point16 source, HashSet<Point16> visited, Queue<Point16> queue)
		{
			if (!TileEntity.ByPosition.TryGetValue(source, out TileEntity entity)
				|| entity is not MEWirelessBridgeEntity bridge || !bridge.Enabled)
			{
				return;
			}

			foreach (Point16 point in WirelessRegistry.SameChannel(bridge.Channel))
			{
				if (point != source && visited.Add(point))
				{
					queue.Enqueue(point);

					// 进阶成就:用无线桥接把相隔很远的两个网络连起来(500 格)
					float distance = Vector2.Distance(
						new Vector2(source.X * 16f, source.Y * 16f),
						new Vector2(point.X * 16f, point.Y * 16f));

					if (distance >= 500f * 16f)
					{
						AchievementTriggers.Trigger<LongRangeWirelessAchievement>();
					}
				}
			}
		}

		/// <summary>网络里所有提供容量的方块(驱动器 + 存储单元)。</summary>
		/// <summary>
		/// 世界上所有互不相连的网络(每个是一组方块坐标)。
		/// 「网络概览」界面用它列出「世界里有哪几个网络」。
		/// </summary>
		public static List<List<Point16>> CollectAllNetworks()
		{
			var result = new List<List<Point16>>();
			var seen = new HashSet<Point16>();

			foreach (KeyValuePair<Point16, TileEntity> pair in new List<KeyValuePair<Point16, TileEntity>>(TileEntity.ByPosition))
			{
				// 已经在别的网络里了(或者不是 ME 方块)就跳过
				if (!seen.Add(pair.Key) || !IsMEBlock(pair.Key.X, pair.Key.Y))
				{
					continue;
				}

				List<Point16> blocks = Collect(pair.Key.X, pair.Key.Y);
				if (blocks.Count == 0)
				{
					continue;
				}

				foreach (Point16 block in blocks)
				{
					seen.Add(block);
				}

				result.Add(blocks);
			}

			return result;
		}

		/// <summary>
		/// 一个网络的「稳定标识」:取所有方块里 Y 最小、同 Y 取 X 最小的那一格。
		/// 网络命名用它当键 —— 和查询顺序无关,只要方块集合不变就稳定。
		/// </summary>
		public static Point16 CanonicalAnchor(List<Point16> blocks)
		{
			if (blocks == null || blocks.Count == 0)
			{
				return new Point16(-1, -1);
			}

			Point16 best = blocks[0];

			foreach (Point16 point in blocks)
			{
				if (point.Y < best.Y || (point.Y == best.Y && point.X < best.X))
				{
					best = point;
				}
			}

			NetworkRegistry.TrackComponent(blocks, best);
            return best;
		}

		public static List<IMEStorage> CollectStorage(int startX, int startY)
		{
			var units = new List<IMEStorage>();

			foreach (Point16 point in Collect(startX, startY))
			{
				if (TileEntity.ByPosition.TryGetValue(point, out TileEntity entity)
					&& entity is IMEStorage storage)
				{
					units.Add(storage);
				}
			}

			return units;
		}

		public static T FindEntity<T>(int startX, int startY) where T : class
		{
			foreach (Point16 point in Collect(startX, startY))
			{
				if (TileEntity.ByPosition.TryGetValue(point, out TileEntity entity) && entity is T match)
				{
					return match;
				}
			}

			return null;
		}

		public static MEMolecularAssemblerEntity FindAssembler(int startX, int startY)
		{
			return FindEntity<MEMolecularAssemblerEntity>(startX, startY);
		}

		public static MECraftingCpuEntity FindCraftingCpu(int startX, int startY)
		{
			return FindCraftingTerminal(startX, startY) ?? (MECraftingCpuEntity)FindEntity<MECraftingCpuEntity>(startX, startY);
		}

        public static MECraftingTerminalEntity FindCraftingTerminal(int startX, int startY)
        {
            MECraftingTerminalEntity best = null;
            foreach (var point in Collect(startX, startY))
            {
                MachineTiles.EnsureTerminal(point.X, point.Y);
                if (TileEntity.ByPosition.TryGetValue(point, out var entity) && entity is MECraftingTerminalEntity terminal
                    && (best == null || point.Y < best.Position.Y || (point.Y == best.Position.Y && point.X < best.Position.X))) best = terminal;
            }
            return best;
        }

		public static Dictionary<ItemKey, long> Aggregate(List<IMEStorage> units) => NetworkInventory.Get(units);

		/// <summary>把一个物品实例存进网络。返回实际存入数量。</summary>
		public static long Deposit(List<IMEStorage> units, Item item)
		{
			if (units == null || units.Count == 0 || item == null || item.type <= 0 || item.stack <= 0)
			{
				return 0;
			}

			ItemKey key = ItemKey.FromItem(item);
			long remaining = item.stack;

			foreach (IMEStorage unit in units)
			{
				if (remaining <= 0)
				{
					break;
				}

				// 分区清单:这块存储不收这种物品就跳过,让它落到别的方块上
				if (!unit.Allows(key))
				{
					continue;
				}

				remaining -= unit.Ledger.Insert(key, remaining);
			}

			long total = item.stack - remaining;
			if (total > 0)
			{
				item.stack -= (int)total;
				if (item.stack <= 0)
				{
					item.stack = 0;
					item.type = 0;
				}

				// 成就「第一次入库」挂在这里而不是界面上:
				// 终端存入 / 存储单元右键 / 收集器 / 总线导入 / 随身磁铁
				// 最后都会走到这个函数,一处触发就全覆盖。
				AchievementTriggers.Trigger<StoreFirstItemAchievement>();

				// 进阶成就:单个网络存到 64K 字节
				long used = 0;
				foreach (IMEStorage unit in units)
				{
					used = StorageAmounts.Add(used, unit.Ledger.UsedBytes);
				}

				if (used >= 65536)
				{
					AchievementTriggers.Trigger<BigStorageAchievement>();
				}
			}

			return total;
		}

		/// <summary>从网络中取出指定身份的物品。返回实际取出数量。</summary>
		/// <summary>
		/// 把「来源网络」里的东西尽量搬进「目标网络」。
		/// 目标方的分区清单照样生效(不接受的种类会留在来源网络)。
		/// 返回实际搬过去的件数。
		/// </summary>
		public static long TransferBetweenNetworks(int fromX, int fromY, int toX, int toY)
        {
            var from=CollectStorage(fromX,fromY); var to=CollectStorage(toX,toY);
            if (from.Count==0 || to.Count==0 || from.Exists(a=>to.Exists(b=>ReferenceEquals(a.Ledger,b.Ledger)))) return 0;
            long moved=0;
            foreach (var source in from)
            foreach (var entry in new List<KeyValuePair<ItemKey,long>>(source.Ledger.Entries))
            {
                foreach (var target in to)
                {
                    if (!target.Allows(entry.Key)) continue;
                    long amount=target.Ledger.Insert(entry.Key,source.Ledger.GetAmount(entry.Key),true);
                    if (amount<=0) continue;
                    long extracted=source.Ledger.Extract(entry.Key,amount);
                    long inserted=target.Ledger.Insert(entry.Key,extracted);
                    if (inserted<extracted) source.Ledger.Restore(entry.Key,extracted-inserted);
                    moved=StorageAmounts.Add(moved,inserted);
                }
            }
            return moved;
        }

		public static long TryWithdraw(List<IMEStorage> units, ItemKey key, long amount)
		{
			if (units == null || amount <= 0)
			{
				return 0;
			}

			long remaining = amount;

			foreach (IMEStorage unit in units)
			{
				if (remaining <= 0)
				{
					break;
				}

				remaining -= unit.Ledger.Extract(key, remaining);
			}

			return amount - remaining;
		}
	}
}
