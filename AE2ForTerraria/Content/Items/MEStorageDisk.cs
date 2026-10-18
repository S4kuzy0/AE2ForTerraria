using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>
	/// 存储磁盘基类。磁盘本身不保存物品 —— 它只提供容量,
	/// 真正的物品存在磁盘驱动器方块里,所以换磁盘不会丢东西。
	/// </summary>
	public abstract class MEStorageDisk : ModItem
	{
		/// <summary>Disks no longer charge bytes; retained for capacity compatibility with fixed storage units.</summary>
		public virtual long CapacityBytes => long.MaxValue;

		/// <summary>Base item type capacity; physical dictionaries and snapshots remain resource bounded.</summary>
		public abstract long CapacityTypes { get; }

		/// <summary>All identities of one base item type share this quantity limit; actual identities stay separate.</summary>
		public virtual long CapacityPerType => int.MaxValue;
	}
}
