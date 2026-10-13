using System;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria.DataStructures;

int failures=0;
foreach(string scenario in new[]{"cancel","pause","disconnect CPU","disconnect storage","replace storage","change anonymous target","unchanged order","anonymous unchanged"})
{
    var storage=new Storage(); storage.Ledger.Restore(ItemKey.Crafted(1),20);
    var cpu=new MECraftingCpuEntity(); var job=new MECraftingCpuEntity.Job(); cpu.Jobs.Add(job);
    bool anonymous=scenario.StartsWith("anonymous") || scenario=="change anonymous target";
    var worker=new SourceAssembler(anonymous ? null : cpu,anonymous ? null : job);
    MENetwork.Blocks=new(){new(0,0),cpu.Position,new(2,0)};
    MENetwork.Storage=new(){storage}; TileEntity.ByPosition=new(){{cpu.Position,cpu}};
    StorageSystem.Planner.DuringCondition=()=>
    {
        switch(scenario)
        {
            case "cancel":cpu.DropJob(job);break;
            case "pause":cpu.Paused=true;break;
            case "disconnect CPU":MENetwork.Blocks.Remove(cpu.Position);break;
            case "disconnect storage":MENetwork.Storage.Clear();break;
            case "replace storage":MENetwork.Storage=new(){new Storage()};break;
            case "change anonymous target":worker.Target=ItemKey.Crafted(3);break;
        }
    };
    bool result=worker.Commit();
    bool shouldCommit=scenario=="unchanged order" || scenario=="anonymous unchanged";
    bool correct=result==shouldCommit && storage.Ledger.GetAmount(ItemKey.Crafted(1))==(shouldCommit ? 19 : 20)
        && storage.Ledger.GetAmount(ItemKey.Crafted(2))==(shouldCommit ? 1 : 0);
    Console.WriteLine((correct ? "PASS " : "FAIL ")+scenario+": committed="+result+", wood="+storage.Ledger.GetAmount(ItemKey.Crafted(1))
        +", output="+storage.Ledger.GetAmount(ItemKey.Crafted(2))+", remaining="+job.Remaining);
    if(!correct) failures++;
}
Console.WriteLine("Controlled source-method harness: 8 assertions, "+failures+" failures. Terraria/recipe planning are test doubles; not an in-game Update test.");
return failures==0 ? 0 : 1;
