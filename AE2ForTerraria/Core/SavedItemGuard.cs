using Terraria.ModLoader;

namespace AE2ForTerraria.Core
{
	/// <summary>
	/// 当前运行期及旧版数值格式的物品 ID 边界校验。
	/// 当前存储、任务与过滤持久化由 ItemPersistence 保存稳定的 ItemIO 身份。
	/// 缺失模组内容必须保留原身份，不能依据此数值校验删除存档记录。
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

        /// <summary>纯 ID 选择器不能识别缺失物品的原身份，禁止添加裸占位类型。</summary>
        public static bool IsSelectableFilterType(int type)
            => IsValidType(type) && ItemLoader.GetItem(type) is not Terraria.ModLoader.Default.UnloadedItem;

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
