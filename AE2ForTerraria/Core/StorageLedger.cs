using System.Collections.Generic;

namespace AE2ForTerraria.Core
{
	/// <summary>
	/// AE2 式存储账本:身份 -> 数量。
	/// 关键点:容量按「类型数」和「总数」两条轴计费,所以不可堆叠物品只占一个类别,
	/// 不受物品 maxStack 限制 —— 这是整个存储系统的地基。
	/// </summary>
	public sealed class StorageLedger
	{
		private Dictionary<ItemKey, long> _amounts = new Dictionary<ItemKey, long>();
		private Dictionary<int, long> _typeTotals = new();

		/// <summary>每占用一种类型消耗的字节。</summary>
		public long BytesPerType { get; set; }

		/// <summary>多少数量折算 1 字节。</summary>
		public long AmountPerByte { get; set; }

		/// <summary>本单元最多能容纳多少种类型(对应 AE2 的 63 类)。</summary>
		public long MaxTypes { get; set; }

		/// <summary>总字节预算。由插进去的磁盘提供容量;long.MaxValue 表示不限。</summary>
		public long MaxBytes { get; set; } = long.MaxValue;

		/// <summary>Aggregate limit for all identities of one base item type; old excess stock remains lossless.</summary>
		public long MaxAmountPerType { get; set; } = long.MaxValue;

		public StorageLedger(long bytesPerType = 8, long amountPerByte = 8, long maxTypes = 63)
		{
			BytesPerType = bytesPerType <= 0 ? 1 : bytesPerType;
			AmountPerByte = amountPerByte <= 0 ? 1 : amountPerByte;
			MaxTypes = maxTypes <= 0 ? 1 : maxTypes;
		}

		public int StoredTypeCount => _typeTotals.Count;
		public long GetTypeAmount(int type) => _typeTotals.GetValueOrDefault(type);
		public bool ContainsType(int type) => _typeTotals.ContainsKey(type);

		public long StoredTotalAmount { get; private set; }

		public long UsedBytes => (long)System.Math.Min(long.MaxValue,
			(decimal)StoredTypeCount * System.Math.Max(1, BytesPerType)
			+ System.Math.Ceiling((decimal)StoredTotalAmount / System.Math.Max(1, AmountPerByte)));

		public IReadOnlyDictionary<ItemKey, long> Entries => _amounts;

		/// <summary>
		/// 内容版本号:任何一次成功的存取 / 清空都会 +1。
		///
		/// 界面(驱动器 / 存储单元的内容面板)以前是**每帧**重建整个列表 + 排序,
		/// 几千种物品时窗口开着就一直烧 CPU。有了版本号,界面只在
		/// 「版本 + 搜索词 + 排序模式」真的变了才重建,其余帧只更新容量那一行。
		/// </summary>
		public int Version { get; private set; }

		public long GetAmount(ItemKey key)
		{
			return _amounts.TryGetValue(key, out long amount) ? amount : 0L;
		}

		public bool Contains(ItemKey key)
		{
			return _amounts.TryGetValue(key, out long amount) && amount > 0;
		}

		/// <summary>返回实际存入的数量。simulate = 只试算不落账(对应 AE2 的 SIMULATE)。</summary>
		public long Insert(ItemKey key, long amount, bool simulate = false)
		{
			if (key.IsAir || amount <= 0 || StoredTypeCount > MaxTypes)
			{
				return 0;
			}

			long current = GetAmount(key);
			long typeTotal = GetTypeAmount(key.Type);
			amount = System.Math.Min(amount, System.Math.Max(0, MaxAmountPerType - typeTotal));
			amount = System.Math.Min(amount, long.MaxValue - StoredTotalAmount);
			if (typeTotal <= 0 && StoredTypeCount >= MaxTypes)
			{
				return 0; // 类型已满,开不了新类别
			}

			// 字节预算:先算这个身份要占的字节,超出就只存得下的部分
			if (MaxBytes != long.MaxValue)
			{
				decimal typeBytes = ((decimal)StoredTypeCount + (typeTotal > 0 ? 0 : 1))
					* System.Math.Max(1, BytesPerType);
				long capacityItems = (long)System.Math.Clamp(
					((decimal)MaxBytes - typeBytes) * System.Math.Max(1, AmountPerByte)
					- StoredTotalAmount, 0, long.MaxValue);
				if (capacityItems <= 0)
				{
					return 0;
				}

				if (amount > capacityItems)
				{
					amount = capacityItems;
				}
			}

			if (simulate || amount <= 0)
			{
				return amount;
			}

			_amounts[key] = current + amount;
			_typeTotals[key.Type] = typeTotal + amount;
			StoredTotalAmount += amount;
			Version++;
			return amount;
		}

		/// <summary>返回实际取出的数量。</summary>
		public long Extract(ItemKey key, long amount, bool simulate = false)
		{
			if (amount <= 0)
			{
				return 0;
			}

			long current = GetAmount(key);
			long taken = current < amount ? current : amount;
			if (taken <= 0 || simulate)
			{
				return taken;
			}

			long left = current - taken;
			if (left <= 0)
			{
				_amounts.Remove(key);
			}
			else
			{
				_amounts[key] = left;
			}

			StoredTotalAmount -= taken;
			long typeLeft = GetTypeAmount(key.Type) - taken;
			if (typeLeft == 0) _typeTotals.Remove(key.Type);
			else _typeTotals[key.Type] = typeLeft;
			Version++;
			return taken;
		}

		/// <summary>把实际物品栏里的一格吞进账本(掉落直收/导入用)。</summary>
		public long InsertItem(Terraria.Item item)
		{
			if (item == null || item.type <= 0 || item.stack <= 0)
			{
				return 0;
			}

			long stored = Insert(ItemKey.FromItem(item), item.stack);
			if (stored > 0)
			{
				item.stack -= (int)stored;
				if (item.stack <= 0) item.TurnToAir();
			}

			return stored;
		}

		public void Clear()
		{
			if (_amounts.Count > 0)
			{
				Version++;
			}

			_amounts.Clear();
			_typeTotals.Clear();
			StoredTotalAmount = 0;
		}

		/// <summary>Restore authoritative data without applying today's capacity/partition limits.</summary>
		public void Restore(ItemKey key, long amount)
		{
			if (key.IsAir || amount <= 0) return;
			if (amount > long.MaxValue - StoredTotalAmount)
				throw new System.IO.InvalidDataException("Storage quantity overflow");
			_amounts[key] = GetAmount(key) + amount;
			_typeTotals[key.Type] = GetTypeAmount(key.Type) + amount;
			StoredTotalAmount += amount;
			Version++;
		}

		public StorageLedger Copy()
		{
			var copy = new StorageLedger(BytesPerType, AmountPerByte, MaxTypes)
				{ MaxBytes = MaxBytes, MaxTypes = MaxTypes, MaxAmountPerType = MaxAmountPerType };
			foreach (var entry in _amounts) copy.Restore(entry.Key, entry.Value);
			return copy;
		}

        /// <summary>Publish a completely decoded save without exposing a cleared or partially restored ledger.</summary>
        internal void ReplaceContents(StorageLedger decoded)
        {
            // Allocate before touching this ledger; even a failed allocation leaves the previous contents intact.
            var replacement = new Dictionary<ItemKey, long>(decoded._amounts);
            var typeTotals = new Dictionary<int, long>(decoded._typeTotals);
            _amounts = replacement;
            _typeTotals = typeTotals;
            StoredTotalAmount = decoded.StoredTotalAmount;
            Version++;
        }
	}
}
