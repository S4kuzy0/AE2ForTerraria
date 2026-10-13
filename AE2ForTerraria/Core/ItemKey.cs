using System;
using Terraria;

namespace AE2ForTerraria.Core
{
    /// <summary>Stack-independent identity: runtime type, prefix and normalized ItemIO data. Persistence uses stable mod names.</summary>
	public readonly struct ItemKey : IEquatable<ItemKey>
	{
		public static readonly ItemKey None = new ItemKey(0, 0, 0);

		public readonly int Type;
		public readonly int Prefix;

		/// <summary>预留,当前恒为 0(见类型注释)。</summary>
		public readonly int Variant;

		// Serialized, stack-independent ItemIO data. Kept on the key, not in an unbounded global registry.
		public readonly string Data;

		public ItemKey(int type, int prefix = 0, int variant = 0, string data = null)
		{
			Type = type;
			Prefix = prefix;
			Variant = variant;
			Data = string.IsNullOrEmpty(data) ? null : data;
		}

		/// <summary>从实际物品取身份。数量(stack)刻意不参与。</summary>
		public static ItemKey FromItem(Item item)
		{
			return ItemPersistence.KeyOf(item);
		}

		/// <summary>合成产物一律视为无前缀(重铸/宝物才带前缀)。</summary>
		public static ItemKey Crafted(int type)
		{
			return new ItemKey(type, 0, 0);
		}

		public bool IsAir => Type <= 0;

		public ItemKey WithoutPrefix()
		{
			return Crafted(Type);
		}

		public bool Equals(ItemKey other)
		{
			return Type == other.Type && Prefix == other.Prefix && Variant == other.Variant
				&& string.Equals(Data, other.Data, StringComparison.Ordinal);
		}

		public override bool Equals(object obj)
		{
			return obj is ItemKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = Type;
				hash = (hash * 397) ^ Prefix;
				hash = (hash * 31) ^ Variant;
				hash = (hash * 31) ^ (Data?.GetHashCode(StringComparison.Ordinal) ?? 0);
				return hash;
			}
		}

		public Item CreateItem(int stack = 1) => ItemPersistence.Create(this, stack);

		public override string ToString()
		{
			return Prefix == 0 ? "item#" + Type : "item#" + Type + "@" + Prefix;
		}
	}
}
