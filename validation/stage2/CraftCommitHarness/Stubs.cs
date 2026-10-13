using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using Terraria.DataStructures;
namespace Terraria
{
    public sealed class Item { public int type,stack,prefix; public void TurnToAir(){type=stack=0;} }
    public sealed class Recipe { public Func<bool> Condition; }
    public sealed class Player { public bool active; public string name; }
    public static class Main { public static Player[] player=Array.Empty<Player>(); }
    public static class Lang { public static string GetItemNameValue(int type) => "Item " + type; }
}
namespace Terraria.DataStructures
{
    public readonly record struct Point16(int X,int Y);
    public class TileEntity { public static Dictionary<Point16,TileEntity> ByPosition=new(); }
}
namespace AE2ForTerraria.Content.Tiles
{
    public sealed class MECraftingCpuEntity:TileEntity
    {
        public sealed class Job { public ItemKey Key=ItemKey.Crafted(2); public long Remaining=1; public string RequestedBy="owner"; }
        public Point16 Position=new(1,0); public bool Paused,OutputToInventory; public string LastStatus;
        public List<Job> Jobs=new();
        public void DropJob(Job job) => Jobs.Remove(job);
    }
}
namespace AE2ForTerraria.Content.Achievements
{
    public sealed class AutomateFirstItemAchievement {}
    public static class AchievementTriggers { public static void Trigger<T>() {} }
}
namespace AE2ForTerraria.Core
{
    public static class ItemPersistence
    {
        public static ItemKey KeyOf(Terraria.Item item) => new(item.type,item.prefix);
        public static Terraria.Item Create(ItemKey key,int stack=1) => new(){type=key.Type,prefix=key.Prefix,stack=stack};
    }
    public sealed class Storage:IMEStorage { public StorageLedger Ledger {get;}=new(); public bool Allows(ItemKey key)=>true; }
    public sealed class CraftPlan
    {
        public bool FullySatisfied=true,Limited;
        public List<string> BlockedReasons=new();
        public Dictionary<ItemKey,long> Consumed=new(){{ItemKey.Crafted(1),1}},Outputs=new(){{ItemKey.Crafted(2),1}};
    }
    public sealed class ControlledPlanner
    {
        public Action DuringCondition;
        public CraftPlan Plan(StorageLedger stock,ItemKey key,int amount,Func<Terraria.Recipe,bool> filter,bool useExistingTarget)
        {
            bool allowed=filter(new Terraria.Recipe { Condition=()=>{DuringCondition();return true;} });
            return new CraftPlan { FullySatisfied=allowed };
        }
    }
}
namespace AE2ForTerraria.Systems
{
    public static class SavedItemGuard { public static bool IsValidType(int type)=>type>0; }
    public static class ModText { public static string Get(string key,string fallback)=>fallback; }
    public static class StorageSystem { public static ControlledPlanner Planner=new(); }
    public static class MENetwork
    {
        public static List<IMEStorage> Storage=new();
        public static List<Point16> Blocks=new(){new(0,0),new(1,0),new(2,0)};
        public static List<Point16> Collect(int x,int y)=>new(Blocks);
        public static Point16 CanonicalAnchor(List<Point16> blocks)=>new(0,0);
        public static List<IMEStorage> CollectStorage(int x,int y)=>new(Storage);
    }
    public static class NetworkRegistry
    {
        public const int LevelUse=2,LevelManage=3;
        public static int GetLevel(Point16 p,string name)=>LevelManage;
        public static bool IsLocked(Point16 p,int type)=>false;
    }
    public static class StorageOperations { public static void Withdraw(List<IMEStorage> storage,ItemKey key,long count,Terraria.Player player) {} }
    public static class MENet { public static void SendCpu(Point16 p){} public static void SendAssembler(Point16 p){} public static void SendNetworkLedgers(int x,int y){} }
}
