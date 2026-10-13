using System;
using System.Collections.Generic;
using System.Diagnostics;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using AE2ForTerraria.Content.Achievements;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// 分子装配室 + 合成 CPU 的集合体:一个方块就能完成整条自动合成。
	/// 往里放工作站(工作台/铁砧/熔炉…)才能跑需要该工作站的配方;
	/// 不需要写样板 —— 只要配方条件满足且原料齐全,就能自动制造。
	/// 如果网络里接了「独立合成 CPU」,装配室会优先执行它的合成队列。
	/// </summary>
	public class MEMolecularAssemblerEntity : ModTileEntity
	{
		/// <summary>一排几个工作站槽位。</summary>
		public const int SlotsPerRow = 5;

		/// <summary>最多几排(放满一排就会出现下一排)。</summary>
		public const int MaxRows = 6;

		/// <summary>槽位总容量。</summary>
		public const int MaxStationSlots = SlotsPerRow * MaxRows;

		/// <summary>
		/// 空闲时多久尝试选一次任务(聚合库存 + 规划器很贵,不适合每 tick 跑)。
		/// 进度推进是每 tick 的,只有「选任务 / 重新规划」才按这个间隔。
		/// </summary>
		private const int ReselectIntervalTicks = 10;

        // Continue a stable scan next time instead of planning every queue in a large merged network at once.
        private const int MaxCandidatePlansPerSelection = 64;
        private const double CandidatePlanningBudgetMilliseconds = 8;
        private int _nextCpuIndex;
        private int _nextJobIndex;
        private long _scanQueueSignature = long.MinValue;

		public readonly Item[] Stations = new Item[MaxStationSlots];

		public ItemKey Target = ItemKey.None;

		/// <summary>界面显示用的一句话状态。</summary>
		public string LastStatus; // 构造时用本地化文案初始化

		/// <summary>当前正在合成的物品(AE2 里相当于 CPU 正在跑的那个任务)。</summary>
		public ItemKey ActiveKey = ItemKey.None;

		/// <summary>当前这一件的进度,单位是 tick(0 .. TicksPerItem)。</summary>
		public float ProgressTicks;

		/// <summary>
		/// 并行数:网络里有几个分子装配室就能同时推进几份进度。
		/// 相当于 AE2 的「协处理器」——多摆一个装配室,合成就快一倍。
		/// </summary>
		public int Parallel = 1;

		/// <summary>每件产品需要的 tick 数(配置里可调)。</summary>
		public static int TicksPerItem
		{
			get
			{
				MEConfig config = MEConfig.Instance;
				int value = config?.CraftTicksPerItem ?? 20;
				return Math.Clamp(value, 1, 600);
			}
		}

		/// <summary>整体进度 0..1,给界面画进度条用。</summary>
		public float ProgressFraction => TicksPerItem <= 0
			? 0f
			: Math.Clamp(ProgressTicks / TicksPerItem, 0f, 1f);

		private readonly HashSet<int> _conditionErrors = new();
        private MECraftingCpuEntity _activeCpu;
        private int _reselectTimer;
		private int _tickCounter;
		private MECraftingCpuEntity.Job _activeJob;
		private CraftPlan _plan;
		private bool _planValid;

		/// <summary>
		/// 上次「选任务失败」时的世界状态签名。
		/// 库存 / 目标 / 队列都没变的话,再跑一遍深度递归的规划器只会得到同样的「做不了」,
		/// 所以直接跳过 —— 这就是「没变就复用上次结论」。
		/// </summary>
		private long _lastFailSignature = long.MinValue;
        private int _lastFailureTick;

		public MEMolecularAssemblerEntity()
		{
			for (int i = 0; i < MaxStationSlots; i++)
			{
				Stations[i] = new Item();
			}

			LastStatus = ModText.Safe("StatusIdleRightClick", "空闲:用物品右键设定目标");
		}

		public override bool IsTileValidForEntity(int x, int y)
            => MachineTiles.IsEntityAnchor(x,y,ModContent.TileType<MEMolecularAssemblerTile>());

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

		/// <summary>
		/// 当前应该显示多少个槽位 —— 模仿魔法存储:一排放满就自动多出一排。
		/// 同时保证「已经装着东西的槽位」不会被隐藏。
		/// </summary>
		public int VisibleStationSlots()
		{
			int filled = 0;
			int highest = -1;

			for (int i = 0; i < MaxStationSlots; i++)
			{
				if (Stations[i] != null && Stations[i].type > 0 && Stations[i].stack > 0)
				{
					filled++;
					highest = i;
				}
			}

			int rowsByCount = filled / SlotsPerRow + 1;
			int rowsByHighest = highest / SlotsPerRow + 1;
			int rows = Math.Max(rowsByCount, rowsByHighest);

			if (rows > MaxRows)
			{
				rows = MaxRows;
			}

			return rows * SlotsPerRow;
		}

		/// <summary>槽位里是否装了能提供该地块类型的工作站。</summary>
		public bool HasStation(int tileType)
		{
			foreach (Item station in Stations)
			{
				if (station == null || station.type <= 0)
				{
					continue;
				}

				if (station.createTile == tileType
                    || (station.createTile == TileID.MythrilAnvil && tileType == TileID.Anvils)
                    || (station.createTile == TileID.AdamantiteForge && (tileType == TileID.Furnaces || tileType == TileID.Hellforge))
                    || (station.createTile == TileID.Hellforge && tileType == TileID.Furnaces)
                    || (station.createTile == TileID.AlchemyTable && tileType == TileID.Bottles)
                    || (TileLoader.GetTile(station.createTile)?.AdjTiles is int[] adjacent && Array.IndexOf(adjacent, tileType) >= 0))
				{
					return true;
				}
			}

			return false;
		}

		public List<int> MissingStations(Recipe recipe)
		{
			var missing = new List<int>();
			foreach (int tile in RecipeStation.GetRequiredStations(recipe))
			{
				if (!HasStation(tile))
				{
					missing.Add(tile);
				}
			}

			return missing;
		}

		/// <summary>配方过滤器:缺工作站就判定这条配方不可用。徒手配方永远可用。</summary>
		public bool CanUseRecipe(Recipe recipe)
		{
			if (recipe == null || recipe.Disabled || MissingStations(recipe).Count != 0) return false;
            try
            {
                foreach (var condition in recipe.Conditions) if (!condition.IsMet()) return false;
                return true;
            }
            catch (Exception ex)
            {
                if (_conditionErrors.Add(recipe.RecipeIndex))
                    Mod.Logger.Warn("ME recipe condition could not be evaluated: " + recipe.RecipeIndex + " " + ex.Message);
                return false;
            }
		}

		public override void Update()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				return;
			}

            // A terminal owns scheduling for the whole component. Legacy stations and
            // targets remain in this entity; the terminal executor reads them directly.
            if (MENetwork.FindCraftingTerminal(Position.X, Position.Y) != null)
            {
                if (!ActiveKey.IsAir || _activeJob != null) ClearActive(null);
                LastStatus = "由合成终端调度";
                return;
            }

			_tickCounter++;
            // Reconsider denied active work at the selection cadence; the commit guard below is unconditional.
            if (_activeJob != null && _tickCounter % ReselectIntervalTicks == 0
                && !JobHasUsePermission(_activeJob, CurrentAnchor()))
            {
                SetPermissionDenied(_activeCpu);
                ClearActive(null); // Keep the queued order; another authorized CPU may still run.
            }
            if (_activeJob != null && !ActiveJobIsCurrent())
            {
                ClearActive(null);
                LastStatus = "任务已暂停、取消或断开连接";
                return;
            }
            if (_activeJob == null && !ActiveKey.IsAir && !ActiveKey.Equals(Target)) ClearActive(null);

			// 任务跑完了就清掉,让下一轮重新选(选了别的任务/配方变了都要重来)
			if (!ActiveKey.IsAir && _activeJob != null && _activeJob.Remaining <= 0)
			{
				ClearActive(ModText.Get("StatusDone", "完成"));
			}

			if (ActiveKey.IsAir || !_planValid)
			{
				if (++_reselectTimer < ReselectIntervalTicks)
				{
					return;
				}

				_reselectTimer = 0;

				if (!TrySelect())
				{
					return;
				}
			}

			// 进度推进:每个 tick 加「并行数」点进度。
			// 并行数 = 网络里的装配室数量,所以多摆一个装配室就快一倍。
			if (_tickCounter % 5 == 0)
			{
				RefreshParallel();
			}

			ProgressTicks += 1; // Each physical assembler advances once; counting all workers here squared throughput.

			int guard = 0;
			int perItem = TicksPerItem;

			while (ProgressTicks >= perItem && guard++ < 512)
			{
				ProgressTicks -= perItem;

				if (!ProduceOne())
				{
					// 原料断了 / 配方不可用了 —— 丢掉缓存,下一轮重新规划
					_planValid = false;
					ProgressTicks = 0f;
					ClearActive(null);
					return;
				}
			}

			// 多人:合成是服务器在跑,客户端得能看到进度和库存变化。
			// 只在真的在合成时才发,而且是低频(15/30 tick),不会变成刷屏。
			if (Main.netMode == NetmodeID.Server && !ActiveKey.IsAir)
			{
				if (_tickCounter % 15 == 0)
				{
					MENet.SendAssembler(Position);
				}

				if (_tickCounter % 30 == 0)
				{
					MENet.SendNetworkLedgers(Position.X, Position.Y);

					MECraftingCpuEntity cpuSync = _activeCpu;
					if (cpuSync != null)
					{
						MENet.SendCpu(cpuSync.Position);
					}
				}
			}
		}

		private void RefreshParallel()
		{
			int count = 0;

			foreach (Point16 point in MENetwork.Collect(Position.X, Position.Y))
			{
				if (TileEntity.ByPosition.TryGetValue(point, out TileEntity entity)
					&& entity is MEMolecularAssemblerEntity)
				{
					count++;
				}
			}

			Parallel = Math.Max(1, count);

			// 进阶成就:一个网络里挂 5 个装配室(并行合成)
			if (count >= 5)
			{
				AchievementTriggers.Trigger<FiveAssemblersAchievement>();
			}
		}

		private void ClearActive(string reason)
		{
			if (reason != null && _activeJob == null && !ActiveKey.IsAir)
			{
				LastStatus = reason + ":" + Lang.GetItemNameValue(ActiveKey.Type);
			}

			MECraftingCpuEntity previousCpu = _activeCpu;
			_activeJob = null;
            _activeCpu = null;
			_plan = null;
			_planValid = false;
			ProgressTicks = 0f;
			ActiveKey = ItemKey.None;
            if (previousCpu != null)
            {
                RefreshCpuActiveJob(previousCpu);
                MENet.SendCpu(previousCpu.Position);
            }
		}

        private bool ActiveJobIsCurrent()
        {
            return _activeCpu != null && !_activeCpu.Paused && _activeJob != null && !_activeJob.Paused
                && _activeJob.Remaining > 0 && _activeCpu.Jobs.Contains(_activeJob)
                && TileEntity.ByPosition.TryGetValue(_activeCpu.Position, out var entity)
                && ReferenceEquals(entity, _activeCpu)
                && MENetwork.Collect(Position.X, Position.Y).Contains(_activeCpu.Position);
        }

        private Point16 CurrentAnchor() => MENetwork.CanonicalAnchor(MENetwork.Collect(Position.X, Position.Y));

        // Anonymous keep-stock and legacy targets have no requester identity; preserve their existing behavior.
        private static bool JobHasUsePermission(MECraftingCpuEntity.Job job, Point16 anchor)
            => string.IsNullOrEmpty(job?.RequestedBy)
                || NetworkRegistry.GetLevel(anchor, job.RequestedBy) >= NetworkRegistry.LevelUse;

        private void SetPermissionDenied(MECraftingCpuEntity cpu)
        {
            LastStatus = ModText.Get("MsgNoPermission", "你没有这个 ME 网络的权限(找所有者要权限)");
            if (cpu != null) cpu.LastStatus = LastStatus;
        }

        private static List<MECraftingCpuEntity> ConnectedCpus(List<Point16> blocks)
        {
            var cpus = new List<MECraftingCpuEntity>();
            foreach (Point16 point in blocks)
                if (TileEntity.ByPosition.TryGetValue(point, out var entity) && entity is MECraftingCpuEntity cpu)
                    cpus.Add(cpu);
            cpus.Sort((a, b) => a.Position.Y != b.Position.Y
                ? a.Position.Y.CompareTo(b.Position.Y) : a.Position.X.CompareTo(b.Position.X));
            return cpus;
        }

        // ActiveJob is a display marker, not a reservation. One worker clearing must not erase another worker's marker.
        private static void RefreshCpuActiveJob(MECraftingCpuEntity cpu)
        {
            cpu.ActiveJob = null;
            if (cpu.Paused || !TileEntity.ByPosition.TryGetValue(cpu.Position, out var current)
                || !ReferenceEquals(current, cpu)) return;
            int earliest = int.MaxValue;
            foreach (Point16 point in MENetwork.Collect(cpu.Position.X, cpu.Position.Y))
            {
                if (!TileEntity.ByPosition.TryGetValue(point, out var entity)
                    || entity is not MEMolecularAssemblerEntity worker || !ReferenceEquals(worker._activeCpu, cpu)
                    || worker._activeJob == null || worker._activeJob.Remaining <= 0
                    || worker.ActiveKey.IsAir || !worker._planValid) continue;
                int index = cpu.Jobs.IndexOf(worker._activeJob);
                if (index >= 0 && index < earliest)
                {
                    earliest = index;
                    cpu.ActiveJob = worker._activeJob;
                }
            }
        }

		/// <summary>把网络内容汇总成一份「探针账本」,交给规划器算账。</summary>
		private Dictionary<ItemKey,long> BuildProbe(List<IMEStorage> storage) => MENetwork.Aggregate(storage);

		/// <summary>
		/// 选一个任务开跑。优先队列(能跳过硬料的任务,不再被队首堵住),
		/// 没有队列就退回「右键设定的目标」。
		/// 一次只规划 **1 件** —— 逐件计时需要的正是「这一件要消耗什么」。
		/// </summary>
		private bool TrySelect()
		{
            long selectionStarted = Stopwatch.GetTimestamp();
			if (StorageSystem.Planner == null)
			{
				return false;
			}

			List<IMEStorage> storage = MENetwork.CollectStorage(Position.X, Position.Y);
			if (storage.Count == 0)
			{
				LastStatus = ModText.Get("StatusNoNetwork", "没有连接的存储方块(驱动器 / 存储单元)");
				return false;
			}

			if (_activeJob != null && !ActiveJobIsCurrent()) ClearActive(null);

			var probe = BuildProbe(storage);
			List<Point16> blocks = MENetwork.Collect(Position.X, Position.Y);
            List<MECraftingCpuEntity> cpus = ConnectedCpus(blocks);
            Point16 anchor = MENetwork.CanonicalAnchor(blocks);

			RefreshParallel();

			// 先把「库存 + 目标 + 队列」压成一个签名,和上次失败时对比。
			long signature = ComputeSignature(probe, cpus, anchor, out long queueSignature);
            if (queueSignature != _scanQueueSignature)
            {
                _scanQueueSignature = queueSignature;
                // Inventory changes must not repeatedly restart an expensive first job.
                // Only queue structure/order changes invalidate the saved job index.
                _nextJobIndex = 0;
            }
			if (signature == _lastFailSignature && unchecked((uint)(_tickCounter - _lastFailureTick)) < 60)
			{
				return false;
			}

            bool hasQueuedJobs = false;
            int planned = 0;
            string statusBeforeScan = LastStatus;
            int startCpu = cpus.Count == 0 ? 0 : _nextCpuIndex % cpus.Count;
            int resumeJob = _nextJobIndex;
            for (int offset = 0; offset < cpus.Count; offset++)
            {
                int cpuIndex = (startCpu + offset) % cpus.Count;
                MECraftingCpuEntity cpu = cpus[cpuIndex];
                if (cpu.Jobs.Count == 0) continue;
                hasQueuedJobs = true;
                if (cpu.Paused)
                {
                    cpu.LastStatus = LastStatus = ModText.Get("StatusPaused", "队列已暂停");
                    continue;
                }
                CraftPlan firstFailure = null;
                int firstJob = offset == 0 ? Math.Min(resumeJob, cpu.Jobs.Count) : 0;
                for (int jobIndex = firstJob; jobIndex < cpu.Jobs.Count; jobIndex++)
                {
                    MECraftingCpuEntity.Job candidate = cpu.Jobs[jobIndex];
                    if (candidate == null || candidate.Key.IsAir || candidate.Remaining <= 0 || candidate.Paused) continue;
                    if (!JobHasUsePermission(candidate, anchor))
                    {
                        SetPermissionDenied(cpu);
                        continue;
                    }
                    if (planned > 0 && (planned >= MaxCandidatePlansPerSelection
                        || Stopwatch.GetElapsedTime(selectionStarted).TotalMilliseconds >= CandidatePlanningBudgetMilliseconds))
                    {
                        _nextCpuIndex = cpuIndex; _nextJobIndex = jobIndex;
                        _lastFailSignature = long.MinValue;
                        LastStatus = statusBeforeScan;
                        // Yield without diagnosing unexamined jobs as missing materials.
                        // One Plan always runs; a single mod condition cannot be preempted.
                        return false;
                    }
                    planned++;
                    CraftPlan plan = StorageSystem.Planner.Plan(probe, candidate.Key, 1, CanUseRecipe, useExistingTarget: false);
                    if (plan.FullySatisfied)
                    {
                        _activeJob = candidate; _activeCpu = cpu;
                        ActiveKey = candidate.Key; _plan = plan; _planValid = true; ProgressTicks = 0f;
                        _nextCpuIndex = (cpuIndex + 1) % cpus.Count; _nextJobIndex = 0;
                        _lastFailSignature = long.MinValue;
                        RefreshCpuActiveJob(cpu);
                        return true;
                    }
                    if (firstFailure == null || (plan.Limited && !firstFailure.Limited)) firstFailure = plan;
                }
                if (firstFailure?.Limited == true) cpu.LastStatus = LastStatus = "规划达到复杂度上限，请拆分任务";
                else if (firstFailure?.BlockedReasons.Count > 0) cpu.LastStatus = LastStatus = firstFailure.BlockedReasons[0];
                else if (firstFailure != null && firstFailure.Missing.Count > 0)
                {
                    MissingIngredient first = firstFailure.Missing[0];
                    cpu.LastStatus = LastStatus = ModText.Get("StatusQueueMissing", "队列全部缺料(以队首为例)") + ":"
                        + first.ItemName + " x" + first.Amount + DescribeAcquisition(first);
                }
            }
            _nextJobIndex = 0;
            if (hasQueuedJobs)
            {
                // A resumed partial scan has not revisited the earlier jobs of its first CPU yet.
                if (resumeJob == 0) RememberFailure(signature);
                else _lastFailSignature = long.MinValue;
                return false;
            }

			if (Target.IsAir)
			{
				RememberFailure(signature);
				return false;
			}

			CraftPlan targetPlan = StorageSystem.Planner.Plan(probe, Target, 1, CanUseRecipe, useExistingTarget: false);

			if (!targetPlan.FullySatisfied)
			{
                if (targetPlan.Limited) { LastStatus = "规划达到复杂度上限，请拆分任务"; RememberFailure(signature); return false; }
                if (targetPlan.BlockedReasons.Count>0) { LastStatus=targetPlan.BlockedReasons[0]; return false; }
                if (targetPlan.Missing.Count == 0) { LastStatus = "规划达到复杂度上限，请拆分任务"; return false; }
				MissingIngredient first = targetPlan.Missing[0];
				LastStatus = ModText.Get("StatusMissing", "缺料") + ":"
					+ first.ItemName + " x" + first.Amount + DescribeAcquisition(first);
				RememberFailure(signature);
				return false;
			}

			if (targetPlan.ProducedAmount <= 0)
			{
				LastStatus = ModText.Get("StatusInStock", "库存里已经有") + " "
					+ Lang.GetItemNameValue(Target.Type);
				Target = ItemKey.None;
				return false;
			}

			_activeJob = null;
            _activeCpu = null;
			ActiveKey = Target;
			_plan = targetPlan;
			_planValid = true;
			ProgressTicks = 0f;
			_lastFailSignature = long.MinValue;
			return true;
		}

        private void RememberFailure(long signature)
        {
            _lastFailSignature = signature;
            _lastFailureTick = _tickCounter;
        }

		/// <summary>
		/// 「库存 / 目标 / 队列」的轻量签名。
		/// 仅用于短暂合并失败重试；哈希碰撞最多延迟到定期重试，不决定是否提交产物。
		/// </summary>
		private long ComputeSignature(IReadOnlyDictionary<ItemKey,long> probe, List<MECraftingCpuEntity> cpus, Point16 anchor, out long queueSignature)
		{
			long hash = 17;
            queueSignature = 17;
			hash = unchecked(hash * 31 + Target.GetHashCode());
			hash = unchecked(hash * 31 + Math.Max(1, Parallel));

			// 逐项把库存压进签名(聚合本来就已经算过了,这里是同一量级的开销)
			foreach (KeyValuePair<ItemKey, long> entry in probe)
			{
				hash = unchecked(hash * 31 + entry.Key.GetHashCode());
				hash = unchecked(hash * 31 + entry.Value);
			}

			foreach (MECraftingCpuEntity cpu in cpus)
			{
                queueSignature = unchecked(queueSignature * 31 + cpu.Position.X);
                queueSignature = unchecked(queueSignature * 31 + cpu.Position.Y);
                queueSignature = unchecked(queueSignature * 31 + (cpu.Paused ? 1 : 0));
                queueSignature = unchecked(queueSignature * 31 + cpu.Jobs.Count);
                hash = unchecked(hash * 31 + cpu.Position.X);
                hash = unchecked(hash * 31 + cpu.Position.Y);
                hash = unchecked(hash * 31 + (cpu.Paused ? 1 : 0));
				foreach (MECraftingCpuEntity.Job job in cpu.Jobs)
				{
                    queueSignature = unchecked(queueSignature * 31 + (job?.Id?.GetHashCode(StringComparison.Ordinal) ?? 0));
                    queueSignature = unchecked(queueSignature * 31 + (job?.Key.GetHashCode() ?? 0));
                    queueSignature = unchecked(queueSignature * 31 + (job?.Paused==true ? 1 : 0));
					if (job == null)
					{
						continue;
					}

					hash = unchecked(hash * 31 + job.Key.GetHashCode());
                    hash = unchecked(hash * 31 + (job.Id?.GetHashCode(StringComparison.Ordinal) ?? 0));
					hash = unchecked(hash * 31 + NetworkRegistry.GetLevel(anchor, job.RequestedBy));
					hash = unchecked(hash * 31 + job.Remaining);
                    hash = unchecked(hash * 31 + (job.Paused ? 1 : 0));
				}
			}

			return hash;
		}

		private static string DescribeAcquisition(MissingIngredient ingredient)
		{
			if (string.IsNullOrEmpty(ingredient.Acquisition))
			{
				return string.Empty;
			}

			string tier = ingredient.TierIndex >= 0
				? " " + ModText.Format("MsgTierTag", "时间点#{0}", ingredient.TierIndex)
				: string.Empty;

			return "  [" + ingredient.Acquisition + tier + "]";
		}

		/// <summary>
		/// 产出**一件**。原料按缓存的计划扣,扣不动就返回 false 让上层重新规划。
		/// </summary>
		private bool ProduceOne()
        {
            if (ActiveKey.IsAir || StorageSystem.Planner == null) return false;
            if (_activeJob != null && !ActiveJobIsCurrent()) return false;
            MECraftingCpuEntity selectedCpu = _activeCpu;
            MECraftingCpuEntity.Job selectedJob = _activeJob;
            ItemKey selectedKey = ActiveKey;
            var storage = MENetwork.CollectStorage(Position.X, Position.Y);
            var plan = StorageSystem.Planner.Plan(BuildProbe(storage), ActiveKey, 1, CanUseRecipe, useExistingTarget: false);
            if (!plan.FullySatisfied)
            {
                LastStatus = plan.Limited ? "规划达到复杂度上限，请拆分任务"
                    : plan.BlockedReasons.Count>0 ? plan.BlockedReasons[0] : "原料或配方条件已变化，等待补齐";
                return false;
            }
            // Conditions are mod callbacks. They can cancel/pause queues, change the target,
            // or alter topology while planning. Never commit through the pre-callback references.
            if (!ReferenceEquals(selectedCpu, _activeCpu) || !ReferenceEquals(selectedJob, _activeJob)
                || !ActiveKey.Equals(selectedKey)
                || (selectedJob != null ? !ActiveJobIsCurrent() || !selectedJob.Key.Equals(selectedKey)
                    : !Target.Equals(selectedKey)))
            { LastStatus = "任务已暂停、取消或断开连接"; return false; }
            var currentStorage = MENetwork.CollectStorage(Position.X, Position.Y);
            if (!SameStorageSources(storage, currentStorage))
            { LastStatus = "原料或配方条件已变化，等待补齐"; return false; }
            storage = currentStorage;
            Point16 anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(Position.X,Position.Y));
            if (!JobHasUsePermission(_activeJob, anchor))
            { SetPermissionDenied(_activeCpu); return false; }
            foreach (var material in plan.Consumed)
                if (NetworkRegistry.IsLocked(anchor,material.Key.Type)
                    && NetworkRegistry.GetLevel(anchor,_activeJob?.RequestedBy)<NetworkRegistry.LevelManage)
                { LastStatus="材料已锁定，需要管理权限或解除锁定"; return false; }
            if (!StorageTransaction.TryApply(storage, plan.Consumed, plan.Outputs, out string reason))
            { LastStatus = reason; if (_activeCpu != null) _activeCpu.LastStatus = reason; return false; }
            long output = plan.Outputs.TryGetValue(ActiveKey, out long count) ? count : 0;
            string name = Lang.GetItemNameValue(ActiveKey.Type);
            if (_activeJob != null)
            {
                long completed = Math.Min(_activeJob.Remaining, output);
                if (_activeCpu.OutputToInventory && !string.IsNullOrEmpty(_activeJob.RequestedBy))
                {
                    foreach (Player player in Main.player)
                        if (player != null && player.active && player.name == _activeJob.RequestedBy)
                        { StorageOperations.Withdraw(storage, ActiveKey, completed, player); break; }
                }
                _activeJob.Remaining -= completed;
                if (_activeJob.Remaining <= 0) _activeCpu.DropJob(_activeJob);
                _activeCpu.LastStatus = _activeJob.Remaining <= 0 ? "已完成: " + name : "合成中: " + name + "，剩余 " + _activeJob.Remaining;
                LastStatus = _activeCpu.LastStatus;
                MENet.SendCpu(_activeCpu.Position);
            }
            else { Target = ItemKey.None; LastStatus = "已合成: " + name + "，产物已入库"; }
            MENet.SendNetworkLedgers(Position.X, Position.Y);
            ClearActive(null); // Replan every batch; never execute an obsolete consumed/output pair twice.
            MENet.SendAssembler(Position);
            AchievementTriggers.Trigger<AutomateFirstItemAchievement>();
            return true;
        }

        private static bool SameStorageSources(List<IMEStorage> before, List<IMEStorage> current)
        {
            if (before.Count != current.Count) return false;
            // Preserve storage priority as well as entity identity. A changed source set will
            // replan normally on the next selection; no private ledger has been debited yet.
            for (int i = 0; i < before.Count; i++)
                if (!ReferenceEquals(before[i], current[i])) return false;
            return true;
        }

		/// <summary>
		/// 这条配方一次产出几个。
		/// 同一物品可能有多条配方,规划器内部挑了一条;这里按「原料正好对得上缓存计划」认出它,
		/// 认不出来就按 1 算(宁可少给也不凭空多发)。
		/// </summary>
		public override void SaveData(TagCompound tag)
		{
			ItemPersistence.WriteKey(tag, Target, "targetItem");
            tag["targetType"] = Target.Type;
			tag["targetPrefix"] = Target.Prefix;
			tag["stations"] = new List<Item>(Stations);

            // Only durable target/stations are saved. Active plans have not consumed stock.

		}

		public override void LoadData(TagCompound tag)
		{
			// 存档里的物品 ID 可能因为玩家删掉模组而失效 —— 和其余 6 个方块实体一样过守卫
			Target = ItemPersistence.ReadKey(tag, "targetItem", "targetType", "targetPrefix");

			if (tag.ContainsKey("stations"))
			{
				System.Collections.Generic.IList<Item> saved = tag.GetList<Item>("stations");
				for (int i = 0; i < MaxStationSlots && i < saved.Count; i++)
				{
					Item station = saved[i];

					// 工作站槽位也是存档里的物品,ID 失效就清空该槽
					if (station == null || (station.type > 0 && !SavedItemGuard.IsValidType(station.type)))
					{
						station = new Item();
					}

					Stations[i] = station;
				}
			}

            ActiveKey=ItemKey.None;
            ProgressTicks=0;

			// 批次提交前没有扣料。重载后安全地重新规划并计时。
			_plan = null;
			_planValid = false;
			_activeJob = null;
            _activeCpu = null;
            _nextCpuIndex = 0; _nextJobIndex = 0;
            _scanQueueSignature = long.MinValue;
			_lastFailSignature = long.MinValue;
			_reselectTimer = 0;

			if (ProgressTicks < 0f)
			{
				ProgressTicks = 0f;
			}
		}
	}
}
