using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace AE2ForTerraria.UI;

/// <summary>Recipe Browser categories, evaluated against loaded content, shared by both catalogues.</summary>
internal static class BrowserCategories
{
    internal sealed record Category(string Name, Func<Item, bool> Matches, Category[] Children = null);
    private static Category C(string name, Func<Item, bool> match, params Category[] children) => new(name, match, children);
    private static Category Union(string name, params Category[] children) => C(name, i => children.Any(c => c.Matches(i)), children);
    private static Category[] _items;
    private static HashSet<int> _stations;
    private static readonly Dictionary<(int, int, int), bool> Results = new();
    private static readonly Dictionary<Category, int> Icons = new();
    public static void Reset() { _items = null; _stations = null; Results.Clear(); Icons.Clear(); }
    internal static int Icon(Category category)
    {
        if (Icons.TryGetValue(category, out int icon)) return icon;
        icon = category.Name switch
        {
            "全部" => ItemID.Book, "武器" => ItemID.IronBroadsword, "工具" => ItemID.IronPickaxe,
            "盔甲" => ItemID.IronHelmet, "物块" => ItemID.Wood, "墙壁" => ItemID.StoneWall,
            "药水" => ItemID.LesserHealingPotion, "饰品" => ItemID.CloudinaBottle,
            "专家" => ItemID.DemonHeart, "钓鱼" => ItemID.WoodFishingPole,
            "工作站" => ItemID.WorkBench, "其他" => ItemID.Chest,
            _ => ContentSamples.ItemsByType.Values.Where(i => i.type > 0 && i.type < ItemID.Count)
                .OrderBy(i => i.type).FirstOrDefault(category.Matches)?.type
                ?? ContentSamples.ItemsByType.Values.FirstOrDefault(category.Matches)?.type ?? ItemID.Book
        };
        Icons[category] = icon; return icon;
    }
    public static Category[] Items
    {
        get
        {
            if (_items != null) return _items;
            _stations = new HashSet<int>(Main.recipe.Take(Recipe.numRecipes).Where(r => r != null && !r.Disabled).SelectMany(r => r.requiredTile).Where(t => t >= 0));
            var categories = new List<Category>
            {
                C("全部", i => true),
                Union("武器", C("近战", i => i.CountsAsClass(DamageClass.Melee) && i.pick == 0 && i.axe == 0 && i.hammer == 0),
                    C("悠悠球", i => ItemID.Sets.Yoyo[i.type]), C("魔法", i => i.CountsAsClass(DamageClass.Magic)),
                    C("远程", i => i.CountsAsClass(DamageClass.Ranged) && i.ammo == 0), C("投掷", i => i.CountsAsClass(DamageClass.Throwing)),
                    C("召唤", i => i.CountsAsClass(DamageClass.Summon) && !i.sentry && !ProjectileID.Sets.IsAWhip[i.shoot]),
                    C("鞭", i => ProjectileID.Sets.IsAWhip[i.shoot]), C("哨兵", i => i.sentry)),
                Union("工具", C("镐", i => i.pick > 0), C("斧", i => i.axe > 0), C("锤", i => i.hammer > 0)),
                Union("盔甲", C("头部", i => i.headSlot >= 0), C("上衣", i => i.bodySlot >= 0), C("裤装", i => i.legSlot >= 0)),
                C("物块", i => i.createTile >= 0,
                    C("工作站", i => _stations.Contains(i.createTile)), C("家具", i => i.createTile > 0 && Main.tileFrameImportant[i.createTile]),
                    C("方块", i => i.createTile >= 0 && !Main.tileFrameImportant[i.createTile]), C("容器", i => i.createTile >= 0 && Main.tileContainer[i.createTile]),
                    C("电路", i => ItemID.Sets.SortingPriorityWiring[i.type] > -1),
                    C("雕像", i => GenVars.statueList != null && GenVars.statueList.Any(p => p.X == i.createTile && p.Y == i.placeStyle)),
                    C("门", i => TileID.Sets.RoomNeeds.CountsAsDoor.Contains(i.createTile)), C("椅子", i => TileID.Sets.RoomNeeds.CountsAsChair.Contains(i.createTile)),
                    C("桌子", i => TileID.Sets.RoomNeeds.CountsAsTable.Contains(i.createTile)), C("光源", i => TileID.Sets.RoomNeeds.CountsAsTorch.Contains(i.createTile)),
                    C("火把", i => i.createTile >= 0 && TileID.Sets.Torch[i.createTile])),
                C("墙壁", i => i.createWall >= 0), C("饰品", i => i.accessory, C("翅膀", i => i.wingSlot > 0)),
                C("弹药", i => i.ammo != 0),
                C("药水", i => i.UseSound?.IsTheSameAs(SoundID.Item3) == true || Food(i),
                    C("生命药水", i => i.healLife > 0), C("魔力药水", i => i.healMana > 0),
                    C("增益药水", i => i.UseSound?.IsTheSameAs(SoundID.Item3) == true && i.buffType > 0 && !Food(i)), C("食物", Food)),
                C("专家", i => i.expert), C("大师", i => i.master),
                Union("宠物", C("宠物", i => Main.vanityPet[i.buffType]), C("照明宠物", i => Main.lightPet[i.buffType])),
                C("坐骑", i => i.mountType >= 0, C("矿车", i => i.mountType >= 0 && MountID.Sets.Cart[i.mountType])),
                C("钩爪", i => Main.projHook[i.shoot]), Union("染料", C("染料", i => i.dye != 0), C("染发剂", i => i.hairDye >= 0)),
                C("召唤物", i => ItemID.Sets.SortingPriorityBossSpawns[i.type] >= 0 && !SummonExceptions.Contains(i.type)),
                C("消耗品", i => i.createWall <= 0 && i.createTile < 0 && !(i.ammo > 0 && !i.notAmmo) && i.consumable,
                    C("捕获的生物", i => i.makeNPC != 0)),
                C("宝匣", i => Main.ItemDropsDB.GetRulesForItemID(i.type).Count > 0,
                    C("困难前宝匣", i => ItemID.Sets.IsFishingCrate[i.type] && !ItemID.Sets.IsFishingCrateHardmode[i.type]),
                    C("困难模式宝匣", i => ItemID.Sets.IsFishingCrateHardmode[i.type]),
                    C("困难前宝藏袋", i => ItemID.Sets.BossBag[i.type] && ItemID.Sets.PreHardmodeLikeBossBag[i.type] && i.type != ItemID.QueenSlimeBossBag),
                    C("困难模式宝藏袋", i => ItemID.Sets.BossBag[i.type] && !ItemID.Sets.PreHardmodeLikeBossBag[i.type] || i.type == ItemID.QueenSlimeBossBag),
                    C("其他", i => Main.ItemDropsDB.GetRulesForItemID(i.type).Count > 0 && !ItemID.Sets.BossBag[i.type] && !ItemID.Sets.IsFishingCrate[i.type])),
                Union("钓鱼", C("钓竿", i => i.fishingPole > 0), C("鱼饵", i => i.bait > 0),
                    C("浮标", i => i.type >= ItemID.FishingBobber && i.type <= ItemID.FishingBobberGlowingRainbow), C("任务鱼", i => i.questItem)),
                C("提炼", i => ItemID.Sets.ExtractinatorMode[i.type] >= 0)
            };
            var primary = categories.Skip(1).ToArray();
            categories.Add(C("其他", i => !primary.Any(c => c.Matches(i))));
            return _items = categories.ToArray();
        }
    }
    private static bool Food(Item i) => i.buffType == BuffID.WellFed || i.buffType == BuffID.WellFed2 || i.buffType == BuffID.WellFed3;
    private static readonly HashSet<int> SummonExceptions = new() { ItemID.LifeCrystal, ItemID.ManaCrystal, ItemID.LifeFruit, ItemID.TreasureMap,
        ItemID.ShellphoneDummy, ItemID.Shellphone, ItemID.ShellphoneSpawn, ItemID.ShellphoneOcean, ItemID.ShellphoneHell, ItemID.MagicConch,
        ItemID.DemonConch, ItemID.CellPhone, ItemID.IceMirror, ItemID.MagicMirror };
    public static bool Matches(int type, int category, int child)
    {
        if (!ContentSamples.ItemsByType.TryGetValue(type, out var item)) return false;
        var key = (type, category, child);
        if (!Results.TryGetValue(key, out bool result))
        {
            var c = Items[category];
            result = child > 0 ? c.Children[child - 1].Matches(item) : c.Matches(item);
            Results[key] = result;
        }
        return result;
    }
    public static readonly string[] NpcKinds = { "全部", "敌对生物", "Boss", "城镇 NPC", "小动物", "其他友好生物" };
    public static int NpcKind(int type)
    {
        if (!ContentSamples.NpcsByNetId.TryGetValue(type, out var npc)) return 0;
        if (npc.townNPC || NPCID.Sets.ActsLikeTownNPC[type]) return 3;
        if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[type]) return 2;
        if (NPCID.Sets.CountsAsCritter[type]) return 4;
        return npc.friendly ? 5 : 1;
    }
}
