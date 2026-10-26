using System;
using System.Collections.Generic;
using System.Diagnostics;
using AE2ForTerraria.Content.Achievements;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Core;

/// <summary>Main-thread, budgeted scheduling; no item is reserved outside an atomic commit.</summary>
public sealed class TerminalCraftingExecutor
{
    // Work budgets bound frame cost, not order size, queue size or an artificial craft delay.
    internal const int CandidateBudget=64;
    internal const double FrameBudgetMilliseconds=4;
    private int _cursor;
    private int _legacyCursor;
    public void Reset() { _cursor=0; _legacyCursor=0; }

    /// <summary>Manual orders execute synchronously; blocked work remains a normal durable job.</summary>
    public static bool Submit(MECraftingCpuEntity host, ItemKey key, long count, string requester, out string status)
    {
        status = "物品、数量或合成设备无效";
        if (Main.netMode == NetmodeID.MultiplayerClient || host == null
            || !TileEntity.ByPosition.TryGetValue(host.Position, out var live) || !ReferenceEquals(live, host)) return false;
        var terminal = MENetwork.FindCraftingTerminal(host.Position.X, host.Position.Y);
        // Do not merge a new click into an older paused or oversized job.
        if (!host.TryEnqueue(key, count, requester, terminal == null, out var job)) return false;
        bool crafted = false;
        if (terminal == null) status = "已加入旧设备队列";
        else if (host.Paused) status = "队列已暂停；任务已保留";
        else
        {
            var storage = MENetwork.CollectStorage(host.Position.X, host.Position.Y);
            crafted = TryExecute(terminal, host, job, storage, out status, notifyCompletion: false);
            if (!crafted && host.Jobs.Contains(job)) status = "暂未合成：" + status + "；任务已保留";
        }
        job.LastStatus = status;
        host.LastStatus = status;
        if (terminal != null) terminal.LastStatus = status;
        MENet.SendCpu(host.Position);
        if (crafted)
        {
            MENet.SendNetworkLedgers(host.Position.X, host.Position.Y);
            AchievementTriggers.Trigger<AutomateFirstItemAchievement>();
        }
        return true;
    }

    public static bool HasStation(IReadOnlyList<Item> stations,int tile)
    {
        foreach (var item in stations)
        {
            if (item==null || item.IsAir || item.createTile<0) continue;
            int found=item.createTile;
            if (found==tile || (found==TileID.MythrilAnvil && tile==TileID.Anvils)
                || (found==TileID.AdamantiteForge && (tile==TileID.Furnaces || tile==TileID.Hellforge))
                || (found==TileID.Hellforge && tile==TileID.Furnaces)
                || (found==TileID.AlchemyTable && tile==TileID.Bottles)
                || (TileLoader.GetTile(found)?.AdjTiles is int[] adjacent && Array.IndexOf(adjacent,tile)>=0)) return true;
        }
        return false;
    }

