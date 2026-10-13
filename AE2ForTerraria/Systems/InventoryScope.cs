using System;
using Terraria;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 「背包」到底指哪几格。
	///
	/// 泰拉瑞亚的 Player.inventory 有 59 格,不是全部都是背包:
	///   0–49  主背包
	///   50–53 弹药栏
	///   54–57 钱币栏
	///   58    手持/其它(不应该被搬)
	///
	/// 以前「存入全部」扫的是整个 inventory.Length,于是会顺手把子弹和钱一起塞进 ME。
	/// 现在统一走这里:默认只扫主背包,想连弹药 / 钱币一起存要在配置里打开。
	///
	/// 注意:ME 的存储本身**不受 maxStack 限制**(身份 + 数量分开存),
	/// 所以不可堆叠的物品在里面也只占一个类别 —— 这一条不受这里的范围影响。
	/// </summary>
	public static class InventoryScope
	{
		/// <summary>主背包格子数(0–49)。</summary>
		public const int MainSlots = 50;

		/// <summary>主背包 + 弹药栏 + 钱币栏 的格子数(0–57)。</summary>
		public const int ExtendedSlots = 58;

		/// <summary>要扫描多少个背包格子。</summary>
		public static int DepositSlotCount(Player player, bool includeAmmoAndCoins)
		{
			int total = player?.inventory?.Length ?? 0;

			return includeAmmoAndCoins
				? Math.Min(ExtendedSlots, total)
				: Math.Min(MainSlots, total);
		}

		/// <summary>配置里是否允许连弹药 / 钱币一起存。</summary>
		public static bool IncludeAmmoAndCoinsByDefault
		{
			get
			{
				try
				{
					MEConfig config = MEConfig.Instance;
					return config != null && config.DepositIncludesAmmoAndCoins;
				}
				catch
				{
					return false;
				}
			}
		}
	}
}
