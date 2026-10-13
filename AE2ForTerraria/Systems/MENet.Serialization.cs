using System.Collections.Generic;
using System.IO;
using AE2ForTerraria.Core;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// ME 网络包的**序列化格式**。
	///
	/// 拆出来的目的:改包体的时候只碰这一个文件。
	/// 以前收发、序列化、各子系统的服务器逻辑全挤在 MENet.cs 里,
	/// 改一处很容易碰坏另一处。
	///
	/// 规则:任何格式改动都要把 MENet.ProtocolVersion +1 ——
		/// 显式版本封套校验失败的包会被直接丢弃并打日志。
	/// </summary>
	public static partial class MENet
	{
		private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid collection length");
            return count;
        }
        private static void WriteKey(BinaryWriter writer, ItemKey key)
		{
			writer.Write(key.Type);
			writer.Write(key.Prefix);
            writer.Write(key.Data ?? string.Empty);
		}

		private static ItemKey ReadKey(BinaryReader reader)
		{
			int type = reader.ReadInt32();
			int prefix = reader.ReadInt32();
			string data = reader.ReadString();
            if (data.Length > 4 * 1024 * 1024 || type < 0 || type >= Terraria.ModLoader.ItemLoader.ItemCount
                || prefix < 0 || prefix >= Terraria.ModLoader.PrefixLoader.PrefixCount)
                throw new InvalidDataException("Invalid item identity");
            return new ItemKey(type, prefix, 0, data);
		}

		private static void WriteLedger(BinaryWriter writer, StorageLedger ledger)
		{
			// Capacity metadata uses Int64, including the digital singularity's logical limits.
			writer.Write(ledger.MaxTypes);
			writer.Write(ledger.MaxBytes);
			writer.Write(ledger.MaxAmountPerType);

			// 条目数直接写实际条目数。
			// 以前写的是 StoredTypeCount 的 short 版:超过 32767 种会变负数,
			// 接收端 for 循环次数为负 → 包内后续数据全部错位。
			writer.Write(ledger.Entries.Count);

			foreach (KeyValuePair<ItemKey, long> entry in ledger.Entries)
			{
				WriteKey(writer, entry.Key);
				writer.Write(entry.Value);
			}
		}

		private static void ReadLedgerInto(BinaryReader reader, StorageLedger ledger)
        {
            long maxTypes=reader.ReadInt64(); long maxBytes=reader.ReadInt64(); long maxAmountPerType=reader.ReadInt64(); int count=ReadCount(reader);
            if (maxTypes < 0 || maxBytes < 0 || maxAmountPerType < 0) throw new InvalidDataException("Invalid ledger capacity");
            var decoded=new StorageLedger(ledger.BytesPerType,ledger.AmountPerByte,maxTypes)
                {MaxBytes=maxBytes, MaxTypes=maxTypes, MaxAmountPerType=maxAmountPerType};
            for(int i=0;i<count;i++)
            {
                ItemKey key=ReadKey(reader); long amount=reader.ReadInt64();
                if (!key.IsAir && amount>0) decoded.Restore(key,amount);
            }
            // Publish only after the complete ledger validated; a truncated snapshot leaves the previous view intact.
            ledger.ReplaceContents(decoded); ledger.MaxBytes=decoded.MaxBytes; ledger.MaxTypes=decoded.MaxTypes;
            ledger.MaxAmountPerType=decoded.MaxAmountPerType;
        }

		/// <summary>跳过一份账本(本地没有对应实体时用,保证包内后续数据不错位)。</summary>
		private static void SkipLedger(BinaryReader reader)
		{
			reader.ReadInt64(); // MaxTypes
			reader.ReadInt64(); // MaxBytes
			reader.ReadInt64(); // MaxAmountPerType
			int count = ReadCount(reader);

			if (count < 0)
			{
				return;
			}

			for (int i = 0; i < count; i++)
			{
				ReadKey(reader);
				reader.ReadInt64(); // amount
			}
		}
	}
}