    public void Update(MECraftingTerminalEntity terminal)
    {
        if (StorageSystem.Planner==null) return;
        var blocks=MENetwork.Collect(terminal.Position.X,terminal.Position.Y);
        var hosts=new List<MECraftingCpuEntity>();
        var legacy=new List<MEMolecularAssemblerEntity>();
        foreach (var point in blocks)
        {
            if (!TileEntity.ByPosition.TryGetValue(point,out var entity)) continue;
            if (entity is MECraftingCpuEntity cpu) { hosts.Add(cpu); cpu.ActiveJob=null; }
            if (entity is MEMolecularAssemblerEntity assembler) legacy.Add(assembler);
        }
        hosts.Sort((a,b)=>a.Position.Y!=b.Position.Y ? a.Position.Y.CompareTo(b.Position.Y) : a.Position.X.CompareTo(b.Position.X));
        var storage=MENetwork.CollectStorage(terminal.Position.X,terminal.Position.Y);
        if (storage.Count==0) { terminal.LastStatus="等待连接存储"; return; }
        int count=0;
        foreach (var cpu in hosts) count=checked(count+cpu.Jobs.Count);
        long started=Stopwatch.GetTimestamp();
        bool changed=false;
        var dirty=new HashSet<MECraftingCpuEntity>();
        // Each waiting job gets a turn, including jobs after a blocked or paused head.
        int attempts=Math.Min(count,CandidateBudget);
        for (int attempt=0;attempt<attempts;attempt++)
        {
            if (attempt>0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds>=FrameBudgetMilliseconds) break;
            int currentCount=0;
            foreach (var cpu in hosts) currentCount+=cpu.Jobs.Count;
            if (currentCount==0) break;
            int index=(_cursor++ & int.MaxValue)%currentCount;
            MECraftingCpuEntity host=null;
            MECraftingCpuEntity.Job job=null;
            foreach (var cpu in hosts)
            {
                if (index<cpu.Jobs.Count) { host=cpu; job=cpu.Jobs[index]; break; }
                index-=cpu.Jobs.Count;
            }
            if (job==null) continue;
            string old=job.LastStatus;
            if (host.Paused || job.Paused) job.LastStatus="已暂停";
            else if (job.Remaining<=0) { host.DropJob(job); dirty.Add(host); }
            else if (TryExecute(terminal,host,job,storage,out string status))
            {
                job.LastStatus=status;
                host.LastStatus=terminal.LastStatus=status;
                dirty.Add(host); changed=true;
                storage=MENetwork.CollectStorage(terminal.Position.X,terminal.Position.Y);
            }
            else { job.LastStatus=status; host.LastStatus=status; }
            if (old!=job.LastStatus) dirty.Add(host);
        }
        // Retain old assembler's saved single target without migrating, duplicating or
        // deleting its station inventory. A canceled/changed target is checked at commit.
        if (count==0 && !terminal.Paused)
        {
            legacy.Sort((a,b)=>a.Position.Y!=b.Position.Y ? a.Position.Y.CompareTo(b.Position.Y) : a.Position.X.CompareTo(b.Position.X));
            for (int attempt=0;attempt<Math.Min(legacy.Count,CandidateBudget);attempt++)
            {
                if (attempt>0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds>=FrameBudgetMilliseconds) break;
                var assembler=legacy[(_legacyCursor++ & int.MaxValue)%legacy.Count];
                if (assembler.Target.IsAir) continue;
                var target=assembler.Target;
                var temporary=new MECraftingCpuEntity.Job { Key=target,Remaining=1,Total=1 };
                bool completed=TryExecute(terminal,null,temporary,storage,out string status,assembler);
                if (completed)
                { assembler.Target=ItemKey.None; changed=true; }
                assembler.LastStatus=terminal.LastStatus=status;
                MENet.SendAssembler(assembler.Position);
                if (completed) break;
            }
        }
        foreach (var cpu in dirty) MENet.SendCpu(cpu.Position);
        if (changed)
        {
            MENet.SendNetworkLedgers(terminal.Position.X,terminal.Position.Y);
            AchievementTriggers.Trigger<AutomateFirstItemAchievement>();
        }
    }

    private static bool IsCurrent(MECraftingTerminalEntity terminal,MECraftingCpuEntity host,MECraftingCpuEntity.Job job,MEMolecularAssemblerEntity legacy)
    {
        if (!TileEntity.ByPosition.TryGetValue(terminal.Position,out var te) || !ReferenceEquals(te,terminal)) return false;
        var blocks=MENetwork.Collect(terminal.Position.X,terminal.Position.Y);
        if (legacy!=null) return !terminal.Paused && legacy.Target.Equals(job.Key)
            && blocks.Contains(legacy.Position) && TileEntity.ByPosition.TryGetValue(legacy.Position,out var old) && ReferenceEquals(old,legacy);
        return host!=null && !host.Paused && !job.Paused && job.Remaining>0 && host.Jobs.Contains(job)
            && blocks.Contains(host.Position) && TileEntity.ByPosition.TryGetValue(host.Position,out var current) && ReferenceEquals(current,host);
    }

    private static bool HasPermission(Point16 anchor,string player,Point16 host,out string effectivePlayer)
    {
        effectivePlayer=string.IsNullOrEmpty(player) ? NetworkRegistry.OriginalOwner(host) : player;
        if (!string.IsNullOrEmpty(effectivePlayer)) return NetworkRegistry.GetLevel(anchor,effectivePlayer)>=NetworkRegistry.LevelUse;
        // Legacy anonymous jobs are permitted only when the complete component is
        // actually unowned. A merged ownership summary is never an authorization.
        return NetworkRegistry.GetOwner(anchor)==null;
    }

    private static bool SameSources(List<IMEStorage> before,List<IMEStorage> after)
    {
        if (before.Count!=after.Count) return false;
        for (int i=0;i<before.Count;i++) if (!ReferenceEquals(before[i],after[i])) return false;
        return true;
    }

