using System;
using System.Collections.Generic;
using System.Diagnostics;
using AE2ForTerraria.Content.Achievements;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// 独立合成 CPU:持有合成队列。
	/// 分子装配室会优先从队列里取任务执行;队列里的任务可以排队、看进度、取消。
	/// </summary>
	public class MECraftingCpuEntity : ModTileEntity
	{
		/// <summary>旧版页面/回归夹具的大小；不再作为队列上限。</summary>
		public const int MaxJobs = 24;

		/// <summary>旧版页面大小；不再作为保持库存上限。</summary>
		public const int MaxKeep = 12;

		public sealed class Job
		{
            public string Id=Guid.NewGuid().ToString("N");
			public ItemKey Key;
			public long Remaining;
			public long Total;

			/// <summary>下单的玩家名(空 = 没指定,产出进网络)。</summary>
			public string RequestedBy;

            public bool Paused;
            public string LastStatus = "等待处理";

			public long Done => Total - Remaining;
		}

		/// <summary>「保持库存」一项:网络里少于 Target 就自动补货。</summary>
		public sealed class KeepEntry
		{
			public ItemKey Key;
			public long Target;
		}

		public readonly List<Job> Jobs = new List<Job>();

		/// <summary>保持库存清单。装配室空闲时会按它自动补货。</summary>
		public readonly List<KeepEntry> Keep = new List<KeepEntry>();

		/// <summary>暂停后不再从队列取任务(已经排在队列里的东西不会丢)。</summary>
		public bool Paused;

		/// <summary>
		/// 产出目的地:false = 进网络(默认),true = 交给下单的玩家。
		/// 「保持库存」的补货**永远进网络**(否则补货就没意义了)。
		/// </summary>
		public bool OutputToInventory;

		public string LastStatus = ModText.Safe("LabelIdle", "空闲");

		/// <summary>
		/// 正在被分子装配室执行的那个任务(没有就是 null)。
		/// 界面用它把「正在合成」那一条标出来并显示进度。
		/// </summary>
		public Job ActiveJob;

		private int _keepTimer;
        private int _keepCursor;

		public override bool IsTileValidForEntity(int x, int y)
            => MachineTiles.IsEntityAnchor(x,y,ModContent.TileType<MECraftingCpuTile>());

		public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
		{
            var origin=MachineTiles.Origin(i,j);i=origin.X;j=origin.Y;
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				NetMessage.SendTileSquare(Main.myPlayer, i, j, 1);
				NetMessage.SendData(MessageID.TileEntityPlacement, -1, -1, null, i, j, Type);
				return -1;
			}

			return Place(i, j);
		}

		/// <summary>兼容入口：同身份、同请求人的任务在数值允许时合并，不限制队列长度。</summary>
		public bool Enqueue(ItemKey key, long count, string requestedBy = null)
            => TryEnqueue(key, count, requestedBy, true, out _);

        internal bool TryEnqueue(ItemKey key, long count, string requestedBy, bool merge, out Job job)
		{
            job = null;
			if (!SavedItemGuard.IsValidType(key.Type) || key.Prefix != 0 || key.Data != null || count <= 0)
			{
				return false;
			}

            if (merge)
                foreach (Job existing in Jobs)
                {
                    if (!existing.Key.Equals(key) || !string.Equals(existing.RequestedBy ?? "", requestedBy ?? "", StringComparison.Ordinal)) continue;
                    // Preserve the full numeric range without overflowing a merged order.
                    if (existing.Total > long.MaxValue - count || existing.Remaining > long.MaxValue - count) continue;
                    existing.Remaining += count; existing.Total += count;
                    job = existing;
                    return true;
                }

			Jobs.Add(job = new Job
			{
				Key = key,
				Remaining = count,
				Total = count,
				RequestedBy = requestedBy
			});
			return true;
		}

		/// <summary>设置 / 更新「保持库存」。target &lt;= 0 表示移除。</summary>
		public bool SetKeep(ItemKey key, long target)
		{
            if (target <= 0)
            {
                Keep.RemoveAll(entry => entry.Key.Equals(key));
                return true;
            }
			if (!SavedItemGuard.IsValidType(key.Type) || key.Prefix != 0 || key.Data != null)
			{
				return false;
			}

			for (int i = 0; i < Keep.Count; i++)
			{
				if (Keep[i].Key.Equals(key))
				{
					Keep[i].Target = target;
					return true;
				}
			}

			Keep.Add(new KeepEntry { Key = key, Target = target });

			// 成就也挂在数据层:不管从终端还是别处设置,只要设成了就算
			AchievementTriggers.Trigger<KeepStockAchievement>();

			if (Keep.Count >= 5)
			{
				AchievementTriggers.Trigger<KeepFiveAchievement>();
			}

			return true;
		}

		public override void Update()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || Paused || Keep.Count == 0)
			{
				return;
			}

			if (++_keepTimer < 30)
			{
				return;
			}

			_keepTimer = 0;
			TopUpKeepStock();
		}

		/// <summary>
		/// 按「保持库存」把不足的部分排进队列。
		/// 只负责排队 —— 真正的合成还是走装配室那套,不用维护第二份逻辑。
		/// </summary>
		private void TopUpKeepStock()
		{
			List<IMEStorage> storage = MENetwork.CollectStorage(Position.X, Position.Y);
			if (storage.Count == 0)
			{
				return;
			}

			Dictionary<ItemKey, long> totals = MENetwork.Aggregate(storage);
            var hosts=new List<MECraftingCpuEntity>();
            foreach (var point in MENetwork.Collect(Position.X,Position.Y))
                if (TileEntity.ByPosition.TryGetValue(point,out var entity) && entity is MECraftingCpuEntity cpu) hosts.Add(cpu);

            long started = Stopwatch.GetTimestamp();
            int attempts = Math.Min(Keep.Count, TerminalCraftingExecutor.CandidateBudget);
			for (int attempt = 0; attempt < attempts; attempt++)
			{
                if (attempt > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= TerminalCraftingExecutor.FrameBudgetMilliseconds) break;
                KeepEntry entry = Keep[(_keepCursor++ & int.MaxValue) % Keep.Count];

				long have = totals.TryGetValue(entry.Key, out long amount) ? amount : 0;

				// 队列里已经排了同一个物品的话,把它算作「在路上」,避免重复排队
				foreach (var host in hosts)
                foreach (Job job in host.Jobs)
				{
					if (job.Key.Equals(entry.Key) && (!host.OutputToInventory || string.IsNullOrEmpty(job.RequestedBy)))
					{
							have = StorageAmounts.Add(have, job.Remaining);
					}
				}

				if (have >= entry.Target)
				{
					continue;
				}

                // Missing content remains a durable target, but cannot block other replenishment.
                Enqueue(entry.Key, entry.Target - have);
			}
		}

		public Job Peek()
		{
			return Jobs.Count > 0 ? Jobs[0] : null;
		}

		/// <summary>把队列里的一项上移 / 下移 / 置顶(delta = -1 上移,+1 下移;-999 置顶)。</summary>
		public bool MoveJob(Job job, int delta)
		{
			if (job == null)
			{
				return false;
			}

			int index = Jobs.IndexOf(job);
			if (index < 0)
			{
				return false;
			}

			int target = delta <= -999 ? 0 : index + delta;
			target = Math.Clamp(target, 0, Jobs.Count - 1);

			if (target == index)
			{
				return false;
			}

			Jobs.RemoveAt(index);
			Jobs.Insert(target, job);
			return true;
		}

        public Job FindJob(string id) => string.IsNullOrEmpty(id) ? null : Jobs.Find(job=>job.Id==id);

		public void DropJob(Job job)
		{
			if (job == null)
			{
				return;
			}

			Jobs.Remove(job);

			// 正在执行的那一条被取消时,必须把 ActiveJob 一起清掉 ——
			// 否则装配室手里还攥着这个 Job 引用,会把这「最后一件」做完
			// (界面上已经没有它了,看起来就像取消无效)。
			if (ReferenceEquals(ActiveJob, job))
			{
				ActiveJob = null;
			}
		}

		public void ClearQueue()
		{
			Jobs.Clear();
			ActiveJob = null;
			LastStatus = ModText.Safe("StatusQueueCleared", "队列已清空");
		}

		public override void SaveData(TagCompound tag)
		{
			tag["paused"] = Paused;

			var list = new List<TagCompound>();

			foreach (Job job in Jobs)
			{
				var entry = new TagCompound();
				ItemPersistence.WriteKey(entry, job.Key);
                entry["type"] = job.Key.Type;
				entry["prefix"] = job.Key.Prefix;
				entry["jobId"] = job.Id;
                entry["remaining"] = job.Remaining;
				entry["jobPaused"] = job.Paused;
				entry["total"] = job.Total;
				entry["by"] = job.RequestedBy ?? string.Empty;
				list.Add(entry);
			}

			tag["jobs"] = list;
			tag["outputToInventory"] = OutputToInventory;

			var keepList = new List<TagCompound>();

			foreach (KeepEntry entry in Keep)
			{
				var keepTag = new TagCompound();
				ItemPersistence.WriteKey(keepTag, entry.Key);
                keepTag["type"] = entry.Key.Type;
				keepTag["prefix"] = entry.Key.Prefix;
				keepTag["target"] = entry.Target;
				keepList.Add(keepTag);
			}

			tag["keep"] = keepList;
		}

		public override void LoadData(TagCompound tag)
		{
			Jobs.Clear();
			Keep.Clear();
			Paused = tag.ContainsKey("paused") && tag.GetBool("paused");
			OutputToInventory = tag.ContainsKey("outputToInventory") && tag.GetBool("outputToInventory");

			// 注意:以前这里在「没有 jobs 键」时会直接 return,
			// 于是 Keep 清单会被一起跳过 —— 现在两个清单各判各的。
			if (tag.ContainsKey("jobs"))
			{
				foreach (TagCompound entry in tag.GetList<TagCompound>("jobs"))
				{
					// 模组被删掉后 ID 会越界 —— 无效条目直接丢
					ItemKey key = ItemPersistence.ReadKey(entry);
                    if (key.IsAir)
					{
						continue;
					}

					Jobs.Add(new Job
					{
						Id = entry.ContainsKey("jobId") ? entry.GetString("jobId") : Guid.NewGuid().ToString("N"),
                        Key = key,
						Remaining = TagCompat.ReadLong(entry, "remaining"),
						Total = TagCompat.ReadLong(entry, "total"),
						RequestedBy = entry.ContainsKey("by") ? entry.GetString("by") : null,
                        Paused = entry.ContainsKey("jobPaused") && entry.GetBool("jobPaused")
					});
				}
			}

			if (tag.ContainsKey("keep"))
			{
				foreach (TagCompound entry in tag.GetList<TagCompound>("keep"))
				{
					ItemKey key = ItemPersistence.ReadKey(entry);
                    if (key.IsAir)
					{
						continue;
					}

					Keep.Add(new KeepEntry { Key = key, Target = TagCompat.ReadLong(entry, "target") });
				}
			}
		}
	}
}
