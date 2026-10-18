using System.Linq;
using System.Collections.Generic;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// 磁盘驱动器。物品真实存在这个方块里,磁盘只负责提供容量 ——
	/// 所以可以随时换磁盘,只要新容量不小于已用容量。
	/// </summary>
	public class MEDiskDriveEntity : ModTileEntity, IMEStorage, IPartitionedStorage
	{
		public const int DiskSlots = 10;

		public readonly Item[] Disks = new Item[DiskSlots];

		/// <summary>
		/// 磁盘提供累加的类别位和每身份数量上限；已存内容不因换盘或版本变更而截断。
		/// </summary>
		public StorageLedger Ledger { get; } = new StorageLedger(8, 8, int.MaxValue);

		/// <summary>
		/// 分区清单:空 = 不过滤。想让「这块盘只放矿石」就把它设成白名单 + 勾矿石。
		/// 按**物品 ID**判断,不看词缀(和存储总线一致)。
		/// </summary>
		public List<int> Filter { get; set; } = new List<int>();

		/// <summary>true = 白名单(只收清单里的),false = 黑名单。</summary>
		public bool IsWhitelist { get; set; }

		private HashSet<int> _filterSet;

		public void MarkFilterDirty()
		{
			_filterSet = null;
		}

		public bool Allows(ItemKey key)
		{
			if ((Filter == null || Filter.Count == 0) && !ItemPersistence.HasMissingFilterReferences(Filter))
			{
				return true;
			}

			_filterSet ??= new HashSet<int>(Filter);
			bool listed = _filterSet.Contains(key.Type);
			return IsWhitelist ? listed : !listed;
		}

		public MEDiskDriveEntity()
		{
			for (int i = 0; i < DiskSlots; i++)
			{
				Disks[i] = new Item();
			}

			Ledger.MaxBytes = 0;
			Ledger.MaxTypes = 0;
			Ledger.MaxAmountPerType = 0;
		}

		public override bool IsTileValidForEntity(int x, int y)
		{
			Tile tile = Main.tile[x, y];
			return MachineTiles.IsEntityAnchor(x, y, ModContent.TileType<MEDiskDriveTile>());
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

		public static bool IsDisk(Item item)
		{
			if (item == null || item.type <= 0)
			{
				return false;
			}

			return ContentSamples.ItemsByType[item.type].ModItem is MEStorageDisk;
		}

		/// <summary>按已提交的磁盘组重算容量。槽位操作先验证替换后的容量。</summary>
		public void RefreshCapacity()
		{
			var capacity = CapacityAfterReplacement(-1, null);
			Ledger.MaxBytes = capacity.Bytes;
			Ledger.MaxTypes = capacity.Types;
			Ledger.MaxAmountPerType = capacity.PerType;
		}

        private (long Bytes, long Types, long PerType) CapacityAfterReplacement(int slot, Item replacement)
        {
            long bytes = 0, types = 0, perType = 0;
            for (int i = 0; i < DiskSlots; i++)
            {
                Item disk = i == slot ? replacement : Disks[i];
                if (disk == null || disk.IsAir || !ContentSamples.ItemsByType.TryGetValue(disk.type, out var sample)
                    || sample.ModItem is not MEStorageDisk storageDisk) continue;
                bytes = StorageAmounts.Add(bytes, storageDisk.CapacityBytes);
                types = StorageAmounts.Add(types, storageDisk.CapacityTypes);
                perType = System.Math.Max(perType, storageDisk.CapacityPerType);
            }
            return (bytes, types, perType);
        }

        public bool CanRemoveDisk(int slot, out string reason) => CanReplaceDisk(slot, new Item(), out reason);
        public bool CanReplaceDisk(int slot, Item replacement, out string reason)
        {
            reason = null;
            if (slot < 0 || slot >= DiskSlots) return false;
            if (replacement != null && !replacement.IsAir && !IsDisk(replacement)) return false;
            var capacity = CapacityAfterReplacement(slot, replacement);
            var currentCapacity = CapacityAfterReplacement(-1, null);
            // An old overfilled drive may be expanded one slot at a time without first emptying it.
            if (Ledger.StoredTypeCount > capacity.Types && capacity.Types < currentCapacity.Types)
            {
                reason = "换盘后类别容量不足，磁盘未移动（已存 " + Ledger.StoredTypeCount + " 类，可容纳 " + capacity.Types + " 类）";
                return false;
            }
            foreach (int type in Ledger.Entries.Keys.Select(key => key.Type).Distinct())
                if (Ledger.GetTypeAmount(type) > capacity.PerType && capacity.PerType < currentCapacity.PerType)
                {
                    reason = "换盘后单类数量上限不足，磁盘未移动；请保留数字奇点或先取出超额物品";
                    return false;
                }
            return true;
        }

		public override void SaveData(TagCompound tag)
		{
			tag["disks"] = new List<Item>(Disks);
			tag["whitelist"] = IsWhitelist;
			ItemPersistence.SaveFilter(tag,"filter",Filter);

			tag["entries"] = ItemPersistence.SaveLedger(Ledger);
		}

		public override void LoadData(TagCompound tag)
		{
			var disks = new Item[DiskSlots];
			for (int i = 0; i < DiskSlots; i++) disks[i] = new Item();
			if (tag.ContainsKey("disks"))
			{
				System.Collections.Generic.IList<Item> saved = tag.GetList<Item>("disks");
				for (int i = 0; i < DiskSlots && i < saved.Count; i++)
				{
					disks[i] = saved[i] ?? new Item();
				}
			}

			// Decode every fallible field first. A bad later item must not replace live disks, filters or stock.
			bool whitelist = tag.ContainsKey("whitelist") && tag.GetBool("whitelist");
			var filter = ItemPersistence.LoadFilter(tag,"filter");
			ItemPersistence.LoadLedger(Ledger, tag);
			System.Array.Copy(disks, Disks, DiskSlots);
			RefreshCapacity();

			// 分区清单(模组被删掉后 ID 会越界,读的时候过滤)
			IsWhitelist = whitelist;
			Filter = filter;

			MarkFilterDirty();
		}
	}
}
