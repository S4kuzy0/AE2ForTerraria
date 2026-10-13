using AE2ForTerraria.Core;
using Terraria;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 带词缀的显示名。
	///
	/// 存储身份是「物品ID + 前缀」,所以「传奇 泰拉刃」和「泰拉刃」在网络里是**两份**东西。
	/// 界面必须把词缀显示出来,否则玩家会看到两个一模一样的格子、还搞不懂为什么数量对不上。
	/// </summary>
	public static class UIItemNames
	{
		public static string Display(ItemKey key)
		{
			if (key.IsAir)
			{
				return string.Empty;
			}

			return Display(key.Type, key.Prefix);
		}

		public static string Display(int type, int prefix)
		{
			string name = Lang.GetItemNameValue(type);
			if (string.IsNullOrWhiteSpace(name))
			{
				name = "#" + type;
			}

			string prefixName = PrefixName(prefix);
			return prefixName == null ? name : prefixName + " " + name;
		}

		/// <summary>词缀的本地化名(没有词缀返回 null)。</summary>
		public static string PrefixName(int prefix)
		{
			if (prefix <= 0)
			{
				return null;
			}

			try
			{
				// 1.4 里 Lang.prefix 是本地化文本数组,越界或空都当作没有
				if (prefix < Lang.prefix.Length)
				{
					string value = Lang.prefix[prefix]?.Value;
					if (!string.IsNullOrWhiteSpace(value))
					{
						return value;
					}
				}
			}
			catch
			{
				// 取不到就退回只显示物品名
			}

			return null;
		}

		/// <summary>
		/// 这个物品来自哪个模组(原版返回 "Terraria")。
		/// 浏览器里用来做「按模组筛选」和悬停提示。
		/// </summary>
		public static string ModNameOf(int type)
		{
			if (type <= 0)
			{
				return null;
			}

			try
			{
				Terraria.ModLoader.ModItem modItem = Terraria.ModLoader.ItemLoader.GetItem(type);
				return modItem?.Mod?.Name ?? "Terraria";
			}
			catch
			{
				return null;
			}
		}

		/// <summary>把词缀应用到实际物品上(取出来的时候用)。</summary>
		public static void ApplyPrefix(Item item, int prefix)
		{
			if (item == null || prefix <= 0)
			{
				return;
			}

			try
			{
				item.Prefix(prefix);
			}
			catch
			{
				// 个别词缀可能不允许套在这个物品上 —— 那就保持无词缀,不要崩
			}
		}
	}
}
