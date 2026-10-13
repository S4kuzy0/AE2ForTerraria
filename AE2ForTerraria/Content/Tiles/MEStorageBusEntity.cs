using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// 存储总线:把它紧挨着的箱子接进 ME 网络。
	/// - 默认是**导入**(箱子 → 网络)
	/// - 右键切成**导出**(网络 → 箱子)
	/// - 按周期扫描四周实际接触的原版箱子
	///
	/// 已知限制:只认原版箱子(走 Main.chest)。第三方存储模组的容器没有通用接口,接不了。
	/// </summary>
	public class MEStorageBusEntity : ModTileEntity
	{
		private const int IntervalTicks = 30;

		private static readonly (int dx, int dy)[] Neighbors =
		{
			(1, 0), (-1, 0), (0, 1), (0, -1),
		};

		private int _timer;
        private ulong _nextErrorLogTick;

		/// <summary>false = 导入(箱子 → 网络),true = 导出(网络 → 箱子)。</summary>
		public bool ExportMode { get; private set; }

		/// <summary>
		/// 分区清单。空清单 = 不过滤(全部放行),和 AE2 存储总线的默认行为一致。
		/// </summary>
		public List<int> Filter = new List<int>();

		/// <summary>true = 白名单(只搬清单里的),false = 黑名单(清单里的不搬)。</summary>
		public bool IsWhitelist;

		private HashSet<int> _filterSet;

		public void MarkFilterDirty()
		{
			_filterSet = null;
		}

		/// <summary>清单是否放行这个物品。空清单一律放行。</summary>
		public bool Allows(int itemType)
		{
			if ((Filter == null || Filter.Count == 0) && !ItemPersistence.HasMissingFilterReferences(Filter))
			{
				return true;
			}

			_filterSet ??= new HashSet<int>(Filter);
			bool listed = _filterSet.Contains(itemType);
			return IsWhitelist ? listed : !listed;
		}

		public void ToggleMode()
		{
			ExportMode = !ExportMode;
			
		}

		/// <summary>服务器用来把客户端切好的模式写回真实实体。</summary>
		public void SetExportMode(bool export)
		{
			if (ExportMode == export)
			{
				return;
			}

			ExportMode = export;
			
		}

		public override bool IsTileValidForEntity(int x, int y)
		{
			Tile tile = Main.tile[x, y];
			return tile.HasTile && tile.TileType == ModContent.TileType<MEStorageBusTile>();
		}

		public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				NetMessage.SendTileSquare(Main.myPlayer, i, j, 1);
				NetMessage.SendData(MessageID.TileEntityPlacement, -1, -1, null, i, j, Type);
				return -1;
			}

			return Place(i, j);
		}

		public override void Update()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				return;
			}

			if (++_timer < IntervalTicks)
			{
				return;
			}

			_timer = 0;
            try { Run(); }
            catch (System.Exception ex) when (ex is not System.OutOfMemoryException && ex is not System.AccessViolationException)
            {
                ReportTransferFailure(null, ex);
            }
		}

		private void Run()
		{
			List<IMEStorage> network = MENetwork.CollectStorage(Position.X, Position.Y);
			if (network.Count == 0)
			{
				return;
			}

			foreach (int index in AdjacentChestIndices())
			{
				Chest chest = Main.chest[index];
				if (chest?.item == null)
				{
					continue;
				}


				if (ExportMode)
				{
						// 总线自己的周期性导出:不允许搬锁定物品
						ExportInto(index, chest, network, allowLocked: false);
				}
				else
				{
					ImportFrom(index, chest, network);
				}
			}
		}

		/// <summary>Resolve only touching native chest tiles, never nearby chest origins across a gap.</summary>
		private IEnumerable<int> AdjacentChestIndices()
		{
			var seen = new HashSet<int>();
			foreach ((int dx, int dy) in Neighbors)
			{
				int x = Position.X + dx, y = Position.Y + dy;
				if (!WorldGen.InWorld(x, y, 0)) continue;
				Tile tile = Main.tile[x, y];
				if (!tile.HasTile || tile.TileType >= TileID.Count
					|| (!TileID.Sets.BasicChest[tile.TileType] && !TileID.Sets.BasicDresser[tile.TileType])) continue;

				Point16 origin = TileObjectData.TopLeft(x, y);
				if (!WorldGen.InWorld(origin.X, origin.Y, 0)) continue;
				Tile first = Main.tile[origin.X, origin.Y];
				if (!first.HasTile || first.TileType != tile.TileType) continue;
				int index = Chest.FindChest(origin.X, origin.Y);
				if (index >= 0 && index < Main.chest.Length && Main.chest[index]?.item != null && seen.Add(index))
					yield return index;
			}
		}

		/// <summary>
		/// 把整个网络里的东西尽量导出到**这个网络里所有导出模式的总线**连的箱子里。
		/// 「把这块盘的东西转到箱子里」不用先全塞背包。
		/// 返回真正搬出去的件数。
		/// </summary>
		/// <param name="requesterName">
		/// 发起导出的人(玩家名)。用来判断「被锁定的物品」能不能一起导出 ——
		/// 只有所有者 / 管理者可以;传 null 表示没有玩家上下文,按「不可」处理。
		/// </param>
		public static long ExportNetworkToChests(int startX, int startY, string requesterName = null)
		{
			List<IMEStorage> network = MENetwork.CollectStorage(startX, startY);
			if (network.Count == 0)
			{
				return 0;
			}

			// 物品级锁定:这一条以前**没检查**,于是只有「可存取」权限的人
			// 可以把锁定的东西导出到箱子里再拿走 —— 锁形同虚设。
			// 现在和「取出」用同一套判断:锁着的东西只有所有者 / 管理者能搬。
			Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(startX, startY));
			bool canMoveLocked = !string.IsNullOrEmpty(requesterName)
				&& NetworkRegistry.GetLevel(anchor, requesterName) >= NetworkRegistry.LevelManage;

			long exported = 0;

			foreach (Point16 point in MENetwork.Collect(startX, startY))
			{
				if (!TileEntity.ByPosition.TryGetValue(point, out TileEntity entity)
					|| entity is not MEStorageBusEntity bus)
				{
					continue;
				}

				// 只认导出模式的总线(导入模式的总线不应该被反向灌东西)
				if (!bus.ExportMode)
				{
					continue;
				}

				foreach (int chestIndex in bus.AdjacentChestIndices())
				{
					Chest chest = Main.chest[chestIndex];
					if (chest?.item != null)
					{
						exported = StorageAmounts.Add(exported, bus.ExportInto(chestIndex, chest, network, canMoveLocked));
					}
				}
			}

			return exported;
		}

		private void ImportFrom(int chestIndex, Chest chest, List<IMEStorage> network)
		{
			for (int slot = 0; slot < chest.item.Length; slot++)
			{
				Item item = chest.item[slot];
				if (item == null || item.type <= 0 || item.stack <= 0)
				{
					continue;
				}

				// 分区清单:箱子里的东西也要过一遍
				if (!Allows(item.type))
				{
					continue;
				}

				if (MENetwork.Deposit(network, item) > 0 && Main.netMode == NetmodeID.Server)
				{
					NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, slot);
				}
			}
		}

		/// <summary>
		/// 导出:把网络里的物品放进箱子(每次找第一个还有空位的槽)。
		/// 优先补进箱子里已有的同种物品,其次才占用空格。
		/// </summary>
		/// <param name="allowLocked">
		/// true = 允许搬运被锁定的物品(只有所有者 / 管理者主动发起导出时)。
		/// 总线**自己**每 30 tick 的自动导出永远是 false —— 自动化不该绕过锁定。
		/// </param>
		private long ExportInto(int chestIndex, Chest chest, List<IMEStorage> network,
			bool allowLocked)
		{
			const int perPassExport = 4;
            long exported = 0;
            HashSet<ItemKey> failedCandidates = null;

			// 锁定判断要用网络锚点;允许搬运锁定物品时就不用算了
			Point16 lockAnchor = allowLocked
				? default
				: MENetwork.CanonicalAnchor(MENetwork.Collect(Position.X, Position.Y));

			for (int pass = 0; pass < perPassExport; pass++)
			{
				bool moved = false;
                // A successful fill may invoke mod item loading hooks. Do not reuse identities after that fill.
                var scan = new ChestScan(chest);

				foreach (IMEStorage unit in network)
				{
					var snapshot = new List<KeyValuePair<ItemKey, long>>();
					foreach (KeyValuePair<ItemKey, long> entry in unit.Ledger.Entries)
					{
						snapshot.Add(entry);
					}

					foreach (KeyValuePair<ItemKey, long> entry in snapshot)
					{
						if (entry.Key.Type <= 0 || entry.Value <= 0 || failedCandidates?.Contains(entry.Key) == true)
						{
							continue;
						}

						// 锁定的物品:自动化导出和没权限的人都不许搬
						if (!allowLocked && NetworkRegistry.IsLocked(lockAnchor, entry.Key.Type))
						{
							continue;
						}

						// 分区清单:网络里的东西也要过一遍
						if (!Allows(entry.Key.Type))
						{
							continue;
						}

						// 坏的物品 ID 直接跳过(导出路径也不能因为存档残留就抛异常)
						if (!SavedItemGuard.IsValidType(entry.Key.Type))
						{
							continue;
						}

                        long taken;
                        List<int> touched;
                        try
                        {
                            int maxStack = SavedItemGuard.MaxStackOf(entry.Key.Type);
                            int free = FreeSpaceFor(scan, entry.Key, maxStack);
                            if (free <= 0) continue;
                            int take = (int)System.Math.Min(entry.Value, free);
                            taken = TransferToChest(scan, unit.Ledger, entry.Key, take, maxStack, out touched);
                        }
                        catch (System.Exception ex) when (ex is not System.OutOfMemoryException && ex is not System.AccessViolationException)
                        {
                            // Preparation can invoke mod hooks even while measuring room. Discard
                            // every cached identity before inspecting the next candidate. A failed
                            // key is retried next scan, not on every pass ahead of healthy entries.
                            scan.Invalidate();
                            (failedCandidates ??= new HashSet<ItemKey>()).Add(entry.Key);
                            ReportTransferFailure(entry.Key, ex);
                            continue;
                        }
						if (taken <= 0)
						{
							continue;
						}

						moved = true;
                        exported = StorageAmounts.Add(exported, taken);

						// 以前这里固定传槽位 0,而实际搬进去的可能是别的槽位 ——
						// 多人下客户端看到的那个箱子就不同步。现在把真正动过的槽位逐个同步。
						if (Main.netMode == NetmodeID.Server)
						{
							foreach (int slot in touched)
							{
								NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, slot);
							}
						}

						break;
					}

					if (moved)
					{
						break;
					}
				}

				if (!moved)
				{
					break;
				}
			}
            return exported;
		}

        private void ReportTransferFailure(ItemKey? key, System.Exception exception)
        {
            if (Main.GameUpdateCount < _nextErrorLogTick) return;
            _nextErrorLogTick = Main.GameUpdateCount + 600;
            string context = key.HasValue
                ? " skipped export candidate " + key.Value + "; its stock was not transferred, other candidates continue."
                : " failed during automatic transfer.";
            AE2ForTerraria.Instance?.Logger.Error("Storage bus at " + Position + context, exception);
        }

        /// <summary>One synchronous candidate scan only; mutable custom data is never cached between scans.</summary>
        internal sealed class ChestScan
        {
            public readonly Chest Chest;
            private ItemKey?[] _identities;
            private readonly System.Func<Item, ItemKey> _identify;
            private readonly System.Func<ItemKey, Item> _create;
            public ChestScan(Chest chest, System.Func<Item, ItemKey> identify = null, System.Func<ItemKey, Item> create = null)
            {
                Chest = chest;
                _identify = identify;
                _create = create;
            }
            public ItemKey Identify(Item item) => _identify == null ? ItemKey.FromItem(item) : _identify(item);
            public Item Create(ItemKey key) => _create == null ? key.CreateItem() : _create(key);
            public void Invalidate() => _identities = null;
            public bool CanStackInto(int slot, ItemKey key, int maxStack)
            {
                Item item = Chest.item[slot];
                if (item == null || item.type != key.Type || item.prefix != key.Prefix
                    || item.stack >= System.Math.Min(maxStack, item.maxStack)) return false;
                _identities ??= new ItemKey?[Chest.item.Length];
                ItemKey? identity = _identities[slot];
                if (!identity.HasValue)
                {
                    identity = Identify(item);
                    _identities[slot] = identity;
                }
                return identity.Value.Equals(key);
            }
        }

		private static int FreeSpaceFor(ChestScan scan, ItemKey key, int maxStack)
		{
			long free = 0;
            Chest chest = scan.Chest;

			for (int slot = 0; slot < chest.item.Length; slot++)
			{
				Item item = chest.item[slot];

				if (item == null || item.type <= 0)
				{
					free += maxStack;
					continue;
				}

				// 词缀也算身份的一部分:传奇的剑不该被塞进普通剑那一格
				if (scan.CanStackInto(slot, key, maxStack))
				{
					free += System.Math.Min(maxStack, item.maxStack) - item.stack;
				}
			}

			return (int)System.Math.Min(free, int.MaxValue);
		}

        /// <summary>Prepare all hook-dependent work before committing either inventory. Exceptions propagate without our partial writes.</summary>
        internal static int TransferToChest(ChestScan scan, StorageLedger ledger, ItemKey key, int amount,
            int maxStack, out List<int> touched)
        {
            touched = new List<int>();
            if (amount <= 0 || maxStack <= 0) return 0;
            Item[] slots = scan.Chest.item;
            int ledgerVersion = ledger.Version;
            int requested = (int)System.Math.Min(amount, ledger.GetAmount(key));
            if (requested <= 0) return 0;
            int remaining = requested;
            var prepared = new List<(int Slot, Item Original, int OriginalStack, Item Result)>();
            try
            {
                // Cloning, key serialization and item reconstruction may call other mods. None may run after debit.
                for (int slot = 0; slot < slots.Length && remaining > 0; slot++)
                {
                    Item original = slots[slot];
                    if (!scan.CanStackInto(slot, key, maxStack)) continue;
                    int put = System.Math.Min(remaining, System.Math.Min(maxStack, original.maxStack) - original.stack);
                    Item result = original.Clone();
                    if (ReferenceEquals(original, result)) throw new System.InvalidOperationException("Item clone reused a live chest stack.");
                    result.stack = original.stack + put;
                    prepared.Add((slot, original, original.stack, result));
                    touched.Add(slot);
                    remaining -= put;
                }
                for (int slot = 0; slot < slots.Length && remaining > 0; slot++)
                {
                    Item original = slots[slot];
                    if (original != null && original.type > 0) continue;
                    Item result = scan.Create(key);
                    if (result == null || result.type != key.Type || result.prefix != key.Prefix || result.maxStack <= 0)
                        throw new System.InvalidOperationException("Reconstructed chest item has a different identity.");
                    result.stack = System.Math.Min(remaining, System.Math.Min(maxStack, result.maxStack));
                    prepared.Add((slot, original, original?.stack ?? 0, result));
                    touched.Add(slot);
                    remaining -= result.stack;
                }
                int accepted = requested - remaining;
                if (accepted == 0) return 0;

                // Verify custom data on the prepared results and source stacks before entering the no-hook commit.
                foreach (var change in prepared)
                {
                    if (change.Result.stack <= 0 || change.Result.stack > change.Result.maxStack || !scan.Identify(change.Result).Equals(key))
                        throw new System.InvalidOperationException("Prepared chest item lost custom data.");
                    if (change.Original != null && change.Original.type > 0 && !scan.Identify(change.Original).Equals(key))
                        throw new System.InvalidOperationException("Chest item identity changed during export preparation.");
                }
                if (!ReferenceEquals(scan.Chest.item, slots) || ledger.Version != ledgerVersion || ledger.GetAmount(key) < accepted)
                    throw new System.InvalidOperationException("Source inventory changed during chest export preparation.");
                foreach (var change in prepared)
                    if (!ReferenceEquals(slots[change.Slot], change.Original) || (change.Original?.stack ?? 0) != change.OriginalStack)
                        throw new System.InvalidOperationException("Chest slot changed during export preparation.");

                // StorageLedger is sealed and these operations contain no item hooks or other mod callbacks.
                ledger.Extract(key, accepted);
                foreach (var change in prepared) slots[change.Slot] = change.Result;
                return accepted;
            }
            finally
            {
                // Even failed preparation can run hooks. A reused scan must never retain pre-hook identities.
                scan.Invalidate();
            }
        }

		public override void SaveData(TagCompound tag)
		{
			tag["exportMode"] = ExportMode;
			tag["whitelist"] = IsWhitelist;
			ItemPersistence.SaveFilter(tag,"filter",Filter);
		}

		public override void LoadData(TagCompound tag)
		{
			ExportMode = tag.ContainsKey("exportMode") && tag.GetBool("exportMode");
			IsWhitelist = tag.ContainsKey("whitelist") && tag.GetBool("whitelist");

			Filter = ItemPersistence.LoadFilter(tag,"filter");

			MarkFilterDirty();
			
		}
	}
}
