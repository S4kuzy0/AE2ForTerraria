using System.Collections.Generic;

namespace AE2ForTerraria.Core
{
	/// <summary>
	/// 能提供存储容量的方块实体(ME 磁盘驱动器、ME 存储单元)。
	/// 网络只需要这两样东西:一本账本。
	/// </summary>
	public interface IMEStorage
	{
		StorageLedger Ledger { get; }

		/// <summary>
		/// 这个存储方块收不收这种物品(分区清单)。
		/// 默认实现是「全收」;驱动器 / 存储单元可以覆写成白/黑名单。
		/// 存入时被拒绝的物品会落到网络里**其它**愿意收的存储方块上。
		/// </summary>
		bool Allows(ItemKey key);
	}

	/// <summary>
	/// 带分区清单的存储方块(驱动器 / 存储单元)。
	/// 抽出来是为了让「改分区」的网络同步只有一条代码路径。
	/// </summary>
	public interface IPartitionedStorage
	{
		List<int> Filter { get; set; }

		bool IsWhitelist { get; set; }

		void MarkFilterDirty();
	}
}