    private static bool TryExecute(MECraftingTerminalEntity terminal,MECraftingCpuEntity host,MECraftingCpuEntity.Job job,
        List<IMEStorage> storage,out string status,MEMolecularAssemblerEntity legacy=null,bool notifyCompletion=true)
    {
        status="等待处理";
        if (!IsCurrent(terminal,host,job,legacy)) { status="任务已暂停、取消或断开"; return false; }
        ItemKey requestedKey=job.Key;long requestedAmount=job.Remaining;
        var anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(terminal.Position.X,terminal.Position.Y));
        var hostPosition=host?.Position ?? legacy?.Position ?? terminal.Position;
        if (!HasPermission(anchor,job.RequestedBy,hostPosition,out string effectivePlayer)) { status="等待使用权限"; return false; }
        var stations=terminal.ConnectedStations();
        string blocked=null;
        bool Filter(Recipe recipe)
        {
            bool allowed=terminal.CanUseRecipe(recipe,stations,out string reason,effectivePlayer);
            if (!allowed && blocked==null) blocked=reason;
            return allowed;
        }
        CraftPlan Plan(long amount)
        {
            var inventory=MENetwork.Aggregate(storage);
            // Re-read stock and locks for a smaller retry too: recipe conditions can
            // change either, and the aggregate itself is shared with other readers.
            if (NetworkRegistry.GetLevel(anchor,effectivePlayer)<NetworkRegistry.LevelManage)
            {
                inventory=new Dictionary<ItemKey,long>(inventory);
                foreach (var key in new List<ItemKey>(inventory.Keys))
                    if (NetworkRegistry.IsLocked(anchor,key.Type)) inventory.Remove(key);
            }
            return StorageSystem.Planner.Plan(inventory,requestedKey,amount,Filter,useExistingTarget:false);
        }
        var plan=Plan(requestedAmount);
        bool canRetrySmaller=requestedAmount>1;
        // A large order may partially proceed when only one recipe batch is affordable.
        if (!plan.FullySatisfied && canRetrySmaller)
        {
            canRetrySmaller=false;
            plan=Plan(1);
        }
        // At most one smaller retry. Every candidate passes the entire commit guard;
        // a failed transaction has only touched private ledger copies.
        for(int attempt=0;attempt<2;attempt++)
        {
            if (!plan.FullySatisfied)
            {
                status=plan.Limited ? "规划工作量较大，等待重试或拆分任务"
                    : plan.BlockedReasons.Count>0 ? plan.BlockedReasons[0]
                    : blocked ?? (plan.Missing.Count>0 ? "缺少："+plan.Missing[0].ItemName+" ×"+plan.Missing[0].Amount : "等待可用配方");
                return false;
            }
            // Recipe conditions are callbacks and may mutate topology/queues. Revalidate
            // every live reference and station/condition just before the storage transaction.
            if (!IsCurrent(terminal,host,job,legacy) || !job.Key.Equals(requestedKey) || job.Remaining!=requestedAmount)
            { status="任务已暂停、取消或修改"; return false; }
            var currentStorage=MENetwork.CollectStorage(terminal.Position.X,terminal.Position.Y);
            if (!SameSources(storage,currentStorage)) { status="网络已变化，等待重新规划"; return false; }
            var currentStations=terminal.ConnectedStations();
            foreach (int recipeIndex in plan.Recipes)
                if (!terminal.CanUseRecipe(Main.recipe[recipeIndex],currentStations,out status,effectivePlayer)) return false;
            if (!IsCurrent(terminal,host,job,legacy) || !job.Key.Equals(requestedKey) || job.Remaining!=requestedAmount
                || !SameSources(currentStorage,MENetwork.CollectStorage(terminal.Position.X,terminal.Position.Y)))
            { status="网络或任务已变化，等待重新规划"; return false; }
            // The last condition callback could remove a station after it was checked.
            var finalStations=terminal.ConnectedStations();
            foreach(int recipeIndex in plan.Recipes)
                foreach(int tile in RecipeStation.GetRequiredStations(Main.recipe[recipeIndex]))
                    if(!HasStation(finalStations,tile)) {status="工作站已变化，等待重新规划";return false;}
            anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(terminal.Position.X,terminal.Position.Y));
            if (!HasPermission(anchor,job.RequestedBy,hostPosition,out effectivePlayer)) { status="等待使用权限"; return false; }
            foreach (var entry in plan.Consumed)
                if (NetworkRegistry.IsLocked(anchor,entry.Key.Type) && NetworkRegistry.GetLevel(anchor,effectivePlayer)<NetworkRegistry.LevelManage)
                { status="材料已锁定，需要管理权限"; return false; }
            if (!StorageTransaction.TryApply(currentStorage,plan.Consumed,plan.Outputs,out status,out var failure))
            {
                if (!canRetrySmaller || failure!=StorageTransactionFailure.OutputRejected) return false;
                canRetrySmaller=false;
                blocked=null;
                stations=terminal.ConnectedStations();
                plan=Plan(1);
                continue;
            }
            long completed=Math.Min(job.Remaining,plan.Outputs.GetValueOrDefault(job.Key));
            job.Remaining-=completed;
            if (host!=null)
            {
                if (host.OutputToInventory && !string.IsNullOrEmpty(job.RequestedBy))
                    foreach (var player in Main.player)
                        if (player!=null && player.active && player.name==job.RequestedBy)
                        { StorageOperations.Withdraw(currentStorage,job.Key,completed,player); break; }
                if (job.Remaining==0) host.DropJob(job);
                else host.ActiveJob=job;
            }
            status=job.Remaining==0 ? "合成完成："+Lang.GetItemNameValue(job.Key.Type)+"（需求 "+job.Total+" 件）"
                : "已合成 "+completed+" 件，剩余 "+job.Remaining;
            if (job.Remaining==0 && notifyCompletion) MENet.NotifyCraftCompleted(job.RequestedBy,status);
            return true;
        }
        return false;
    }
}
