using System;
using System.IO;
using System.Reflection;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

// Opt-in operation tests: temporary entities/recipes only, no player or world file access.
internal static class AutocraftingRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check)
    {
        Recipe[] recipes = Main.recipe;
        int count = Recipe.numRecipes;
        var activeAssembler = UISystem.ActiveAssembler;
        var activeCpu = UISystem.ActiveCraftingCpu;
        var activeTerminal = UISystem.ActiveCraftingTerminal;
        var activePosition = UISystem.ActiveTerminalPos;
        var activeNetwork = UISystem.ActiveNetwork;
        var activeContents = UISystem.ActiveNetworkContents;
        Player[] players = Main.player;
        int myPlayer = Main.myPlayer;
        try
        {
            Main.player = new Player[players.Length];
            for (int i = 0; i < Main.player.Length; i++) Main.player[i] = new Player();
            Main.myPlayer = 0;
            Main.player[0].active = true;
            Main.player[0].name = "AE2AutocraftingRegression";
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveCraftingTerminal)).SetValue(null, null);
            Main.recipe = new[] { Recipe.Create(ItemID.Torch, 3).AddIngredient(ItemID.Wood).AddIngredient(ItemID.Gel) };
            Recipe.numRecipes = 1;
            StorageSystem.Recipes.Build();
            using var world = new RegressionWorld();
            foreach (bool packet in new[] { false, true })
            {
                world.Reset();
                var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 30, 30);
                var assembler = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 31, 30);
                storage.Ledger.Restore(ItemKey.Crafted(ItemID.Torch), 5);
                storage.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), 2);
                storage.Ledger.Restore(ItemKey.Crafted(ItemID.Gel), 2);
                Order(assembler, packet);
                check(NetworkRegistry.GetLevel(MENetwork.CanonicalAnchor(MENetwork.Collect(31, 30)), Main.LocalPlayer.name)
                    >= NetworkRegistry.LevelUse, "legacy order fixture has an authorized named local player");
                check(assembler.Target.Type == ItemID.Torch, "no-CPU order reaches assembler through " + (packet ? "decoded order request" : "legacy target command"));
                for (int i = 0; i < MEMolecularAssemblerEntity.TicksPerItem + 25; i++) assembler.Update();
                check(storage.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 8
                    && storage.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == 1
                    && storage.Ledger.GetAmount(ItemKey.Crafted(ItemID.Gel)) == 1 && assembler.Target.IsAir,
                    "no-CPU manufacture adds full batch without consuming existing stock via " + (packet ? "decoded order request" : "legacy target command"));

                // A saved order remains a request for new output after reloading the entity.
                Order(assembler, packet);
                var saved = new TagCompound(); assembler.SaveData(saved);
                var restored = new MEMolecularAssemblerEntity(); restored.LoadData(saved);
                Terraria.DataStructures.TileEntity.ByPosition.Remove(assembler.Position);
                Terraria.DataStructures.TileEntity.ByID.Remove(assembler.ID);
                world.Place(restored, ModContent.TileType<MEMolecularAssemblerTile>(), 31, 30);
                for (int i = 0; i < MEMolecularAssemblerEntity.TicksPerItem + 25; i++) restored.Update();
                check(storage.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 11 && restored.Target.IsAir,
                    "pending no-CPU manufacture survives entity save/load");
            }

            world.Reset();
            var limited = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 30, 30);
            var worker = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 31, 30);
            limited.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), 1);
            limited.Ledger.Restore(ItemKey.Crafted(ItemID.Gel), 1);
            // Refusing the output partition tests failure after planning, without changing material availability.
            limited.IsWhitelist = true; limited.Filter.Add(ItemID.Wood); limited.Filter.Add(ItemID.Gel); limited.MarkFilterDirty();
            Order(worker, false);
            for (int i = 0; i < MEMolecularAssemblerEntity.TicksPerItem + 25; i++) worker.Update();
            check(limited.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == 1
                && limited.Ledger.GetAmount(ItemKey.Crafted(ItemID.Gel)) == 1
                && limited.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 0 && !worker.Target.IsAir,
                "blocked no-CPU output preserves both ingredients and pending order");
            limited.IsWhitelist = false; limited.Filter.Clear(); limited.MarkFilterDirty();
            for (int i = 0; i < MEMolecularAssemblerEntity.TicksPerItem + 65; i++) worker.Update();
            check(limited.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 3 && worker.Target.IsAir,
                "no-CPU manufacture resumes after output partition becomes available");

            world.Reset();
            var full = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 30, 30);
            worker = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 31, 30);
            full.Ledger.Restore(ItemKey.Crafted(ItemID.Torch), 5);
            full.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), 1);
            full.Ledger.Restore(ItemKey.Crafted(ItemID.Gel), 1);
            full.Ledger.MaxBytes = 1;
            Order(worker, false);
            for (int i = 0; i < MEMolecularAssemblerEntity.TicksPerItem + 25; i++) worker.Update();
            check(full.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 5
                && full.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == 1
                && full.Ledger.GetAmount(ItemKey.Crafted(ItemID.Gel)) == 1 && !worker.Target.IsAir,
                "full output capacity preserves existing target, materials and order");
            full.Ledger.MaxBytes = 8192;
            for (int i = 0; i < MEMolecularAssemblerEntity.TicksPerItem + 65; i++) worker.Update();
            check(full.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 8 && worker.Target.IsAir,
                "pending manufacture resumes when storage capacity is restored");

            // Core tests the shared authority operation; the client render suite separately
            // clicks a fully initialized order button. No server fixture needs UI/font setup.
            world.Reset();
            var host = world.Place(new MECraftingTerminalEntity(), ModContent.TileType<MECraftingTerminalTile>(), 30, 30);
            var stock = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 31, 30);
            stock.Ledger.Restore(ItemKey.Crafted(ItemID.Torch), 5);
            stock.Ledger.Restore(ItemKey.Crafted(ItemID.Wood), 1);
            stock.Ledger.Restore(ItemKey.Crafted(ItemID.Gel), 1);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveTerminalPos)).SetValue(null, host.Position);
            Main.LocalPlayer.Center = new Vector2(host.Position.X * 16f + 8f, host.Position.Y * 16f + 8f);
            NetworkRegistry.TryClaim(MENetwork.CanonicalAnchor(MENetwork.Collect(30, 30)), Main.LocalPlayer.name);
            UISystem.SetActiveNetwork(new() { stock }, null, host);
            check(TerminalCraftingExecutor.Submit(host,ItemKey.Crafted(ItemID.Torch),1,Main.LocalPlayer.name,out string result)
                && host.Jobs.Count == 0 && stock.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 8 && result.StartsWith("合成完成"),
                "shared manual order completes new output synchronously on the integrated terminal");
            var accessView = new TerminalUIState();
            typeof(TerminalUIState).GetField("_selected", Hidden).SetValue(accessView, ItemKey.Crafted(ItemID.Torch));
            typeof(TerminalUIState).GetMethod("OrderCraft", Hidden).Invoke(accessView, null);
            check(host.Jobs.Count == 0 && stock.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == 0,
                "access view cannot enqueue crafts or consume materials through the terminal command");
            host.Update();
            check(host.Jobs.Count == 0 && stock.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch)) == 8
                && stock.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == 0
                && stock.Ledger.GetAmount(ItemKey.Crafted(ItemID.Gel)) == 0,
                "world update after an instant command cannot duplicate its completed output");
        }
        finally
        {
            Main.recipe = recipes; Recipe.numRecipes = count; StorageSystem.Recipes.Build();
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveAssembler)).SetValue(null, activeAssembler);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveCraftingCpu)).SetValue(null, activeCpu);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveCraftingTerminal)).SetValue(null, activeTerminal);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveTerminalPos)).SetValue(null, activePosition);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveNetwork)).SetValue(null, activeNetwork);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveNetworkContents)).SetValue(null, activeContents);
            Main.player = players; Main.myPlayer = myPlayer;
            UIStatus.Clear();
        }
    }

    private static void Order(MEMolecularAssemblerEntity assembler, bool packet)
    {
        // Both retained legacy handlers run; this fixture does not test transport.
        Main.netMode = NetmodeID.SinglePlayer;
        typeof(UISystem).GetProperty(nameof(UISystem.ActiveTerminalPos)).SetValue(null, assembler.Position);
        Main.LocalPlayer.Center = new Vector2(assembler.Position.X * 16f + 8f, assembler.Position.Y * 16f + 8f);
        // The handler fixture represents an owner using this legacy network, not an anonymous menu player.
        NetworkRegistry.TryClaim(MENetwork.CanonicalAnchor(MENetwork.Collect(assembler.Position.X, assembler.Position.Y)), Main.LocalPlayer.name);
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, true);
        writer.Write((int)ItemID.Torch); writer.Write(0); writer.Write(string.Empty);
        if (packet) writer.Write(1L);
        data.Position = 0;
        using var reader = new BinaryReader(data);
        typeof(MENet).GetMethod("HandleOnServer", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { packet ? MENet.ClientOrder : MENet.ClientSetTarget, assembler.Position, reader, 0 });
    }
}
