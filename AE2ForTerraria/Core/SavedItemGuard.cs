using Terraria.ModLoader;

namespace AE2ForTerraria.Core
{
	/// <summary>
	/// 存档里物品 ID 的校验。
	///
	/// 账本、CPU 队列、Keep 清单、过滤 / 分区清单都是直接存 `int 物品ID`,
	/// 而玩家随时可能**禁用或删掉某个模组** —— 那些 ID 就会越界,
	/// 下游到处都在 `ContentSamples.ItemsByType[type]` 直接索引,
	/// 结果就是拆方块 / 打开界面时抛异常,或者静默变成另一个物品。
	///
	/// 所以所有 LoadData 都必须先过这里,无效条目直接丢掉。
	/// </summary>
	public static class SavedItemGuard
	{
		/// <summary>这个物品 ID 在当前加载的内容里有效吗。</summary>
		public static bool IsValidType(int type)
		{
			return type > 0 && type < ItemLoader.ItemCount;
		}

		/// <summary>有效性 + 数量过滤。无效时 <paramref name="key"/> 为 None。</summary>
		public static bool TryRead(int type, int prefix, long amount, out ItemKey key)
		{
			key = ItemKey.None;

			if (amount <= 0 || !IsValidType(type))
			{
				return false;
			}

			key = new ItemKey(type, prefix);
			return true;
		}

		/// <summary>过滤清单用:只要 ID 有效就收。</summary>
		public static bool IsValidFilterEntry(int type)
		{
			return IsValidType(type);
		}

		/// <summary>
		/// 安全取最大堆叠。
		/// 下游到处都在 `ContentSamples.ItemsByType[type].maxStack` 直接索引,
		/// 存档里残留的坏 ID 会让它抛异常 —— 统一走这里,坏 ID 当 1 处理。
		/// </summary>
		public static int MaxStackOf(int type)
		{
			if (!IsValidType(type))
			{
				return 1;
			}

			try
			{
				int maxStack = Terraria.ID.ContentSamples.ItemsByType[type].maxStack;
				return maxStack <= 0 ? 1 : maxStack;
			}
			catch
			{
				return 1;
			}
		}
	}
}
