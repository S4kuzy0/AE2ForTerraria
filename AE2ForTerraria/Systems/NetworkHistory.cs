using System.Collections.Generic;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 网络容量的历史采样。
	///
	/// 概览里显示的占用 / 类型数 / 件数都是「当前值」,做容量规划时看不出趋势。
	/// 这里每分钟给每个网络拍一张快照,保留最近 12 张(约 12 分钟窗口),
	/// 概览里就能显示「最近 N 分钟 +X 字节」。
	///
	/// 采样频率刻意低:它是**给玩家看的趋势**,不是精确统计。
	/// </summary>
	public sealed class NetworkHistory : ModSystem
	{
		private const int SampleIntervalTicks = 3600; // 1 分钟
		private const int MaxSamples = 12;

		private struct Sample
		{
			public long UsedBytes;
			public long Types;
			public long Items;
		}

		private static readonly Dictionary<string, List<Sample>> Samples =
			new Dictionary<string, List<Sample>>();

		private static int _timer;

		private static string Key(Point16 anchor)
		{
			return anchor.X + "," + anchor.Y;
		}

		public override void ClearWorld()
		{
			Samples.Clear();
			_timer = 0;
		}

		public override void PostUpdateWorld()
		{
			if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
			{
				return;
			}

			if (++_timer < SampleIntervalTicks)
			{
				return;
			}

			_timer = 0;
			TakeSample();
		}

		private static void TakeSample()
		{
			// 注意:不能在这里 Clear —— 那样每个网络永远只剩 1 个采样点,历史就没意义了。
			// 本轮出现的网络记一笔;没出现的(被拆掉/改名)从表里删掉。
			var seen = new HashSet<string>();

			foreach (List<Point16> blocks in MENetwork.CollectAllNetworks())
			{
				Point16 anchor = MENetwork.CanonicalAnchor(blocks);
				var sample = new Sample();

				foreach (Point16 point in blocks)
				{
					if (TileEntity.ByPosition.TryGetValue(point, out TileEntity entity)
						&& entity is IMEStorage storage)
					{
						sample.UsedBytes = StorageAmounts.Add(sample.UsedBytes, storage.Ledger.UsedBytes);
						sample.Types = StorageAmounts.Add(sample.Types, storage.Ledger.StoredTypeCount);
						sample.Items = StorageAmounts.Add(sample.Items, storage.Ledger.StoredTotalAmount);
					}
				}

				string key = Key(anchor);
				seen.Add(key);

				if (!Samples.TryGetValue(key, out List<Sample> list))
				{
					list = new List<Sample>();
					Samples[key] = list;
				}

				list.Add(sample);

				while (list.Count > MaxSamples)
				{
					list.RemoveAt(0);
				}
			}

			var stale = new List<string>();

			foreach (string key in Samples.Keys)
			{
				if (!seen.Contains(key))
				{
					stale.Add(key);
				}
			}

			foreach (string key in stale)
			{
				Samples.Remove(key);
			}
		}

		/// <summary>最近一次采样到现在的差值;没有历史时返回 false。</summary>
		public static bool TryGetDelta(Point16 anchor, out long bytes, out long items, out int minutes)
		{
			bytes = 0;
			items = 0;
			minutes = 0;

			if (!Samples.TryGetValue(Key(anchor), out List<Sample> list) || list.Count < 2)
			{
				return false;
			}

			Sample oldest = list[0];
			Sample newest = list[list.Count - 1];

			bytes = newest.UsedBytes - oldest.UsedBytes;
			items = newest.Items - oldest.Items;
			minutes = list.Count - 1;
			return true;
		}

		public override void SaveWorldData(TagCompound tag)
		{
			var list = new List<TagCompound>();

			foreach (KeyValuePair<string, List<Sample>> pair in Samples)
			{
				var entry = new TagCompound();
				entry["k"] = pair.Key;

				var samples = new List<TagCompound>();

				foreach (Sample sample in pair.Value)
				{
					var one = new TagCompound();
					one["b"] = sample.UsedBytes;
					one["t"] = sample.Types;
					one["i"] = sample.Items;
					samples.Add(one);
				}

				entry["s"] = samples;
				list.Add(entry);
			}

			tag["networkHistory"] = list;
		}

		public override void LoadWorldData(TagCompound tag)
		{
			Samples.Clear();

			if (!tag.ContainsKey("networkHistory"))
			{
				return;
			}

			foreach (TagCompound entry in tag.GetList<TagCompound>("networkHistory"))
			{
				string key = entry.GetString("k");

				if (string.IsNullOrEmpty(key))
				{
					continue;
				}

				var list = new List<Sample>();

				foreach (TagCompound one in entry.GetList<TagCompound>("s"))
				{
					list.Add(new Sample
					{
						UsedBytes = System.Math.Max(0, TagCompat.ReadLong(one, "b")),
						Types = System.Math.Max(0, TagCompat.ReadLong(one, "t")),
						Items = System.Math.Max(0, TagCompat.ReadLong(one, "i"))
					});
				}

				Samples[key] = list;
			}
		}
	}
}
