using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems;

internal static class BrowserRefinementRegression
{
    internal static void Snapshots(Action<bool, string> check)
    {
        int mode = Main.netMode; bool menu = Main.gameMenu; var state = UISystem.Interface.CurrentState;
        var visible = UISystem.ActiveNetworkContents;
        try
        {
            UISystem.Interface.SetState(null); Main.netMode = NetmodeID.MultiplayerClient; Main.gameMenu = false;
            var position = new Point16(30, 30); PinnedInventory.Track(position);
            var read = (TerminalReadState)typeof(PinnedInventory).GetField("Read", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            read.Begin(position); int epoch = read.Epoch;
            typeof(PinnedInventory).GetField("_requestedAt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, Main.GameUpdateCount);
            var contents = new Dictionary<ItemKey, long> { [ItemKey.Crafted(ItemID.Wood)] = 123 };
            UISystem.ApplyTerminalAccess(position, epoch, NetworkRegistry.LevelReadOnly, contents, new(-1, -1), new(-1, -1));
            check(ReferenceEquals(PinnedInventory.Snapshot(), contents) && ReferenceEquals(UISystem.ActiveNetworkContents, visible),
                "background inventory response updates only its authorized bookmark session");
            var other = new TerminalReadState(); other.Begin(position);
            check(other.Epoch != epoch && !UISystem.AcceptTerminalInventorySnapshot(position, other.Epoch),
                "foreground and background readers use distinct epochs and reject each other's late responses");
            UISystem.ApplyTerminalAccess(position, epoch, NetworkRegistry.LevelNone, null, new(-1, -1), new(-1, -1));
            check(PinnedInventory.Snapshot() == null && !PinnedInventory.Accepts(position, epoch)
                && ReferenceEquals(UISystem.ActiveNetworkContents, visible),
                "background permission denial clears its inventory without replacing or clearing another window");
        }
        finally { PinnedInventory.Reset(); Main.netMode = mode; Main.gameMenu = menu; UISystem.Interface.SetState(state); }
    }
    internal static void Trees(Action<bool, string> check)
    {
        void Recipes(params Recipe[] recipes)
        {
            Main.recipe = recipes; Recipe.numRecipes = recipes.Length;
            var index = new RecipeIndex(); index.Build();
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Recipes)).SetValue(null, index);
        }
        BookmarkTree Build(int type, RecipeTreeStock stock = null)
        {
            var tree = new BookmarkTree(new BrowserBookmark(BrowserContent.Name(type)), stock);
            for (int tick = 0; tick < 1000 && !tree.Complete; tick++) tree.Advance(16);
            check(tree.Complete, "refined tree finishes within its incremental work bound"); return tree;
        }
        Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.IronBar).AddIngredient(ItemID.CopperBar),
            Recipe.Create(ItemID.IronBar).AddIngredient(ItemID.StoneBlock, 4), Recipe.Create(ItemID.CopperBar).AddIngredient(ItemID.StoneBlock, 4));
        var input = new Dictionary<int, long> { [ItemID.StoneBlock] = 5 };
        var shared = new RecipeTreeStock(input);
        var tree = Build(ItemID.Torch, shared);
        check(tree.Nodes.Count(node => node.Item == ItemID.StoneBlock) == 1
            && tree.Nodes.Single(node => node.Item == ItemID.StoneBlock).Missing == 3,
            "shared stock hides a fulfilled branch without counting its items again in a sibling");
        check(input[ItemID.StoneBlock] == 5, "preview reservations leave the original inventory unchanged");
        var second = Build(ItemID.IronBar, shared);
        check(second.Nodes.Single(node => node.Item == ItemID.StoneBlock).Missing == 4,
            "ordered favorite roots share one preview stock pool");

        Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.IronBar).AddIngredient(ItemID.CopperBar),
            Recipe.Create(ItemID.IronBar).AddIngredient(ItemID.StoneBlock, 4), Recipe.Create(ItemID.CopperBar).AddIngredient(ItemID.IronBar));
        tree = Build(ItemID.Torch, new RecipeTreeStock(new Dictionary<int, long> { [ItemID.StoneBlock] = 5 }));
        check(!tree.MaterialsReady && tree.Nodes.Where(node => node.Item == ItemID.StoneBlock).Sum(node => node.Missing) == 3,
            "repeated intermediate recipes still reserve their materials and cannot falsely report ready");

        Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.StoneBlock, 5).AddIngredient(ItemID.StoneWall),
            Recipe.Create(ItemID.StoneBlock).AddIngredient(ItemID.StoneWall, 4), Recipe.Create(ItemID.StoneWall).AddIngredient(ItemID.StoneBlock));
        tree = Build(ItemID.Torch, new RecipeTreeStock(new Dictionary<int, long> { [ItemID.StoneBlock] = 2, [ItemID.StoneWall] = 3 }));
        check(tree.Nodes.Count == 2 && tree.Nodes[1].Item == ItemID.StoneBlock && tree.Nodes[1].Missing == 3,
            "cycle rollback removes its entire conversion and refunds preview stock for other branches");
        check(!tree.MaterialsReady && !tree.Nodes.Any(node => node.Cycle), "pruned cyclic material remains missing without a misleading ready status or cycle notice");

        Recipes(Recipe.Create(ItemID.StoneBlock).AddIngredient(ItemID.StoneWall, 4),
            Recipe.Create(ItemID.StoneWall).AddIngredient(ItemID.StoneBlock),
            Recipe.Create(ItemID.StoneBlock).AddIngredient(ItemID.DirtBlock, 2));
        tree = Build(ItemID.StoneBlock);
        check(tree.Nodes.Count == 2 && tree.Nodes[1].Item == ItemID.DirtBlock && tree.RootRecipe == 2,
            "tree retries an acyclic alternative before collapsing a cyclic material to a leaf");
        tree = Build(ItemID.StoneBlock, new RecipeTreeStock(new Dictionary<int, long> { [ItemID.DirtBlock] = 2 }));
        check(tree.Nodes.Count == 1 && tree.MaterialsReady, "a root remains visible with materials ready when every required ingredient is in stock");

        var foodGroup = RecipeGroup.recipeGroups.Values.First(group => group.ValidItems.Count > 1 && group.ValidItems.Contains(ItemID.Wood));
        int groupId = RecipeGroup.recipeGroups.First(pair => ReferenceEquals(pair.Value, foodGroup)).Key;
        var grouped = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood, 3);
        grouped.acceptedGroups.Add(groupId); Recipes(grouped);
        int alternative = foodGroup.ValidItems.First(type => type != ItemID.Wood);
        tree = Build(ItemID.Torch, new RecipeTreeStock(new Dictionary<int, long> { [alternative] = 3 }));
        check(tree.Nodes.Count == 1 && tree.MaterialsReady, "recipe-group alternatives can satisfy a pinned requirement without expanding an arbitrary representative");
    }

    [Autoload(false)]
    private sealed class LiteralBonus : ModItem
    {
        public override void UpdateArmorSet(Player player) { player.setBonus = "safe bonus text"; Forbidden(); }
    }
    [Autoload(false)]
    private sealed class WritesBeforeBonus : ModItem
    {
        public override void UpdateArmorSet(Player player) { Mutation = 99; Main.LocalPlayer.statLife = 0; player.setBonus = "unreachable"; }
    }
    [Autoload(false)]
    private sealed class CallsBeforeBonus : ModItem
    {
        public override void UpdateArmorSet(Player player) { Forbidden(); player.setBonus = "unreachable"; }
    }
    [Autoload(false)]
    private sealed class LoopBeforeBonus : ModItem
    {
        public override void UpdateArmorSet(Player player) { while (player.statLife == 0) { } player.setBonus = "unreachable"; }
    }
    [Autoload(false)]
    private sealed class FormattedBonus : ModItem
    {
        public override void UpdateArmorSet(Player player)
        {
            player.setBonus = Terraria.Localization.Language.GetText("ArmorSetBonus.Wood").Format(new object[] { 3, "safe" });
            Forbidden();
        }
    }
    [Autoload(false)]
    private sealed class ExternalArrayBonus : ModItem
    {
        public override void UpdateArmorSet(Player player) { ExternalArguments[0] = "changed"; player.setBonus = "unreachable"; }
    }
    private static readonly object[] ExternalArguments = { "original" };
    private static int Mutation;
    private static void Forbidden() { Mutation++; throw new InvalidOperationException("Armor preview executed a forbidden effect"); }
    internal static void Armor(Action<bool, string> check)
    {
        var player = Main.LocalPlayer; int life = player.statLife;
        string bonus = player.setBonus;
        int projectiles = Main.projectile.Count(projectile => projectile.active);
        var head = ContentSamples.ItemsByType[ItemID.WoodHelmet];
        var body = ContentSamples.ItemsByType[ItemID.WoodBreastplate];
        var legs = ContentSamples.ItemsByType[ItemID.WoodGreaves];
        Mutation = 0;
        check(ArmorSetText.Read(head, body, legs, new LiteralBonus()) == "safe bonus text",
            "guarded armor reader captures declared text before subsequent combat callbacks");
        check(ArmorSetText.Read(head, body, legs, new CallsBeforeBonus()) == null && Mutation == 0,
            "guarded armor reader rejects effect calls before a description");
        check(ArmorSetText.Read(head, body, legs, new WritesBeforeBonus()) == null && Mutation == 0 && player.statLife == life,
            "guarded armor reader cannot write static state or the real player's fields");
        check(ArmorSetText.Read(head, body, legs, new LoopBeforeBonus()) == null,
            "guarded armor reader rejects backward branches instead of executing an unbounded equipment loop");
        check(ArmorSetText.Read(head, body, legs, new FormattedBonus()) == Terraria.Localization.Language.GetText("ArmorSetBonus.Wood").Format(3, "safe"),
            "guarded armor reader formats localization using its own bounded argument buffer");
        check(ArmorSetText.Read(head, body, legs, new ExternalArrayBonus()) == null && (string)ExternalArguments[0] == "original",
            "guarded armor reader cannot mutate external arrays while formatting a description");
        string wood = ArmorSetText.Read(head, body, legs);
        check(!string.IsNullOrWhiteSpace(wood), "native wood armor description is readable without invoking equipment effects");
        string molten = ArmorSetText.Read(ContentSamples.ItemsByType[ItemID.MoltenHelmet], ContentSamples.ItemsByType[ItemID.MoltenBreastplate], ContentSamples.ItemsByType[ItemID.MoltenGreaves]);
        check(!string.IsNullOrWhiteSpace(molten) && molten != wood, "native molten armor has its own localized set description");
        ArmorSetIndex.Request(body.type, out _);
        for (int tick = 0; tick < 2000 && ArmorSetIndex.Request(body.type, out _).Count == 0; tick++) ArmorSetIndex.Advance();
        check(ArmorSetIndex.Request(body.type, out _).Contains(wood), "hovering a body component resolves its entire native set's bonus");
        if (ModContent.TryFind<ModItem>("CalamityMod/AerospecHeadgear", out var aerospec)
            && ModContent.TryFind<ModItem>("CalamityMod/AerospecBreastplate", out var aerospecBody)
            && ModContent.TryFind<ModItem>("CalamityMod/AerospecLeggings", out var aerospecLegs))
        {
            string text = ArmorSetText.Read(aerospec.Item, aerospecBody.Item, aerospecLegs.Item, aerospec);
            check(!string.IsNullOrWhiteSpace(text) && !text.Contains("{0}"),
                "loaded Calamity armor supplies a formatted set description without running its equipment update: " + ArmorSetText.UnavailableReason);
            ArmorSetIndex.Request(aerospec.Type, out _);
            for (int tick = 0; tick < 1000 && ArmorSetIndex.Request(aerospec.Type, out _).Count == 0; tick++) ArmorSetIndex.Advance();
            check(ArmorSetIndex.Request(aerospecBody.Type, out _).Contains(text), "loaded Calamity set effects are also available when hovering its body component");
        }
        BrowserHover.Item(ItemKey.Crafted(body.type));
        var lines = new List<TooltipLine>();
        ModContent.GetInstance<ArmorSetTooltip>().ModifyTooltips(body, lines);
        check(lines.Any(line => line.Name.StartsWith("ArmorSet", StringComparison.Ordinal) && line.Text.Contains(wood)),
            "actual AE item hover adds set effects to the native item tooltip");
        check(player.statLife == life && player.setBonus == bonus && Main.projectile.Count(projectile => projectile.active) == projectiles && Mutation == 0,
            "armor queries preserve the live player, world projectiles and test effect counters");
        ArmorSetIndex.Reset(); BrowserHover.Reset();
    }
}
