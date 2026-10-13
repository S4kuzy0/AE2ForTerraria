using System.Collections.Generic;
using System.IO;
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
	/// 存储单元:固定容量的存储方块,不需要磁盘。
	/// 容量约等于一块 8K 磁盘,比磁盘方案方便但上限低。
	/// </summary>
	public class MEStorageUnitEntity : ModTileEntity, IMEStorage, IPartitionedStorage
	{
		/// <summary>最高等级(每升一级容量翻两番)。</summary>
		public const int MaxLevel = 3;

		/// <summary>各等级的字节容量:8K / 32K / 128K。</summary>
		public static readonly long[] LevelCapacity = { 8192L, 32768L, 131072L };

		/// <summary>类型位上限(字节容量由 LevelCapacity 决定)。</summary>
		public const int CapacityTypes = 24;

		/// <summary>当前等级(1..3),存在 NBT 里。</summary>
		public int Level { get; private set; } = 1;

		public StorageLedger Ledger { get; } = new StorageLedger(8, 8, CapacityTypes);

		/// <summary>分区清单:空 = 不过滤。用法和驱动器一致。</summary>
		public List<int> Filter { get; set; } = new List<int>();

		/// <summary>true = 白名单,false = 黑名单。</summary>
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

		public MEStorageUnitEntity()
		{
			ApplyLevel();
		}

		/// <summary>把当前等级换算成容量。</summary>
		private void ApplyLevel()
		{
			int index = Level - 1;
			if (index < 0)
			{
				index = 0;
			}

			if (index >= LevelCapacity.Length)
			{
				index = LevelCapacity.Length - 1;
			}

			Ledger.MaxBytes = LevelCapacity[index];
		}

		/// <summary>升一级。返回 false 表示已经满级。</summary>
		public bool TryUpgrade()
		{
			if (Level >= MaxLevel)
			{
				return false;
			}

			Level++;
			ApplyLevel();
			return true;
		}

		public long CurrentCapacity
		{
			get
			{
				int index = Level - 1;
				if (index < 0)
				{
					index = 0;
				}

				if (index >= LevelCapacity.Length)
				{
					index = LevelCapacity.Length - 1;
				}

				return LevelCapacity[index];
			}
		}

		public override bool IsTileValidForEntity(int x, int y)
		{
			Tile tile = Main.tile[x, y];
			return MachineTiles.IsEntityAnchor(x, y, ModContent.TileType<MEStorageUnitTile>());
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

		public override void SaveData(TagCompound tag)
		{
			tag["entries"] = ItemPersistence.SaveLedger(Ledger);
			tag["level"] = Level;
			tag["whitelist"] = IsWhitelist;
			ItemPersistence.SaveFilter(tag,"filter",Filter);
		}

        // Initial section/TileEntitySharing sync carries public machine metadata only.
        // Inventory contents continue to use the permission-checked ME snapshots.
        public override void NetSend(BinaryWriter writer) => writer.Write((byte)Level);

        public override void NetReceive(BinaryReader reader)
        {
            int level = reader.ReadByte();
            if (level < 1 || level > MaxLevel)
                throw new InvalidDataException("Invalid ME storage unit level");
            Level = level;
            ApplyLevel();
        }

		public override void LoadData(TagCompound tag)
		{
			int level = System.Math.Clamp(TagCompat.ReadInt(tag, "level", 1), 1, MaxLevel);
			bool whitelist = tag.ContainsKey("whitelist") && tag.GetBool("whitelist");
			var filter = ItemPersistence.LoadFilter(tag,"filter");
			ItemPersistence.LoadLedger(Ledger, tag);
			Level = level;
			ApplyLevel();

			// 分区清单(模组被删掉后 ID 会越界,读的时候过滤)
			IsWhitelist = whitelist;
			Filter = filter;

			MarkFilterDirty();
		}
	}
}
