using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Content.Items;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.Systems;

/// <summary>Explicitly enabled menu-only UI render regression. Uses temporary entities; no world or character I/O.</summary>
public sealed class UiRegressionSystem : ModSystem
{
    private bool _done;
    private int _menuFrames;
    public override void Load()
    {
        if (!Main.dedServ && Environment.GetEnvironmentVariable("AE2_UI_REGRESSION")=="1") On_Main.Draw += DrawForValidation;
    }
    public override void Unload() { On_Main.Draw -= DrawForValidation; }
    private void DrawForValidation(On_Main.orig_Draw orig, Main self, GameTime gameTime)
    {
        orig(self,gameTime);
        if (Main.gameMenu && Main.menuMode == 0 && StorageSystem.Ready
            && !(bool)(typeof(ModLoader).GetField("isLoading",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)?.GetValue(null) ?? true)
            && ++_menuFrames>120)
            RenderChecks();
    }
    private static void SetViewport(int width,int height,float scale)
    {
        Main.screenWidth=width;Main.screenHeight=height;
        Terraria.GameInput.PlayerInput.CacheOriginalScreenDimensions();Main.UIScale=scale;
    }
    private static Action CaptureViewport()
    {
        int width=Main.screenWidth,height=Main.screenHeight;
        float scale=Main.UIScaleWanted;
        var original=Terraria.GameInput.PlayerInput.OriginalScreenSize;
        return () =>
        {
            SetViewport((int)original.X,(int)original.Y,scale);
            Main.screenWidth=width;Main.screenHeight=height;
        };
    }
    private static void Click(MovableWindowState state,string name)
    {
        var button=(UIElement)state.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(state);
        button.LeftClick(new UIMouseEvent(button,button.GetDimensions().Center()));
    }
    private static System.Reflection.FieldInfo RequiredField(object instance, string name) =>
        instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingFieldException(instance.GetType().FullName, name);
    private static T Field<T>(object instance, string name) => (T)RequiredField(instance, name).GetValue(instance);
    private static void SetField(object instance, string name, object value) => RequiredField(instance, name).SetValue(instance,value);
    private static void Invoke(object instance, string name) => instance.GetType().GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(instance,null);
    private static T TupleField<T>(object tuple, string name) => (T)tuple.GetType().GetField(name)!.GetValue(tuple);
    private static T WindowField<T>(MovableWindowState instance, string name) => (T)typeof(MovableWindowState).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(instance);
    private static UIChoiceMenu ActiveChoice() => (UIChoiceMenu)typeof(UIChoiceMenu).GetField("_active", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
    private static SearchUIState NewSearch()
    {
        var state = new SearchUIState(); state.Activate();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(SearchUIState).GetMethod("BuildLists",flags)!.Invoke(state,null);
        typeof(SearchUIState).GetMethod("Refresh",flags)!.Invoke(state,null);
        return state;
    }
    private static TerminalUIState NewTerminal(TerminalViewMode mode)
    {
        typeof(UISystem).GetProperty(nameof(UISystem.ActiveTerminalViewMode))!.SetValue(null, mode);
        var state = new TerminalUIState(); state.Activate(); state.ResetForOpen(); return state;
    }
    private void Assert(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("AE2 BROWSER CHECK FAILED: " + label);
        Mod.Logger.Info("AE2 BROWSER CHECK PASS: " + label);
    }
    private void ValidateUnifiedBackNavigation()
    {
        foreach (bool terminal in new[] { false, true })
        {
            MovableWindowState state = terminal ? NewTerminal(TerminalViewMode.Craft) : NewSearch();
            var reader = Field<UIBrowserDetails>(state, terminal ? "_info" : "_details");
            var grid = Field<UIItemGrid>(state, "_grid");
            // Observe the initial network snapshot before measuring navigation.
            state.Update(new GameTime());
            int revision = grid.Revision;
            float scroll = grid.ScrollRatio;
            reader.Show(ItemKey.Crafted(ItemID.WoodenSword), 7);
            state.Update(new GameTime());
            if (!terminal)
            {
                for (int frame = 0; frame < 12; frame++) state.Update(new GameTime());
                var detailBounds = reader.GetDimensions(); var gridBounds = grid.GetDimensions();
                Assert(Math.Abs(detailBounds.Y + detailBounds.Height - gridBounds.Y - gridBounds.Height) < 1,
                    "JEI detail covers the extended catalogue through its bottom edge");
            }
            var back = Field<UIButton>(reader, "_back");
            var forward = Field<UIButton>(reader, "_forward");
            void Back() => back.LeftClick(new UIMouseEvent(back, back.GetDimensions().Center()));
            Assert(back.Enabled && !forward.Enabled, "first detail page has an active return to catalogue");
            Assert(reader.Children.OfType<UIButton>().Count(button => button.Text.StartsWith("返回", StringComparison.Ordinal)) == 1,
                "details expose one return action in the upper left");
            Back();
            Assert(!reader.IsOpen && reader.Parent == null && grid.Parent != null,
                "root back closes details and reveals the existing catalogue");
            Assert(!terminal || Field<UIElement>(state, "_stationGrid").Parent != null,
                "root back restores the craft catalogue station slots");
            reader.Show(ItemKey.Crafted(ItemID.WoodenSword), 7);
            reader.Show(ItemKey.Crafted(ItemID.Torch), 3);
            Back();
            Assert(reader.IsOpen && reader.SelectedKey.Type == ItemID.WoodenSword && reader.Quantity == 7 && forward.Enabled,
                "unified back restores the previous item and quantity before returning to catalogue");
            forward.LeftClick(new UIMouseEvent(forward, forward.GetDimensions().Center()));
            Assert(reader.SelectedKey.Type == ItemID.Torch && reader.Quantity == 3,
                "forward remains usable after unified back");
            Back(); Back();
            Assert(!reader.IsOpen && grid.Revision == revision && grid.ScrollRatio == scroll,
                "exhausting detail history returns to the catalogue without rebuilding or scrolling it");
        }
    }
    private void ValidateBrowserPreferences()
    {
        using var preferences = BrowserPreferences.Isolate(Path.Combine(Main.SavePath, "AE2-Validation", "preferences-" + Guid.NewGuid().ToString("N")));
        BrowserPreferences.Write("AE2-BrowserFavorites.json", new[] { BrowserContent.Name(ItemID.Wood), "item:UnavailableMod/PreservedItem" });
        BrowserFavorites.Reset();
        Assert(BrowserFavorites.AlwaysVisible, "new and legacy favorite profiles display a pinned tree immediately by default");
        BrowserFavorites.Move(new Vector2(140.5f, 90.5f)); BrowserFavorites.Reset();
        Assert(BrowserFavorites.Position == new Vector2(140.5f, 90.5f), "pinned tree position survives preference reload without changing bookmark identities");
        Assert(BrowserFavorites.Contains(ItemID.Wood) && BrowserFavorites.Entries.Count == 2, "legacy favorites and temporarily disabled mod references load without loss");
        var torchRecipes = StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(ItemID.Torch));
        int selectedRecipe = torchRecipes[^1];
        BrowserFavorites.Toggle(ItemID.Torch, recipeIndex: selectedRecipe, quantity: 12);
        BrowserFavorites.AlwaysVisible = true;
        BrowserFavorites.Reset();
        var bookmark = BrowserFavorites.Entries.First(entry => entry.Content == BrowserContent.Name(ItemID.Torch));
        Assert(BrowserFavorites.Contains(ItemID.Torch) && BrowserFavorites.Contains(ItemID.Wood) && BrowserFavorites.AlwaysVisible
            && bookmark.Quantity == 12 && bookmark.Recipe == BrowserContent.RecipeKey(Main.recipe[selectedRecipe]),
            "bookmark recipe quantity and eye visibility survive preference reload");
        Assert(BrowserFavorites.Remove("item:UnavailableMod/PreservedItem") && BrowserFavorites.Entries.All(entry => entry.Content != "item:UnavailableMod/PreservedItem"),
            "disabled-mod favorites remain removable by their stable content name");
        var originalRecipes = Main.recipe; int count = Recipe.numRecipes;
        var originalIndex = StorageSystem.Recipes;
        var recipeObject = Main.recipe[selectedRecipe];
        try
        {
            Main.recipe = originalRecipes.Take(count).Reverse().ToArray();
            var reversed = new RecipeIndex(); reversed.Build();
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Recipes))!.SetValue(null, reversed);
            int resolved = BrowserContent.ResolveRecipe(ItemID.Torch, bookmark.Recipe);
            Assert(resolved >= 0 && ReferenceEquals(Main.recipe[resolved], recipeObject), "saved recipe identities survive recipe array reordering");
        }
        finally
        {
            Main.recipe = originalRecipes; Recipe.numRecipes = count;
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Recipes))!.SetValue(null, originalIndex);
        }
        for (int i = 0; i < 12; i++)
            BrowserHistory.Record(new BrowserVisit(BrowserContent.Name(ItemID.Wood + i), 0, null, 0, "", i, 0, i + 1));
        BrowserHistory.Reset();
        Assert(BrowserHistory.Entries.Count == 10 && BrowserHistory.Entries[0].Quantity == 12 && BrowserHistory.Entries[^1].Quantity == 3,
            "exactly the last ten visits survive a browser preference reload");
        var missing = new BrowserVisit("item:UnavailableMod/PreservedItem", 0, null, 0, "", 0, 0, 1);
        BrowserHistory.Record(missing); BrowserHistory.Reset();
        Assert(BrowserHistory.Entries[0] == missing && !missing.TryResolve(out _, out _), "disabled content history is preserved without remapping to another runtime item");
        var invalid = new BrowserVisit(BrowserContent.Name(ItemID.Wood), 0, "invalid item payload", 0, "", 0, 0, 1);
        Assert(!invalid.TryResolve(out _, out _), "malformed history item data is rejected without crashing a browser");
        Assert(!BrowserContent.Resolve("item:UnqualifiedContentName", out _, out _), "malformed content names are rejected before mod registry lookup");
        string historyPath = BrowserPreferences.FilePath("AE2-BrowserHistory.json");
        File.WriteAllText(historyPath, "{"); BrowserHistory.Reset(); BrowserHistory.Record(missing);
        Assert(File.ReadAllText(historyPath) == "{", "invalid preference content is preserved instead of silently overwritten");
        string favoritesPath = BrowserPreferences.FilePath("AE2-BrowserFavorites.json");
        File.WriteAllText(favoritesPath, "{"); BrowserFavorites.Reset();
        Assert(!BrowserFavorites.Toggle(ItemID.Wood) && File.ReadAllText(favoritesPath) == "{",
            "invalid favorite settings remain recoverable and report that changes could not be persisted");
        string altar = RecipeStation.DescribeStation(TileID.DemonAltar);
        Assert(!string.IsNullOrWhiteSpace(altar) && !altar.Contains("#") && !altar.Contains("26"), "unplaceable altar station uses its real localized map name");
    }
    private void ValidateAltFavorites()
    {
        using var preferences = BrowserPreferences.Isolate(Path.Combine(Main.SavePath, "AE2-Validation", "alt-" + Guid.NewGuid().ToString("N")));
        var oldKeys = Main.keyState; int oldX = Main.mouseX, oldY = Main.mouseY, oldCursor = Main.cursorOverride;
        try
        {
            foreach (bool terminal in new[] { false, true })
            {
                MovableWindowState state = terminal ? NewTerminal(TerminalViewMode.Access) : NewSearch();
                var grid = Field<UIItemGrid>(state, "_grid");
                state.Update(new GameTime()); var d = grid.GetDimensions();
                Main.mouseX = (int)d.X + 12; Main.mouseY = (int)d.Y + 12;
                Main.keyState = new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.LeftAlt);
                grid.Update(new GameTime()); var key = grid.HoveredKey;
                long before = UISystem.GetStoredAmount(key); var mouseItem = Main.mouseItem;
                grid.LeftClick(new UIMouseEvent(grid, Main.MouseScreen));
                Assert(BrowserFavorites.Contains(key.Type) && Main.cursorOverride == Terraria.ID.CursorOverrideID.FavoriteStar,
                    "Alt click bookmarks the hovered item with the native star cursor");
                grid.Update(new GameTime());
                Main.mouseX = (int)d.X + 12; Main.mouseY = (int)d.Y + 12;
                // In JEI the newly favored item sorts to the first position; the terminal key remains stable.
                grid.LeftClick(new UIMouseEvent(grid, Main.MouseScreen));
                Assert(UISystem.GetStoredAmount(key) == before && ReferenceEquals(Main.mouseItem, mouseItem),
                    "repeated Alt clicks never withdraw items or change the held stack");
                if (BrowserFavorites.Contains(key.Type)) BrowserFavorites.Toggle(key.Type);
                Assert(terminal || !Field<UIBrowserDetails>(state, "_details").IsOpen,
                    "Alt favorite does not navigate away from the item catalogue");
            }
        }
        finally { Main.keyState = oldKeys; Main.mouseX = oldX; Main.mouseY = oldY; Main.cursorOverride = oldCursor; }
    }
    private void ValidateSelectedRecipeFavorite()
    {
        using var preferences = BrowserPreferences.Isolate(Path.Combine(Main.SavePath, "AE2-Validation", "variant-" + Guid.NewGuid().ToString("N")));
        int type = StorageSystem.Recipes.OutputTypes.First(output => StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(output))
            .Select(index => BrowserContent.RecipeKey(Main.recipe[index])).Distinct().Count() > 1);
        var recipes = StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(type));
        string first = BrowserContent.RecipeKey(Main.recipe[recipes[0]]);
        int page = Enumerable.Range(1, recipes.Count - 1).First(index => BrowserContent.RecipeKey(Main.recipe[recipes[index]]) != first);
        var search = NewSearch(); var reader = Field<UIBrowserDetails>(search, "_details");
        reader.Show(ItemKey.Crafted(type), 7); SetField(reader, "_recipe", page); Invoke(reader, "Rebuild");
        var recipe = Main.recipe[recipes[page]];
        long batches = 7 / Math.Max(1, recipe.createItem.stack) + (7 % Math.Max(1, recipe.createItem.stack) > 0 ? 1 : 0);
        var required = Field<IList>(reader, "_ingredients").Cast<object>().Select(card => (long)card.GetType().GetProperty("Required").GetValue(card)).ToArray();
        Assert(required.SequenceEqual(recipe.requiredItem.Where(item => !item.IsAir).Select(item => batches * item.stack)),
            "single-line ingredient requirements match the selected target quantity and actual batch output");
        RenderProbe(search, "Recipe-reader-selected-favorite");
        var d = reader.GetDimensions(); var point = new Vector2(d.X + 15, d.Y + 40);
        var keys = Main.keyState;
        try
        {
            Main.keyState = new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.LeftAlt);
            reader.LeftClick(new UIMouseEvent(reader, point));
            var saved = BrowserFavorites.Entries.Single();
            Assert(saved.Recipe == BrowserContent.RecipeKey(Main.recipe[recipes[page]]) && saved.Quantity == 7,
                "Alt favorite on a detail title preserves the selected alternate recipe and quantity");
            Main.keyState = new Microsoft.Xna.Framework.Input.KeyboardState();
            reader.LeftClick(new UIMouseEvent(reader, point));
            Assert(reader.Quantity == 7 && Field<int>(reader, "_recipe") == page,
                "normal detail title click never resets the order quantity or selected recipe");
        }
        finally { Main.keyState = keys; }
    }
    private sealed class PinnedTreeProbe : UIElement
    {
        protected override void DrawSelf(SpriteBatch batch) => FavoriteTreeHud.Draw(batch);
    }
    private void ValidatePinnedTreeHud()
    {
        using var preferences = BrowserPreferences.Isolate(Path.Combine(Main.SavePath, "AE2-Validation", "pins-" + Guid.NewGuid().ToString("N")));
        bool menu = Main.gameMenu, inventory = Main.playerInventory;
        var restoreViewport = CaptureViewport();
        try
        {
            SetViewport(1280, 720, 1f);
            BrowserFavorites.Toggle(ItemID.NightsEdge); BrowserFavorites.Toggle(ItemID.Torch);
            BrowserFavorites.AlwaysVisible = false;
            Main.gameMenu = false; Main.playerInventory = false;
            Assert(!FavoriteTreeHud.Visible, "inventory-only pinned trees remain hidden while the inventory is closed");
            BrowserFavorites.AlwaysVisible = true;
            for (int tick = 0; tick < 150; tick++) FavoriteTreeHud.Update(new GameTime());
            var trees = (List<BookmarkTree>)typeof(FavoriteTreeHud).GetField("Trees", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            Assert(FavoriteTreeHud.Visible && trees.Count == 2 && trees.All(tree => tree.Complete), "multiple real pinned recipes finish incremental expansion and remain visible without an inventory");
            Assert(trees.All(tree => tree.Nodes.Count > 1), "each pinned recipe includes its material children");
            var box = FavoriteTreeHud.Bounds;
            Assert(box.X >= 0 && box.Right <= MovableWindowState.ViewportSize.X && box.Bottom <= MovableWindowState.ViewportSize.Y,
                "pinned recipe trees remain within the logical UI viewport");
            RenderProbe(new PinnedTreeProbe(), "Pinned-trees-head");
            typeof(FavoriteTreeHud).GetField("_scroll", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, int.MaxValue);
            FavoriteTreeHud.Update(new GameTime()); RenderProbe(new PinnedTreeProbe(), "Pinned-trees-tail");
            BrowserFavorites.AlwaysVisible = false;
            Assert(!FavoriteTreeHud.Visible, "eye setting switches the pinned tree back to inventory-only visibility");
            Main.playerInventory = true;
            Assert(FavoriteTreeHud.Visible, "opening the native inventory reveals inventory-only pinned trees");
            var terminalProperty = typeof(UISystem).GetProperty(nameof(UISystem.TerminalState));
            var oldTerminal = UISystem.TerminalState; var oldState = UISystem.Interface.CurrentState;
            try
            {
                var terminal = NewTerminal(TerminalViewMode.Access);
                terminalProperty.SetValue(null, terminal); UISystem.Interface.SetState(terminal);
                FavoriteTreeHud.Update(new GameTime());
                var owned = (Dictionary<int, long>)typeof(FavoriteTreeHud).GetField("Owned", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                long networkWood = UISystem.ActiveNetworkContents.Where(item => item.Key.Type == ItemID.Wood).Sum(item => item.Value);
                Assert(owned.GetValueOrDefault(ItemID.Wood) == networkWood, "open readable terminal supplies the live network source for pinned material counts");
                UISystem.Interface.SetState(null); FavoriteTreeHud.Update(new GameTime());
                Assert(owned.GetValueOrDefault(ItemID.Wood) == networkWood,
                    "closing the terminal retains an authorized live network source for pinned material counts");
            }
            finally { UISystem.Interface.SetState(oldState); terminalProperty.SetValue(null, oldTerminal); }
        }
        finally { Main.gameMenu = menu; Main.playerInventory = inventory; FavoriteTreeHud.Reset(); restoreViewport(); }
    }
    private void ValidatePartialGrid()
    {
        var grid = new UIItemGrid { CellSize = 44 };
        grid.Left.Set(20, 0); grid.Top.Set(20, 0); grid.Width.Set(176, 0); grid.Height.Set(105, 0);
        grid.Activate(); grid.SetItems(Enumerable.Range(1, 40).ToList()); grid.Recalculate();
        int oldX = Main.mouseX, oldY = Main.mouseY;
        try
        {
            Main.mouseX = 20 + 44 + 12; Main.mouseY = 20 + 88 + 8; grid.Update(new GameTime());
            Assert(grid.HoveredType == 10 && grid.HasEntryAt(Main.MouseScreen), "visible fragment of the next grid row is interactive at its actual item index");
            Main.mouseY = 126; grid.Update(new GameTime());
            Assert(grid.HoveredType == -1 && !grid.HasEntryAt(Main.MouseScreen), "clipped grid fragments cannot receive input below the grid");
            RenderProbe(grid, "Grid-partial-rows");
        }
        finally { Main.mouseX = oldX; Main.mouseY = oldY; }
    }
    private void ValidateBrowser()
    {
        Assert(StorageSystem.Recipes.OutputTypes.All(type => StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(type)).All(index => Main.recipe[index].createItem.type == type && !Main.recipe[index].Disabled)),
            "every indexed output matches final sorted recipe array");
        Assert(StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(ItemID.TerraBlade)).All(i => Main.recipe[i].createItem.type == ItemID.TerraBlade), "Terra Blade lookup returns Terra Blade recipes");
        var search = NewSearch(); var grid = Field<UIItemGrid>(search,"_grid");
        int revision=grid.Revision; var before=grid.GetDimensions();
        int mouseX = Main.mouseX, mouseY = Main.mouseY; var hoverItem = Main.HoverItem; string hoverName = Main.hoverItemName;
        try
        {
            Main.mouseX=(int)before.X+20; Main.mouseY=(int)before.Y+20;
            grid.Update(new GameTime()); grid.DrawTooltip(null);
            Assert(grid.HoveredType>0 && Main.HoverItem.type==grid.HoveredType,"hover supplies actual item to native tooltip");
            search.ShowDetails(ItemID.Torch);
            var drawer=Field<UIBrowserDetails>(search,"_details").GetDimensions();
            Main.mouseX=(int)drawer.X+25; Main.mouseY=(int)drawer.Y+25; grid.Update(new GameTime());
            Assert(grid.HoveredKey.IsAir && grid.HoveredType<0,"drawer blocks catalogue hover and clears previous item key");
        }
        finally { Main.mouseX=mouseX; Main.mouseY=mouseY; Main.HoverItem=hoverItem; Main.hoverItemName=hoverName; }

        search.ShowDetails(ItemID.Torch); search.ShowDetails(ItemID.TerraBlade);
        Assert(grid.Revision==revision && grid.GetDimensions().ToRectangle()==before.ToRectangle(),"selection preserves grid dimensions and list revision");
        Assert(search.CloseDetails() && !search.CloseDetails(),"drawer closes independently of catalogue");
        Click(search,"_npcTab");
        var entries=Field<List<int>>(search,"_filtered");
        Assert(entries.Contains(NPCID.Guide) && entries.Contains(NPCID.Bunny) && entries.Contains(NPCID.BlueSlime),"NPC catalogue includes town NPC, critter and hostile");
        grid.Update(new GameTime());
        Assert(!grid.NpcLabels && grid.Columns == Math.Max(1,(int)(grid.GetDimensions().Width/grid.CellSize)),
            "NPC catalogue uses compact square cells without permanent name captions");
        var bestiaryOrder=entries.Where(ContentSamples.NpcBestiarySortingId.ContainsKey)
            .Select(type=>ContentSamples.NpcBestiarySortingId[type]).ToArray();
        Assert(bestiaryOrder.Zip(bestiaryOrder.Skip(1),(left,right)=>left<=right).All(ordered=>ordered),
            "default NPC catalogue follows the loaded bestiary order while retaining all NPC categories");
        try
        {
            var dimensions=grid.GetDimensions();
            int column=Math.Min(2,grid.Columns-1);
            Main.mouseX=(int)(dimensions.X+(column+.5f)*grid.CellSize);
            Main.mouseY=(int)(dimensions.Y+.5f*grid.CellSize);
            grid.Update(new GameTime());
            Assert(grid.HoveredType==entries[column],"compact NPC hover targets the displayed portrait rather than the former wide card");
        }
        finally { Main.mouseX=mouseX; Main.mouseY=mouseY; }
        Assert(BrowserCategories.NpcKind(NPCID.Guide)==3 && BrowserCategories.NpcKind(NPCID.Bunny)==4,"NPC property filters classify town NPC and critter");
        Assert(BrowserCategories.Items.Length==21 && BrowserCategories.Items.All(c=>c.Name!="材料" && c.Name!="套装"),
            "Recipe Browser categories merge armor while material remains a filter");
        var weapons=Array.FindIndex(BrowserCategories.Items,c=>c.Name=="武器");
        Assert(BrowserCategories.Matches(ItemID.CopperShortsword,weapons,0) && !BrowserCategories.Matches(ItemID.Wood,weapons,0),"weapon category uses item properties");
        var terminal=NewTerminal(TerminalViewMode.Access);
        Assert(Field<List<ItemKey>>(terminal,"_filtered").Any(k=>k.Prefix!=0),"stored view retains prefixed identities");
        Assert(Field<UIElement>(terminal,"_tasksButton").Parent == null && Field<UIElement>(terminal,"_stationsButton").Parent == null
            && Field<UIElement>(terminal,"_craftableButton").Parent == null && Field<UIElement>(terminal,"_playerInventory").Parent == null,
            "access terminal uses native inventory without an embedded backpack or crafting tabs");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(TerminalUIState).GetMethod("Select",flags)!.Invoke(terminal,new object[] { ItemKey.Crafted(ItemID.Wood) });
        Assert(!Field<UIBrowserDetails>(terminal,"_info").IsOpen && Field<UIElement>(terminal,"_takeButton").Parent != null
            && Field<UIElement>(terminal,"_orderButton").Parent == null,
            "access selection keeps inventory visible and exposes withdrawal rather than an order button");
        terminal.CloseDetails(); typeof(TerminalUIState).GetMethod("AfterNetworkChange",flags)!.Invoke(terminal,null);
        Assert(!Field<UIBrowserDetails>(terminal,"_info").IsOpen,"background inventory refresh does not reopen closed details");
        var craft = NewTerminal(TerminalViewMode.Craft);
        Assert(Field<int>(craft,"_range") == 1 && Field<UIElement>(craft,"_tasksButton").Parent != null
            && Field<UIElement>(craft,"_stationsButton").Parent == null && Field<UIElement>(craft,"_stationGrid").Parent != null && Field<UIElement>(craft,"_depositButton").Parent == null
            && Field<UIElement>(craft,"_takeButton").Parent == null && Field<UIElement>(craft,"_playerInventory").Parent == null,
            "craft terminal defaults to checked craftability and has no ordinary storage actions or backpack");
        Click(craft,"_craftableButton");
        var recipes = Field<List<ItemKey>>(craft,"_filtered");
        Assert(Field<int>(craft,"_range") == 2 && recipes.Count > 0
            && recipes.All(k=>k.Prefix==0 && StorageSystem.Recipes.GetRecipesFor(k).Count>0),
            "all recipes view contains real recipe outputs, never stored prefixed products");
        Assert(Field<UIElement>(craft,"_stationGrid").Parent != null && Field<UIElement>(craft,"_playerInventory").Parent == null,
            "recipe page contains the compact station grid without a duplicate backpack");
        Click(craft,"_tasksButton");
        Assert(Field<UIElement>(craft,"_playerInventory").Parent == null,
            "task management does not leave the station backpack active");
        var reader = Field<UIBrowserDetails>(craft,"_info");
        reader.Show(ItemKey.Crafted(ItemID.WoodenSword), 10); reader.Close(); reader.Show(ItemKey.Crafted(ItemID.Torch), 1);
        Invoke(reader,"Back");
        Assert(!reader.IsOpen, "a new terminal catalogue selection returns directly to the catalogue");
        reader.ShowVisit(BrowserHistory.Entries.First(visit => visit.Content == BrowserContent.Name(ItemID.WoodenSword) && visit.Quantity == 10));
        Assert(reader.Quantity == 10 && Field<long>(craft,"_orderQuantity") == 10,
            "saved browser visit restores both preview and actual order quantity together");
        reader.ShowNpc(NPCID.Guide);
        Assert(Field<ItemKey>(craft,"_selected").IsAir && Field<UIElement>(craft,"_orderButton").Parent == null,
            "NPC navigation clears the previous craft target and its order action");
        reader.Close();
        Assert(UISystem.GetStoredAmount(ItemKey.Crafted(ItemID.Wood))==321,"catalogue interactions do not mutate storage");
        var orderDrive=UISystem.ActiveNetwork.OfType<MEDiskDriveEntity>().First();
        var orderStock=orderDrive.Ledger.Copy();
        try
        {
            reader.Show(ItemKey.Crafted(ItemID.Torch),3); UIStatus.Clear();
            long beforeOrder=orderDrive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch));
            Click(craft,"_orderButton");
            Assert(orderDrive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Torch))>=beforeOrder+3,
                "real order button stores output synchronously without running a terminal Update");
            Assert(UIStatus.Active().Any(line=>line.Text.StartsWith("合成完成")),
                "real instant order button displays completion rather than only a queue notice");
        }
        finally { orderDrive.Ledger.ReplaceContents(orderStock); UISystem.RefreshNetwork(); reader.Close(); UIStatus.Clear(); }
        EmbeddedInventoryRegression.Run(Assert);
        AEUiSessionRegression.Run(Assert);
        ValidateCompactControls();
        ValidateRecipeReader();
        ValidateNativeTransferAndCaches();
    }
    private void ValidateNativeTransferAndCaches()
    {
        var activeField=typeof(UserInterface).GetField("ActiveInstance",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)!;
        object activeUi=activeField.GetValue(null);
        var state=NewTerminal(TerminalViewMode.Access);
        var drive=UISystem.ActiveNetwork.OfType<MEDiskDriveEntity>().First();
        var stock=drive.Ledger.Copy(); var player=Main.LocalPlayer;
        var inventoryItem=player.inventory[45];var cursorSlot=player.inventory[58];var mouse=Main.mouseItem;
        bool inventoryOpen=Main.playerInventory;
        var oldUi=UISystem.Interface;var oldState=UISystem.TerminalState;var oldMode=UISystem.ActiveTerminalViewMode;
        try
        {
            var ui=new UserInterface();ui.SetState(state);
            typeof(UISystem).GetProperty(nameof(UISystem.Interface))!.SetValue(null,ui);
            typeof(UISystem).GetProperty(nameof(UISystem.TerminalState))!.SetValue(null,state);
            Main.playerInventory=true;Main.mouseItem=new Item();player.inventory[45]=new Item(ItemID.Wood,17);
            long before=drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood));
            Assert(player.GetModPlayer<MEPlayer>().ShiftClickSlot(player.inventory,0,45)
                && player.inventory[45].IsAir && drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood))==before+17,
                "native inventory shift click transfers the actual stack through the access terminal");
            drive.Ledger.MaxAmountPerType=before+20;
            Main.mouseItem=new Item(ItemID.Wood,9);
            Invoke(state,"DepositHeldItem");
            Assert(Main.mouseItem.stack==6 && player.inventory[58].stack==6
                && drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood))==before+20,
                "native cursor deposit preserves a partial remainder and its cursor-slot mirror");
            Invoke(state,"DepositHeldItem");
            Assert(Main.mouseItem.stack==6 && drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood))==before+20,
                "full storage cannot consume the remaining native cursor stack");
            player.inventory[45] = new Item(ItemID.Wood, 7); UIStatus.Clear();
            Assert(player.GetModPlayer<MEPlayer>().ShiftClickSlot(player.inventory,0,45)
                && player.inventory[45].stack == 7 && drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == before+20
                && UIStatus.Active().Any(line => line.Text.Contains("数量已达上限")),
                "full storage shift deposit preserves the item and tells the player why nothing moved");
        }
        finally
        {
            drive.Ledger.MaxAmountPerType=stock.MaxAmountPerType;drive.Ledger.ReplaceContents(stock);
            player.inventory[45]=inventoryItem;player.inventory[58]=cursorSlot;Main.mouseItem=mouse;Main.playerInventory=inventoryOpen;
            typeof(UISystem).GetProperty(nameof(UISystem.Interface))!.SetValue(null,oldUi);
            typeof(UISystem).GetProperty(nameof(UISystem.TerminalState))!.SetValue(null,oldState);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveTerminalViewMode))!.SetValue(null,oldMode);
            activeField.SetValue(null,activeUi);
            UISystem.RefreshNetwork();
        }
        ValidateAutomaticCataloguePreload();
        var availability=TerminalCraftingCacheSystem.Current;availability.Refresh();
        availability.Catalogue.Preload(StorageSystem.Recipes.OutputTypes);
        availability.Catalogue.Prioritize(new[]{(int)ItemID.Torch,(int)ItemID.WoodenSword});
        int computed=availability.Catalogue.PlansComputed;
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        availability.Catalogue.Advance(128,100);
        double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Assert(availability.Catalogue.IsCraftable(ItemID.Torch),"warm recipe catalogue resolves a prioritized real recipe with complete materials");
        var reopened=NewTerminal(TerminalViewMode.Craft);
        Assert(ReferenceEquals(availability,Field<TerminalCraftingAvailability>(reopened,"_availability"))
            && availability.Catalogue.PlansComputed>=computed && availability.Catalogue.IsCraftable(ItemID.Torch),
            "reopening a terminal retains its validated catalogue instead of discarding warm results");
        int pendingBefore = availability.Catalogue.PendingCount;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        bool sameCache = true;
        for (int i = 0; i < 1000; i++) sameCache &= ReferenceEquals(availability, TerminalCraftingCacheSystem.Current);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert(sameCache && availability.Catalogue.PendingCount == pendingBefore,
            "1000 unchanged warm-cache reads retain one catalogue without duplicating queued recipe work");
        Mod.Logger.Info("AE2 WARM CACHE LOOKUP BENCHMARK: reads=1000 allocatedBytes=" + allocated);
        var first=availability.Plan(ItemKey.Crafted(ItemID.Torch),25);int hits=availability.PreviewCacheHits;
        var second=availability.Plan(ItemKey.Crafted(ItemID.Torch),25);
        Assert(ReferenceEquals(first,second) && availability.PreviewCacheHits==hits+1,
            "unchanged selected quantity reuses its complete crafting preview after validating stock and conditions");
        Mod.Logger.Info("AE2 CRAFT CACHE BENCHMARK: computed="+(availability.Catalogue.PlansComputed-computed)
            +" feasibilityBuildMs="+availability.Catalogue.ReachabilityMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)
            +" fullPlans="+availability.Catalogue.FullPlansComputed
            +" elapsedMs="+elapsed.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)
            +" pending="+availability.Catalogue.PendingCount+"; real loaded recipes, temporary stock, no world opened.");
        RenderProbe(reopened,"Terminal-cached-catalogue");
        var terminal=UISystem.ActiveCraftingTerminal;var stations=terminal.Stations;
        try
        {
            terminal.Stations=Enumerable.Range(0,41).Select(i=>i==40 ? new Item() : new Item(ItemID.WorkBench)).ToArray();
            reopened.CloseDetails();
            reopened.Update(new GameTime());reopened.Recalculate();
            var grid=Field<UIStationGrid>(reopened,"_stationGrid");var rows=Field<UIList>(grid,"_rows");
            Assert(rows.Count>=2 && rows.Children.SelectMany(e=>e.Children).SelectMany(e=>e.Children).OfType<UIItemSlotElement>().Any()
                && grid.RequiredHeight<=104,"compact workstations use multiple columns and bounded scroll height");
            reopened.ShowDetails(ItemID.Torch); reopened.Update(new GameTime());
            RenderProbe(reopened,"Terminal-dense-stations");
            var details=Field<UIBrowserDetails>(reopened,"_info");var conditions=Field<Rectangle>(details,"_stationBounds");
            Assert(Field<IList>(details,"_ingredientHits").Cast<object>().All(hit=>!TupleField<Rectangle>(hit,"Item1").Intersects(conditions)),
                "dense station rows leave recipe ingredient cards and condition text in separate bounds");
            AssertIngredientVisualBounds(details,"dense station recipe");
            var alternative=details.IngredientVisuals.First(v=>!v.Alternatives.IsEmpty).Alternatives;
            details.LeftClick(new UIMouseEvent(details,alternative.Center.ToVector2()));
            var popup=Field<UIElement>(details,"_alternatives");
            Assert(popup?.Parent==details,"compact ingredient alternative button opens its material picker");
            RenderProbe(reopened,null);
            popup.LeftClick(new UIMouseEvent(popup,Field<Rectangle>(popup,"_closeBounds").Center.ToVector2()));
            Assert(popup.Parent==null,"compact material picker closes without consuming ingredient navigation");
            ValidateDenseIngredientScrolling(reopened,details);
        }
        finally {terminal.Stations=stations;}
    }

    private void AssertIngredientVisualBounds(UIBrowserDetails details,string context)
    {
        Assert(details.IngredientVisuals.Count>0 && details.IngredientVisuals.All(v=>
            !v.Name.IsEmpty && !v.Count.IsEmpty && v.Card.Contains(v.Icon) && v.Card.Contains(v.Name) && v.Card.Contains(v.Count)
            && !v.Name.Intersects(v.Count) && !v.Icon.Intersects(v.Name) && !v.Icon.Intersects(v.Count)
            && (v.Alternatives.IsEmpty || (v.Card.Contains(v.Alternatives) && !v.Alternatives.Intersects(v.Name)
                && !v.Alternatives.Intersects(v.Count) && !v.Alternatives.Intersects(v.Icon)))),
            context+": rendered ingredient names, quantities, icons and alternative buttons have distinct bounds inside their cards");
    }
    private void ValidateDenseIngredientScrolling(TerminalUIState state,UIBrowserDetails details)
    {
        int mouseX=Main.mouseX,mouseY=Main.mouseY,wheel=Terraria.GameInput.PlayerInput.ScrollWheelDelta;
        try
        {
            int recipeIndex=Enumerable.Range(0,Recipe.numRecipes).Where(i=>!Main.recipe[i].Disabled && Main.recipe[i].createItem.type>0)
                .OrderByDescending(i=>Main.recipe[i].requiredItem.Count(item=>!item.IsAir)).First();
            var recipe=Main.recipe[recipeIndex];state.ShowDetails(recipe.createItem.type);
            SetField(details,"_recipe",StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(recipe.createItem.type)).ToList().IndexOf(recipeIndex));
            Invoke(details,"Rebuild");RenderProbe(state,"Terminal-dense-long-materials");
            AssertIngredientVisualBounds(details,"dense station long recipe first page");
            int total=Field<IList>(details,"_ingredients").Count;
            int firstIndex=details.IngredientVisuals[0].Index;
            var firstCard=details.IngredientVisuals[0].Card;
            Assert(total>details.IngredientVisuals.Count,"dense station recipe fixture has offscreen ingredients to exercise scrolling");
            Main.mouseX=firstCard.Center.X;Main.mouseY=firstCard.Center.Y;
            Terraria.GameInput.PlayerInput.ScrollWheelDelta=-120*total;
            details.Update(new GameTime());RenderProbe(state,"Terminal-dense-long-materials-scrolled");
            Assert(Field<int>(details,"_scroll")>0 && details.IngredientVisuals[0].Index>firstIndex
                && details.IngredientVisuals.Any(v=>v.Index==total-1),
                "dense station ingredient wheel scrolling reaches the last material of the real recipe");
            AssertIngredientVisualBounds(details,"dense station long recipe last page");
        }
        finally {Main.mouseX=mouseX;Main.mouseY=mouseY;Terraria.GameInput.PlayerInput.ScrollWheelDelta=wheel;}
    }

    private void ValidateAutomaticCataloguePreload()
    {
        const System.Reflection.BindingFlags statics=System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var recipesProperty=typeof(StorageSystem).GetProperty(nameof(StorageSystem.Recipes),statics)!;
        var originalIndex=StorageSystem.Recipes;
        int originalRevision=originalIndex.Revision;
        Recipe[] originalRecipes=Main.recipe;
        int originalRecipeCount=Recipe.numRecipes;
        var cacheType=typeof(TerminalCraftingCacheSystem);
        var caches=(IDictionary)cacheType.GetField("Caches",statics)!.GetValue(null);
        var savedCaches=new List<DictionaryEntry>();
        foreach(DictionaryEntry entry in caches) savedCaches.Add(entry);
        var savedFields=new List<(System.Reflection.FieldInfo Field,object Value)>();
        foreach(string name in new[]{"_index","_indexRevision","_planner"})
        {
            var field=cacheType.GetField(name,statics) ?? throw new MissingFieldException(cacheType.FullName,name);
            savedFields.Add((field,field.GetValue(null)));
        }
        try
        {
            // Read the settled real recipes into a private index. Never rebuild the
            // global index or mutate Main.recipe to test a cache revision boundary.
            var temporaryIndex=new RecipeIndex();temporaryIndex.Build();
            recipesProperty.SetValue(null,temporaryIndex);
            int outputs=temporaryIndex.OutputTypes.Count;
            var created=TerminalCraftingCacheSystem.Current;
            Assert(outputs>0 && created.Catalogue.PendingCount==outputs && created.Catalogue.PlansComputed==0,
                "production cache creation automatically preloads every recipe output without a fixture Preload call");

            int revision=temporaryIndex.Revision;temporaryIndex.Build();
            var rebuilt=TerminalCraftingCacheSystem.Current;
            Assert(temporaryIndex.Revision>revision && !ReferenceEquals(created,rebuilt)
                && rebuilt.Catalogue.PendingCount==temporaryIndex.OutputTypes.Count && rebuilt.Catalogue.PlansComputed==0,
                "recipe-index revision replaces the production cache and automatically preloads its fresh catalogue");
        }
        finally
        {
            recipesProperty.SetValue(null,originalIndex);
            caches.Clear();foreach(var entry in savedCaches)caches.Add(entry.Key,entry.Value);
            foreach(var field in savedFields)field.Field.SetValue(null,field.Value);
        }
        Assert(ReferenceEquals(StorageSystem.Recipes,originalIndex) && originalIndex.Revision==originalRevision
            && ReferenceEquals(Main.recipe,originalRecipes) && Recipe.numRecipes==originalRecipeCount
            && caches.Count==savedCaches.Count && savedCaches.All(entry=>ReferenceEquals(caches[entry.Key],entry.Value)),
            "automatic preload fixture restores the original recipe index revision, recipe array and all warm-cache instances");
    }
    private void RenderProbe(UIElement element, string name)
    {
        var device=Main.instance.GraphicsDevice; var previous=device.GetRenderTargets();
        using var target=new RenderTarget2D(device,Main.screenWidth,Main.screenHeight);
        try
        {
            device.SetRenderTarget(target);device.Clear(new Color(12,16,24));
            using var batch=new SpriteBatch(device);
            batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.LinearClamp,DepthStencilState.None,RasterizerState.CullNone,null,Main.UIScaleMatrix);
            element.Draw(batch);batch.End();
            if(name!=null)
            {
                string directory=Path.Combine(Main.SavePath,"AE2-Validation");Directory.CreateDirectory(directory);
                using var file=File.Create(Path.Combine(directory,name+".png"));target.SaveAsPng(file,Main.screenWidth,Main.screenHeight);
                Mod.Logger.Info("AE2 UI RENDERED: "+name+" "+Main.screenWidth+"x"+Main.screenHeight+" scale="+Main.UIScale);
            }
        }
        finally {device.SetRenderTargets(previous);}
    }
    private void ValidateRecipeReader()
    {
        var restoreViewport=CaptureViewport();
        int width=Main.screenWidth,height=Main.screenHeight,mx=Main.mouseX,my=Main.mouseY,wheel=Terraria.GameInput.PlayerInput.ScrollWheelDelta;
        float scale=Main.UIScaleWanted;
        bool mouseLeft=Main.mouseLeft;
        try
        {
            SetViewport(1280,720,1f);
            Main.mouseX=0;Main.mouseY=0;Main.mouseLeft=false;Terraria.GameInput.PlayerInput.ScrollWheelDelta=0;
            var search=NewSearch();search.Layout.X=60;search.Layout.Y=50;search.Layout.W=920;search.Layout.H=620;
            // FitToScreen alone only resizes the window. Activate its child layout and
            // settle the grid's row/column calculation before recording scroll ratios.
            search.OnActivate();
            search.Update(new GameTime());search.Update(new GameTime());
            var details=Field<UIBrowserDetails>(search,"_details");var grid=Field<UIItemGrid>(search,"_grid");
            grid.ScrollRows(2);float catalogueScroll=grid.ScrollRatio;int revision=grid.Revision;
            int catalogueRow=Field<int>(grid,"_rowOffset");var catalogueBounds=grid.GetDimensions().ToRectangle();
            void LogCatalogue(string phase) => Mod.Logger.Info("AE2 RECIPE CATALOGUE "+phase+": revision="+grid.Revision+
                " row="+Field<int>(grid,"_rowOffset")+" ratio="+grid.ScrollRatio+" columns="+grid.Columns+
                " visibleRows="+grid.VisibleRows+" totalRows="+grid.TotalRows+" bounds="+grid.GetDimensions().ToRectangle());
            LogCatalogue("baseline");
            int largeIndex=Enumerable.Range(0,Recipe.numRecipes).Where(i=>!Main.recipe[i].Disabled && Main.recipe[i].createItem.type>0)
                .OrderByDescending(i=>Main.recipe[i].requiredItem.Count(item=>!item.IsAir)).First();
            Recipe large=Main.recipe[largeIndex];search.ShowDetails(large.createItem.type);
            SetField(details,"_recipe",StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(large.createItem.type)).ToList().IndexOf(largeIndex));Invoke(details,"Rebuild");
            for(int i=0;i<12;i++)search.Update(new GameTime(TimeSpan.FromSeconds(i/60.0),TimeSpan.FromSeconds(1.0/60)));
            LogCatalogue("reader-open");
            RenderProbe(search,"RecipeReader-long-materials");
            Assert(details.GetDimensions().Width>search.Layout.W*.9f,"recipe reader occupies main content width");
            Assert(Field<IList>(details,"_ingredients").Count==large.requiredItem.Count(item=>!item.IsAir),"all real recipe ingredients retained without eight-step or row truncation");
            var hits=Field<IList>(details,"_ingredientHits").Cast<object>().Where(hit=>!TupleField<bool>(hit,"Item3")).Select(hit=>TupleField<Rectangle>(hit,"Item1")).ToList();
            Assert(hits.Count>=2 && hits.Select(rect=>rect.X).Distinct().Count()>1,"wide reader renders multiple ingredient columns");
            Rectangle stationBounds=Field<Rectangle>(details,"_stationBounds");
            Assert(hits.All(rect=>rect.Bottom<=stationBounds.Top) && stationBounds.Bottom<=details.GetDimensions().Y+details.GetDimensions().Height,"ingredients and station conditions occupy separate nonoverlapping areas");
            var stationText=Field<List<string>>(details,"_stationText");
            for(int i=0;i<60;i++)stationText.Add("条件回归示例 "+i+"：较长中文环境条件必须完整换行，末尾标记可滚动查看。");
            SetField(details,"_stationLineWidth",-1f);RenderProbe(search,null);
            int oldIngredientScroll=Field<int>(details,"_scroll");
            Main.mouseX=stationBounds.Center.X;Main.mouseY=stationBounds.Center.Y;Terraria.GameInput.PlayerInput.ScrollWheelDelta=-360;
            details.Update(new GameTime());RenderProbe(search,"RecipeReader-conditions-scrolled");
            Assert(Field<Rectangle>(details,"_stationBounds").IsEmpty && Field<int>(details,"_stationScroll")==0
                && Field<int>(details,"_scroll")>oldIngredientScroll,"long material and condition lists use one continuous scroll area");
            Assert(Field<List<string>>(details,"_stationLines").Any(line=>line.Contains("可滚动查看")),"condition wrapping retains final text");
            Invoke(details,"Rebuild");SetField(details,"_scroll",int.MaxValue);RenderProbe(search,null);
            int savedScroll=Field<int>(details,"_scroll"),savedStations=Field<int>(details,"_stationScroll"),recipePage=Field<int>(details,"_recipe");
            details.Show(ItemKey.Crafted(ItemID.Torch));Field<UIButton>(details,"_back").LeftClick(new UIMouseEvent(details,Vector2.Zero));
            Assert(details.SelectedKey.Type==large.createItem.type && Field<int>(details,"_recipe")==recipePage && Field<int>(details,"_scroll")==savedScroll && Field<int>(details,"_stationScroll")==savedStations,"history restores selected recipe and independent scroll positions");
            details.Show(ItemKey.Crafted(ItemID.Wood), 10); details.Close(); details.Show(ItemKey.Crafted(ItemID.Torch), 1);
            Invoke(details,"Back");
            Assert(!details.IsOpen, "catalogue selection never inserts a previous closed item into its back path");
            details.ShowVisit(BrowserHistory.Entries.First(visit=>visit.Content==BrowserContent.Name(ItemID.Wood) && visit.Quantity==10));
            details.Show(ItemKey.Crafted(ItemID.Torch),1); Invoke(details,"Back");
            Assert(details.SelectedKey.Type==ItemID.Wood && details.Quantity==10,"recent visit restores the original item and quantity");
            Invoke(details,"Forward");
            Assert(details.SelectedKey.Type==ItemID.Torch && Field<long>(details,"_quantity")==1,
                "forward returns to the later catalogue selection");
            details.SetTab(2); Invoke(details,"Back");
            Assert(Field<int>(details,"_tab")==0,"back restores the preceding recipe tab");
            Invoke(details,"Forward");
            Assert(Field<int>(details,"_tab")==2,"forward restores the uses tab");
            details.ShowNpc(NPCID.Guide); details.Close(); details.Show(ItemKey.Crafted(ItemID.Gel)); Invoke(details,"Back");
            Assert(!details.IsOpen,"NPC pages from a previous catalogue visit do not intercept return to directory");
            details.ShowVisit(BrowserHistory.Entries.First(visit=>visit.Content==BrowserContent.Name(NPCID.Guide,true)));
            Assert(Field<int>(details,"_npc")==NPCID.Guide,"NPC visits remain accessible through saved recent history");
            details.Show(ItemKey.Crafted(ItemID.StoneBlock));
            Assert(!Field<UIButton>(details,"_forward").Enabled,"a new navigation branch clears forward history");
            int groupIndex=Enumerable.Range(0,Recipe.numRecipes).First(i=>!Main.recipe[i].Disabled && Main.recipe[i].acceptedGroups.Any(id=>RecipeGroup.recipeGroups[id].ValidItems.Count>1 && Main.recipe[i].requiredItem.Any(item=>RecipeGroup.recipeGroups[id].ValidItems.Contains(item.type))));
            var grouped=Main.recipe[groupIndex];details.Show(ItemKey.Crafted(grouped.createItem.type));
            SetField(details,"_recipe",StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(grouped.createItem.type)).ToList().IndexOf(groupIndex));Invoke(details,"Rebuild");RenderProbe(search,null);
            object groupHit=Field<IList>(details,"_ingredientHits").Cast<object>().First(hit=>TupleField<bool>(hit,"Item3"));
            var groupRect=TupleField<Rectangle>(groupHit,"Item1");object card=groupHit.GetType().GetField("Item2")!.GetValue(groupHit);
            int cardIndex=(int)card.GetType().GetProperty("Index")!.GetValue(card),oldType=(int)card.GetType().GetProperty("Type")!.GetValue(card);
            details.LeftClick(new UIMouseEvent(details,groupRect.Center.ToVector2()));var popup=Field<UIElement>(details,"_alternatives");
            Assert(popup?.Parent==details,"recipe-group alternatives open in an independent popup");RenderProbe(search,"RecipeReader-alternatives");
            var options=Field<int[]>(popup,"_types");var group=(RecipeGroup)card.GetType().GetProperty("Group")!.GetValue(card);
            Assert(group.ValidItems.ToHashSet().SetEquals(options),"alternative popup contains every accepted recipe-group item");
            object option=Field<IList>(popup,"_hits").Cast<object>().First(hit=>TupleField<int>(hit,"Item2")!=oldType);
            int choice=TupleField<int>(option,"Item2");popup.LeftClick(new UIMouseEvent(popup,TupleField<Rectangle>(option,"Item1").Center.ToVector2()));
            Assert(Field<UIElement>(details,"_alternatives")==null && Field<Dictionary<int,int>>(details,"_alternativeSelection")[cardIndex]==choice,"choosing an alternative updates display and closes popup");
            LogCatalogue("alternative-selected");
            Main.mouseX=0;Main.mouseY=0;Terraria.GameInput.PlayerInput.ScrollWheelDelta=0;
            details.Close();search.Update(new GameTime());LogCatalogue("reader-closed");
            Assert(ReferenceEquals(grid,Field<UIItemGrid>(search,"_grid")) && grid.Revision==revision,"recipe navigation retains catalogue instance and entries");
            Assert(grid.GetDimensions().ToRectangle()==catalogueBounds && Field<int>(grid,"_rowOffset")==catalogueRow,"recipe navigation retains catalogue bounds and first visible row");
            Assert(grid.Revision==revision && grid.ScrollRatio==catalogueScroll,"returning from recipe and alternatives preserves catalogue instance and scroll");

            ValidateCompletePlanReader();
        }
        finally {restoreViewport();Main.mouseX=mx;Main.mouseY=my;Main.mouseLeft=mouseLeft;Terraria.GameInput.PlayerInput.ScrollWheelDelta=wheel;}
    }
    private void ValidateCompletePlanReader()
    {
        Recipe[] originalRecipes = Main.recipe; int originalCount = Recipe.numRecipes;
        var originalIndex = StorageSystem.Recipes; var originalPlanner = StorageSystem.Planner;
        var originalProgression = StorageSystem.Progression; var originalAcquisition = StorageSystem.Acquisition;
        var originalStock = UISystem.ActiveNetworkContents;
        var recipeSetup = typeof(RecipeLoader).GetField("setupRecipes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(RecipeLoader).FullName,"setupRecipes");
        var recipeOwner = typeof(RecipeLoader).GetProperty("CurrentMod", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (recipeOwner?.SetMethod == null) throw new MissingMemberException(typeof(RecipeLoader).FullName,"CurrentMod");
        object originalSetup = recipeSetup.GetValue(null), originalOwner = recipeOwner.GetValue(null);
        try
        {
            // Run the real current planning path. The reader no longer owns the old
            // private plan cache; injecting its former fields bypassed this behavior.
            // Like the menu core runner, temporarily permit unregistered fixture recipes.
            recipeSetup.SetValue(null,true); recipeOwner.SetValue(null,Mod);
            var recipes = new Recipe[12];
            for (int i = 0; i < recipes.Length; i++)
                recipes[i] = Recipe.Create(i == recipes.Length - 1 ? ItemID.Torch : 1000 + i)
                    .AddIngredient(i == 0 ? ItemID.Wood : 999 + i);
            Main.recipe = recipes; Recipe.numRecipes = recipes.Length;
            var index = new RecipeIndex(); index.Build();
            var planner = new CraftPlanner(index, key => "回归步骤物品 " + key.Type
                + "：这是用于验证完整合成步骤与长中文文本换行的说明，结尾必须保留。终点" + key.Type);
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Recipes))!.SetValue(null,index);
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Planner))!.SetValue(null,planner);
            // These are loaded-world indices referencing the original recipe array.
            // This fixture tests planning and text, not progression/acquisition lookup.
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Progression))!.SetValue(null,null);
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Acquisition))!.SetValue(null,null);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveNetworkContents))!.SetValue(null,
                new Dictionary<ItemKey,long> { [ItemKey.Crafted(ItemID.Wood)] = 100000 });
            var plan = new TerminalCraftingAvailability().Plan(ItemKey.Crafted(ItemID.Torch),100000);
            Assert(plan.FullySatisfied && plan.Steps.Count == 12,
                "complete reader fixture plans twelve real dependent recipes using the current material and condition rules");
            var panel = new UIRecipePanel(); panel.Left.Set(20,0); panel.Top.Set(20,0);
            panel.Width.Set(340,0); panel.Height.Set(260,0); panel.Recalculate();
            panel.Show(ItemKey.Crafted(ItemID.Torch),100000);
            var text = Field<IList>(panel,"_lines").Cast<object>().Select(line=>(string)line.GetType().GetField("Text")!.GetValue(line)).ToList();
            Assert(plan.Steps.All(text.Contains) && Field<long>(panel,"_quantity") == 100000,
                "complete plan keeps every step and requested quantity above old display cap");
            RenderProbe(panel,null);
            string wrapped = string.Concat(Field<IList>(panel,"_wrappedLines").Cast<object>().Select(line=>TupleField<string>(line,"Item2")));
            Assert(plan.Steps.All(step=>wrapped.Contains(step)),"long plan text wraps without losing any content");
            panel.ScrollOffset = int.MaxValue; RenderProbe(panel,"RecipeReader-complete-plan-tail");
            Assert(panel.ScrollOffset > 0,"complete long plan tail is reachable by scrolling");
            var deepRecipes = new Recipe[26];
            for (int i = 0; i < deepRecipes.Length; i++)
                deepRecipes[i] = Recipe.Create(i == deepRecipes.Length - 1 ? ItemID.Torch : 1500 + i)
                    .AddIngredient(i == 0 ? ItemID.Wood : 1499 + i);
            deepRecipes[^1].AddIngredient(1504);
            Main.recipe = deepRecipes; Recipe.numRecipes = deepRecipes.Length;
            var deepIndex = new RecipeIndex(); deepIndex.Build();
            typeof(StorageSystem).GetProperty(nameof(StorageSystem.Recipes))!.SetValue(null, deepIndex);
            var tree = new BookmarkTree(new BrowserBookmark(BrowserContent.Name(ItemID.Torch), BrowserContent.RecipeKey(deepRecipes[^1])));
            for (int tick = 0; tick < 1000 && !tree.Complete; tick++) tree.Advance(4);
            Assert(tree.Complete && tree.MaxDepth > 16 && tree.Nodes.Any(node => node.Item == ItemID.Wood)
                && Enumerable.Range(1500, 25).All(type => tree.Nodes.Any(node => node.Item == type)),
                "incremental pinned tree retains every intermediate and final leaf beyond sixteen levels");
            Assert(tree.Nodes.Any(node => node.Reference >= 0), "shared ingredient subtrees retain a navigable reference instead of repeated expansion");
            deepRecipes[0].requiredItem[0].SetDefaults(ItemID.Torch);
            var cycleTree = new BookmarkTree(new BrowserBookmark(BrowserContent.Name(ItemID.Torch)));
            for (int tick = 0; tick < 1000 && !cycleTree.Complete; tick++) cycleTree.Advance(4);
            Assert(cycleTree.Complete && cycleTree.Nodes.Count == 1 && cycleTree.Nodes[0].Item == ItemID.Torch
                && !cycleTree.Nodes.Any(node => node.Cycle), "cyclic conversion is removed from its first material without a repeated item or cycle notice");
            BrowserRefinementRegression.Trees(Assert);
        }
        finally
        {
            try
            {
                Main.recipe = originalRecipes; Recipe.numRecipes = originalCount;
                typeof(StorageSystem).GetProperty(nameof(StorageSystem.Recipes))!.SetValue(null,originalIndex);
                typeof(StorageSystem).GetProperty(nameof(StorageSystem.Planner))!.SetValue(null,originalPlanner);
                typeof(StorageSystem).GetProperty(nameof(StorageSystem.Progression))!.SetValue(null,originalProgression);
                typeof(StorageSystem).GetProperty(nameof(StorageSystem.Acquisition))!.SetValue(null,originalAcquisition);
                typeof(UISystem).GetProperty(nameof(UISystem.ActiveNetworkContents))!.SetValue(null,originalStock);
            }
            finally
            {
                try { recipeOwner.SetValue(null,originalOwner); }
                finally { recipeSetup.SetValue(null,originalSetup); }
            }
        }
    }
    private void ValidateCompactControls()
    {
        var restoreViewport=CaptureViewport();
        int width=Main.screenWidth,height=Main.screenHeight;
        float scale=Main.UIScaleWanted;
        var geometry=(WindowGeometry)typeof(SearchUIState).GetField("Geometry",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
        float x=geometry.X,y=geometry.Y,w=geometry.W,h=geometry.H;
        try
        {
            // Test control behavior in logical pixels with room for every drag. The
            // separate render sweep below still exercises each requested resolution.
            SetViewport(1920,1080,1f);
            ValidateCompactControlsInViewport();
        }
        finally
        {
            restoreViewport();
            geometry.X=x; geometry.Y=y; geometry.W=w; geometry.H=h;
        }
    }
    private void ValidateCompactControlsInViewport()
    {
        var search = NewSearch();
        // Saved layouts can have fractional origins or sit against the screen edge.
        // Neither should change this test's intended unconstrained drag distances.
        search.Layout.X=160f; search.Layout.Y=120f; search.Layout.W=900f; search.Layout.H=560f;
        search.OnActivate();
        var grid = Field<UIItemGrid>(search,"_grid");
        var window = (UIWindowPanel)Field<UIButton>(search,"_filterButton").Parent;
        var reset = WindowField<UIElement>(search,"_resetButton").GetDimensions();
        var star = Field<UIButton>(search,"_historyButton").GetDimensions();
        var mod = Field<UIButton>(search,"_modButton").GetDimensions();
        var sort = Field<UIButton>(search,"_sortButton").GetDimensions();
        var wrench = Field<UIIconButton>(search,"_wrench").GetDimensions();
        var scrollbar = Field<UIGridScrollBar>(search, "_scrollBar").GetDimensions();
        var resizeBounds = WindowField<UIResizeGrip>(search, "_grip").GetDimensions();
        Assert(scrollbar.Y + scrollbar.Height <= resizeBounds.Y - 6,
            "scrollbar ends above the lower resize grip with a separate pointer gap");
        Assert(star.Width == reset.Width && star.Height == reset.Height && star.Y + star.Height < mod.Y,"history matches reset bounds and clears mod selector");
        Assert(wrench.Width == sort.Width && wrench.Height == sort.Height && wrench.Y == sort.Y,"wrench matches sort bounds without inherited text minimum");
        var filter = Field<UIButton>(search,"_filterButton"); var help = Field<UIButton>(search,"_helpButton").GetDimensions();
        Assert(filter.GetDimensions().X + filter.GetDimensions().Width < help.X && help.X + help.Width < star.X,"filter help history toolbar order and separation");
        Assert(Field<UIElement>(search,"_npcTab").GetDimensions().Y + Field<UIElement>(search,"_npcTab").GetDimensions().Height < mod.Y,"navigation tabs do not overlap search row");
        Assert(Field<UIBrowserCategoryBar>(search,"_categories").GetDimensions().Height <= 76,"compact categories reserve item space");
        var owners = Field<string[]>(search,"_itemMods");
        var expected = Field<List<int>>(search,"_allItems").Select(UIItemNames.ModNameOf).Where(n=>n!=null && n!="ModLoader").ToHashSet(StringComparer.Ordinal);
        Assert(expected.SetEquals(owners) && owners.Distinct().Count()==owners.Length,"mod choices contain exactly mods contributing catalogue items");
        Mod.Logger.Info("AE2 ITEM MOD CHOICES: " + string.Join(", ", owners));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var prefixOwner = owners.FirstOrDefault(a=>owners.Any(b=>b!=a && b.StartsWith(a,StringComparison.Ordinal)));
        if (prefixOwner != null)
        {
            typeof(SearchUIState).GetField("_modFilter",flags)!.SetValue(search,prefixOwner);
            typeof(SearchUIState).GetMethod("Refresh",flags)!.Invoke(search,null);
            Assert(Field<List<int>>(search,"_filtered").All(t=>UIItemNames.ModNameOf(t)==prefixOwner),"dropdown mod selection uses exact ownership not name substring");
            typeof(SearchUIState).GetField("_modFilter",flags)!.SetValue(search,null);
            typeof(SearchUIState).GetMethod("Refresh",flags)!.Invoke(search,null);
        }
        int mx=Main.mouseX,my=Main.mouseY; bool left=Main.mouseLeft;
        float x=search.Layout.X,y=search.Layout.Y,w=search.Layout.W,h=search.Layout.H;
        var tick = new GameTime();
        void Mouse(Vector2 p, bool down) { Main.mouseX=(int)p.X; Main.mouseY=(int)p.Y; Main.mouseLeft=down; }
        try
        {
            Main.mouseLeft=false; grid.ScrollRows(3); float scroll=grid.ScrollRatio; int revision=grid.Revision;
            for (int cycle=0;cycle<3;cycle++)
            {
                Click(search,"_filterButton");
                var popup=ActiveChoice();
                Mouse(filter.GetDimensions().Center(),true); popup.Update(tick);
                Assert(UIChoiceMenu.IsOpen && ActiveChoice()==popup,"filter press preserves popup until release " + cycle);
                Main.mouseLeft=false; filter.LeftClick(new UIMouseEvent(filter,Main.MouseScreen));
                Assert(!UIChoiceMenu.IsOpen && grid.Revision==revision && grid.ScrollRatio==scroll,"filter release toggles without rebuilding or scrolling items " + cycle);
            }
            Click(search,"_filterButton"); var choice=ActiveChoice();
            var choicePoint=new Vector2(choice.GetDimensions().X+12,choice.GetDimensions().Y+4+30+12);
            choice.LeftClick(new UIMouseEvent(choice,choicePoint));
            Assert(!UIChoiceMenu.IsOpen && Field<List<int>>(search,"_filtered").All(t=>ContentSamples.ItemsByType[t].material),"filter selection applies material condition and closes menu");
            Click(search,"_filterButton"); choice=ActiveChoice();
            Mouse(new Vector2(0,0),true); choice.Update(tick);
            Assert(!UIChoiceMenu.IsOpen,"outside mouse press dismisses choice menu");
            Main.mouseLeft=false;
            foreach (string edge in new[] {"_frameTop","_frameLeft","_frameRight","_frameBottom"})
            {
                var grip=WindowField<UIFrameGrip>(search,edge); float oldX=search.Layout.X,oldY=search.Layout.Y;
                Assert(grip.Mode==UIFrameGrip.GripMode.Move && !grip.HighlightOnHover,"edge moves without highlight " + edge);
                var start=grip.GetDimensions().Center(); Mouse(start,true); grip.LeftMouseDown(new UIMouseEvent(grip,Main.MouseScreen));
                Main.mouseX+=6; Main.mouseY+=8; grip.Update(tick);
                Assert(search.Layout.W==w && search.Layout.H==h
                    && Math.Abs(search.Layout.X-(oldX+6))<.001f && Math.Abs(search.Layout.Y-(oldY+8))<.001f,
                    "dragging preserves size " + edge);
                Main.mouseLeft=false; grip.LeftMouseUp(new UIMouseEvent(grip,Main.MouseScreen));
            }
            // Exercise the actual saved-layout failure too: subtracting a wide
            // right-edge grab offset loses a float ulp, not a visible pixel.
            search.Layout.X=448.92f; search.Layout.Y=190.29f; search.OnActivate();
            var fractionalGrip=WindowField<UIFrameGrip>(search,"_frameRight");
            float fractionalX=search.Layout.X,fractionalY=search.Layout.Y;
            Mouse(fractionalGrip.GetDimensions().Center(),true);
            fractionalGrip.LeftMouseDown(new UIMouseEvent(fractionalGrip,Main.MouseScreen));
            Main.mouseX+=6; Main.mouseY+=8; fractionalGrip.Update(tick);
            Assert(search.Layout.W==w && search.Layout.H==h
                && Math.Abs(search.Layout.X-(fractionalX+6))<.001f && Math.Abs(search.Layout.Y-(fractionalY+8))<.001f,
                "fractional saved origin preserves intended drag within subpixel precision");
            Main.mouseLeft=false; fractionalGrip.LeftMouseUp(new UIMouseEvent(fractionalGrip,Main.MouseScreen));
            var resize=WindowField<UIResizeGrip>(search,"_grip"); var resizePoint=resize.GetDimensions().Center();
            Assert(WindowField<UIFrameGrip>(search,"_frameCorner").Parent==null && window.GetElementAt(resizePoint)==resize,"visible corner grip is sole resize hit target");
            Mouse(resizePoint,true); resize.LeftMouseDown(new UIMouseEvent(resize,Main.MouseScreen));
            Mouse(resizePoint + new Vector2(12,10),true); resize.Update(tick);
            Mod.Logger.Info($"AE2 CORNER DRAG: before={w},{h}; after={search.Layout.W},{search.Layout.H}");
            // Mouse coordinates are integral; a saved/scaled window origin may be fractional.
            Assert(Math.Abs(search.Layout.W-(w+12))<1 && Math.Abs(search.Layout.H-(h+10))<1,"corner drag changes width and height");
            Main.mouseLeft=false; resize.LeftMouseUp(new UIMouseEvent(resize,Main.MouseScreen));
            grid.SetItems(new List<int>()); var blank=grid.GetDimensions().Center(); float dragX=window.Left.Pixels,dragY=window.Top.Pixels;
            Mouse(blank,true); grid.LeftMouseDown(new UIMouseEvent(grid,Main.MouseScreen));
            Main.mouseX+=7; Main.mouseY+=9; window.Update(tick);
            Assert(window.Dragged && Math.Abs(window.Left.Pixels-(dragX+7))<.001f
                && Math.Abs(window.Top.Pixels-(dragY+9))<.001f,"empty catalogue area drags window");
            Main.mouseLeft=false; grid.LeftMouseUp(new UIMouseEvent(grid,Main.MouseScreen));
            grid.SetItems(new List<int>{ItemID.Wood}); var itemPoint=new Vector2(grid.GetDimensions().X+20,grid.GetDimensions().Y+20);
            dragX=window.Left.Pixels; dragY=window.Top.Pixels;
            Mouse(itemPoint,true); grid.LeftMouseDown(new UIMouseEvent(grid,Main.MouseScreen)); Main.mouseX+=4; window.Update(tick);
            Assert(!window.Dragged && window.Left.Pixels==dragX && window.Top.Pixels==dragY,"item mouse press never starts window dragging");
            Main.mouseLeft=false; grid.LeftMouseUp(new UIMouseEvent(grid,Main.MouseScreen));
        }
        finally
        {
            UIChoiceMenu.Close(); Main.mouseX=mx;Main.mouseY=my;Main.mouseLeft=left;
            search.Layout.X=x;search.Layout.Y=y;search.Layout.W=w;search.Layout.H=h;search.FitToScreen();
        }
    }
    private void ValidateCompactCategories(SearchUIState search)
    {
        var categories = Field<UIBrowserCategoryBar>(search, "_categories");
        var icons = Field<IList>(categories, "_main").Cast<UIElement>().ToArray();
        Assert(icons.Length == BrowserCategories.Items.Length - 1 && BrowserCategories.Items.All(category => category.Name != "套装"),
            "category toolbar merges armor and removes the redundant all icon");
        Assert(icons.All(icon => (int)icon.GetType().GetProperty("ItemType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(icon) > 0),
            "every item category has a real game item sprite");
        Assert(icons.All(icon => icon.GetDimensions().Width == 24 && icon.GetDimensions().Height == 24)
            && icons.All(icon => categories.GetDimensions().ToRectangle().Contains(icon.GetDimensions().ToRectangle())),
            "category icon rows wrap inside the compact toolbar without text minimums");
        icons[0].LeftClick(new UIMouseEvent(icons[0], icons[0].GetDimensions().Center()));
        search.Update(new GameTime());
        Assert(categories.Category == 1 && Field<List<int>>(search, "_filtered").All(categories.Matches),
            "icon category selection applies the existing property filter");
        var children = Field<IList>(categories, "_children").Cast<UIElement>().ToArray();
        Assert(children.Length > 1 && children.All(child => (int)child.GetType().GetProperty("ItemType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(child) > 0),
            "subcategories also use representative item sprites");
        Assert(children.Length == BrowserCategories.Items[1].Children.Length, "subcategory toolbar does not repeat its parent's all button");
        children[0].LeftClick(new UIMouseEvent(children[0], children[0].GetDimensions().Center()));
        Assert(categories.Child == 1, "subcategory icons remain independently selectable");
        icons[0].LeftClick(new UIMouseEvent(icons[0], icons[0].GetDimensions().Center()));
        search.Update(new GameTime());
        Assert(categories.Category == 1 && categories.Child == 0, "clicking the parent category restores all its children");
        Click(search, "_itemTab");
        Assert(categories.Category == 0 && categories.Child == 0, "clicking the mother page restores the unfiltered catalogue");
        icons = Field<IList>(categories, "_main").Cast<UIElement>().ToArray();
        int oldX = Main.mouseX, oldY = Main.mouseY; string oldName = Main.hoverItemName; Item oldHover = Main.HoverItem;
        try
        {
            search.ShowDetails(ItemID.Torch);
            for (int frame = 0; frame < 12; frame++) search.Update(new GameTime(TimeSpan.FromSeconds(frame / 60.0), TimeSpan.FromSeconds(1.0 / 60)));
            var point = icons[Math.Min(17, icons.Length - 1)].GetDimensions().Center();
            Assert(Field<UIBrowserDetails>(search, "_details").GetDimensions().ToRectangle().Contains(point.ToPoint()),
                "covered-category hover probe waits for the detail entrance and lies behind the panel");
            Main.mouseX = (int)point.X; Main.mouseY = (int)point.Y; Main.hoverItemName = ""; Main.HoverItem = new Item();
            RenderProbe(search, null);
            Assert(Main.hoverItemName != BrowserCategories.Items[Math.Min(17, icons.Length - 1) + 1].Name,
                "category icons behind a foreground recipe never supply its hover text");
            search.CloseDetails();
        }
        finally { Main.mouseX = oldX; Main.mouseY = oldY; Main.hoverItemName = oldName; Main.HoverItem = oldHover; }
    }
    private void ValidateLiveResize(MovableWindowState state)
    {
        var window = WindowField<UIWindowPanel>(state,"<Window>k__BackingField");
        var grip = WindowField<UIResizeGrip>(state,"_grip");
        var tick = new GameTime(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(.016));
        float x=state.Layout.X,y=state.Layout.Y,w=state.Layout.W,h=state.Layout.H;
        int mouseX=Main.mouseX,mouseY=Main.mouseY; bool down=Main.mouseLeft;
        try
        {
            Assert(state.Layout.DefaultW==475 && state.Layout.DefaultH==350 && state.Layout.Key.EndsWith(".compact1"),
                state.GetType().Name + " shares Recipe Browser defaults and resets old layout keys once");
            Vector2 start=grip.GetDimensions().Center();
            Main.mouseX=(int)start.X;Main.mouseY=(int)start.Y;Main.mouseLeft=true;
            grip.LeftMouseDown(new UIMouseEvent(grip,start));
            state.Update(tick);
            Assert(Math.Abs(state.Layout.W-w)<1 && Math.Abs(state.Layout.H-h)<1,
                state.GetType().Name + " does not jump when grabbing the resize handle");
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            for(int frame=0;frame<16;frame++)
            {
                Main.mouseX=(int)start.X+(frame%2==0 ? 24 : 8);
                Main.mouseY=(int)start.Y+(frame%2==0 ? 18 : 6);
                // Updating only the grip missed the original Window.Children mutation.
                state.Update(tick);
            }
            elapsed.Stop();
            Assert(state.Layout.W>w && state.Layout.H>h && window.Children.Contains(grip),
                state.GetType().Name + " resizes through complete child enumeration without collection errors");
            Mod.Logger.Info("AE2 WINDOW RESIZE: "+state.GetType().Name+"; updates=16; totalMs="+elapsed.Elapsed.TotalMilliseconds.ToString("F3"));
        }
        finally
        {
            grip.LeftMouseUp(new UIMouseEvent(grip,Main.MouseScreen));
            Main.mouseLeft=false;state.Update(tick);
            state.Layout.X=x;state.Layout.Y=y;state.Layout.W=w;state.Layout.H=h;
            state.FitToScreen();Invoke(state,"ApplyLayout");
            Main.mouseX=mouseX;Main.mouseY=mouseY;Main.mouseLeft=down;
        }
    }

    private void RenderChecks()
    {
        if (_done || Main.dedServ || !Main.gameMenu || !StorageSystem.Ready
            || Environment.GetEnvironmentVariable("AE2_UI_REGRESSION")!="1") return;
        _done=true;
        using var browserPreferences = BrowserPreferences.Isolate(Path.Combine(Main.SavePath, "AE2-Validation", "browser-" + Guid.NewGuid().ToString("N")));
        var restoreViewport=CaptureViewport();
        int width=Main.screenWidth,height=Main.screenHeight; float scale=Main.UIScaleWanted;
        var device=Main.instance.GraphicsDevice;
        var oldTargets=device.GetRenderTargets();
        var restoreLayout=CaptureLayoutState();
        string layoutFile = Path.Combine(Main.SavePath, "AE2ForTerraria-Layout.txt");
        byte[] layoutBefore = File.Exists(layoutFile) ? File.ReadAllBytes(layoutFile) : null;
        var bindings=new[] {nameof(UISystem.ActiveTerminalPos),nameof(UISystem.ActiveNetwork),nameof(UISystem.ActiveNetworkContents),
            nameof(UISystem.ActiveAssembler),nameof(UISystem.ActiveCraftingCpu),nameof(UISystem.ActiveCraftingTerminal),nameof(UISystem.ActiveTerminalViewMode)}
            .Select(name=>typeof(UISystem).GetProperty(name)!).Select(property=>(Property:property,Value:property.GetValue(null))).ToArray();
        int playerIndex=Main.myPlayer;
        var previousPlayer=Main.player[playerIndex];
        var previousEntities=TileEntity.ByID;var previousPositions=TileEntity.ByPosition;var previousChests=Main.chest;
        RegressionWorld world=null;
        try
        {
            string dir=Path.Combine(Main.SavePath,"AE2-Validation"); Directory.CreateDirectory(dir);
            world=new RegressionWorld();
            Main.player[playerIndex]=new Player {whoAmI=playerIndex,active=true,name="界面回归玩家",position=new Vector2(480,480)};
            Main.LocalPlayer.inventory[0] = new Item(ModContent.ItemType<MEDisk1K>());
            Main.LocalPlayer.inventory[1] = new Item(ModContent.ItemType<MEDisk4K>());
            Main.LocalPlayer.inventory[2] = new Item(ItemID.Wood, 75);
            Main.LocalPlayer.inventory[49] = new Item(ItemID.WorkBench);
            foreach(int type in new[] {ItemID.Wood,ItemID.Gel,ItemID.Torch}) { Main.instance.LoadItem(type); Terraria.GameContent.TextureAssets.Item[type].Wait?.Invoke(); }
            var craftingTerminal=world.Place(new MECraftingTerminalEntity(),ModContent.TileType<MECraftingTerminalTile>(),30,30);
            var drive=world.Place(new MEDiskDriveEntity(),ModContent.TileType<MEDiskDriveTile>(),31,30);
            drive.Disks[0]=new Item(ModContent.ItemType<MEDisk1K>()); drive.RefreshCapacity();
            drive.Ledger.Insert(ItemKey.Crafted(ItemID.Wood),321);
            drive.Ledger.Insert(ItemKey.Crafted(ItemID.Gel),37);
            var sword=new Item(ItemID.CopperShortsword); sword.Prefix(PrefixID.Legendary);
            drive.Ledger.Insert(ItemKey.FromItem(sword),2);
            var assembler=world.Place(new MEMolecularAssemblerEntity(),ModContent.TileType<MEMolecularAssemblerTile>(),32,30);
            assembler.Stations[0]=new Item(ItemID.WorkBench);
            var cpu=world.Place(new MECraftingCpuEntity(),ModContent.TileType<MECraftingCpuTile>(),33,30);
            cpu.Enqueue(ItemKey.Crafted(ItemID.Torch),25,"验证玩家");
            for(int i=0;i<12;i++) cpu.Keep.Add(new MECraftingCpuEntity.KeepEntry {Key=ItemKey.Crafted(ItemID.Wood+i),Target=100+i});
            craftingTerminal.Enqueue(ItemKey.Crafted(ItemID.IronBar),48,"界面回归玩家");craftingTerminal.Jobs[0].Paused=true;
            craftingTerminal.Keep.Add(new MECraftingCpuEntity.KeepEntry {Key=ItemKey.Crafted(ItemID.Torch),Target=100});
            craftingTerminal.Stations[0]=new Item(ItemID.Furnace);craftingTerminal.EnsureStationSlots();
            var unit=new MEStorageUnitEntity(); unit.Ledger.Insert(ItemKey.Crafted(ItemID.Wood),80);
            typeof(UISystem).GetProperty(nameof(UISystem.ActiveTerminalPos))!.SetValue(null,craftingTerminal.Position);
            NetworkRegistry.TryClaim(MENetwork.CanonicalAnchor(MENetwork.Collect(30,30)),Main.LocalPlayer.name);
            UISystem.SetActiveNetwork(new List<IMEStorage> {drive},assembler,craftingTerminal);
            Assert(UISystem.GetCraftingHosts().Count==2 && UISystem.GetCraftingHosts().Sum(host=>host.Jobs.Count)==2,
                "integrated task fixture resolves terminal and legacy CPU jobs through actual network membership");
            Assert(UISystem.GetStationHosts().Count==2 && UISystem.GetStationHosts().Sum(host=>host.Stations.Count(item=>item!=null && !item.IsAir))==2
                && ReferenceEquals(UISystem.ActiveCraftingTerminal,craftingTerminal) && UISystem.TerminalCanManage,
                "integrated station fixture resolves installed stations and editable terminal through actual network membership");
            ValidateBrowserPreferences();
            ValidateAltFavorites();
            ValidateSelectedRecipeFavorite();
            ValidatePinnedTreeHud();
            BrowserRefinementRegression.Snapshots(Assert);
            BrowserRefinementRegression.Armor(Assert);
            ValidatePartialGrid();
            ValidateUnifiedBackNavigation();
            ValidateBrowser();
            foreach (var size in new[] { new Point(800,600), new Point(1280,720), new Point(1920,1080) })
            {
                SetViewport(size.X,size.Y,size.X==1920 ? 1.25f : 1f);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var terminal=NewTerminal(TerminalViewMode.Craft);
                typeof(TerminalUIState).GetField("_orderQuantity",flags).SetValue(terminal,25L);
                Field<Terraria.GameContent.UI.Elements.UITextPanel<string>>(terminal,"_quantityButton").SetText("数量 ×25");
                typeof(TerminalUIState).GetMethod("Select",flags).Invoke(terminal,new object[] {ItemKey.Crafted(ItemID.Torch)});
                var driveUi=new DriveUIState(); driveUi.Activate(); driveUi.SetDrive(drive);
                var assemblerUi=new AssemblerUIState(); assemblerUi.Activate(); assemblerUi.SetAssembler(assembler);
                var cpuUi=new CraftingCpuUIState(); cpuUi.Activate(); cpuUi.SetCpu(cpu);
                var unitUi=new StorageUnitUIState(); unitUi.Activate(); unitUi.SetUnit(unit);
                var partitionUi=new DriveUIState(); partitionUi.Activate(); partitionUi.SetDrive(drive);
                Click(partitionUi,"_tabButton");
                var keepUi=new CraftingCpuUIState(); keepUi.Activate(); keepUi.SetCpu(cpu); Click(keepUi,"_viewButton"); Click(keepUi,"_nextButton");
                var search=new SearchUIState(); search.Activate();
                typeof(SearchUIState).GetMethod("BuildLists",flags).Invoke(search,null);
                typeof(SearchUIState).GetMethod("Refresh",flags).Invoke(search,null);
                var closedTerminal = NewTerminal(TerminalViewMode.Craft); closedTerminal.CloseDetails();
                var terminalTasks = NewTerminal(TerminalViewMode.Craft); Click(terminalTasks,"_tasksButton");
                var terminalStations = NewTerminal(TerminalViewMode.Craft);
                var terminalKeep = NewTerminal(TerminalViewMode.Craft); Click(terminalKeep,"_tasksButton");
                var accessTerminal = NewTerminal(TerminalViewMode.Access);
                typeof(TerminalUIState).GetMethod("Select",flags)!.Invoke(accessTerminal,new object[] {ItemKey.Crafted(ItemID.Wood)});
                var keepPanel=Field<UIIntegratedCraftingPanel>(terminalKeep,"_management");
                Field<UIButton>(keepPanel,"_keep").LeftClick(new UIMouseEvent(keepPanel,Vector2.Zero));
                var detailSearch = NewSearch(); detailSearch.ShowDetails(ItemID.TerraBlade);
                var longSearch=NewSearch();
                int longIndex=Enumerable.Range(0,Recipe.numRecipes).Where(i=>!Main.recipe[i].Disabled && Main.recipe[i].createItem.type>0).OrderByDescending(i=>Main.recipe[i].requiredItem.Count(item=>!item.IsAir)).First();
                longSearch.ShowDetails(Main.recipe[longIndex].createItem.type);
                var longDetails=Field<UIBrowserDetails>(longSearch,"_details");
                SetField(longDetails,"_recipe",StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(Main.recipe[longIndex].createItem.type)).ToList().IndexOf(longIndex));Invoke(longDetails,"Rebuild");
                var treeSearch = NewSearch(); treeSearch.ShowDetails(ItemID.TerraBlade); Field<UIBrowserDetails>(treeSearch,"_details").SetTab(3);
                var npcSearch = NewSearch(); Click(npcSearch,"_npcTab");
                var npcDetail = NewSearch(); Click(npcDetail,"_npcTab"); npcDetail.ShowNpcDetails(NPCID.Guide);
                var filterSearch=NewSearch(); var modSearch=NewSearch(); var minimumSearch=NewSearch();
                var auxiliary = new MovableWindowState[] { new BridgeUIState(), new StorageBusUIState(), new DropCollectorUIState(),
                    new NetworkOverviewUIState(), new HelpUIState(), new RecipeOverlayUIState(), new NpcOverlayUIState() };
                foreach (var page in auxiliary) page.Activate();
                foreach (var state in new MovableWindowState[] {terminal,driveUi,assemblerUi,cpuUi,search,unitUi,partitionUi,keepUi,closedTerminal,detailSearch,treeSearch,npcSearch,npcDetail,filterSearch,modSearch,minimumSearch,terminalTasks,terminalStations,terminalKeep,longSearch,accessTerminal}.Concat(auxiliary))
                {
                    UIChoiceMenu.Close();
                    state.Layout.X=state.Layout.Y=-1; state.Layout.W=state.Layout.DefaultW; state.Layout.H=state.Layout.DefaultH;
                    if (ReferenceEquals(state,minimumSearch)) { state.Layout.W=state.Layout.MinW;state.Layout.H=state.Layout.MinH; }
                    state.FitToScreen(); state.Recalculate();
                    for (int frame=0;frame<12;frame++) state.Update(new GameTime(TimeSpan.FromSeconds(1+frame/60.0),TimeSpan.FromSeconds(1.0/60)));
                    ValidateLiveResize(state);
                    if (ReferenceEquals(state,search)) ValidateCompactCategories(search);
                    if (ReferenceEquals(state,filterSearch)) Click(filterSearch,"_filterButton");
                    if (ReferenceEquals(state,modSearch)) Click(modSearch,"_modButton");
                    string label=state.GetType().Name+(ReferenceEquals(state,accessTerminal)?"-access":ReferenceEquals(state,closedTerminal)?"-catalogue":ReferenceEquals(state,detailSearch)?"-details":ReferenceEquals(state,treeSearch)?"-tree":ReferenceEquals(state,npcSearch)?"-npcs":ReferenceEquals(state,npcDetail)?"-npc-detail":ReferenceEquals(state,filterSearch)?"-filter":ReferenceEquals(state,modSearch)?"-mods":ReferenceEquals(state,minimumSearch)?"-minimum":ReferenceEquals(state,terminalTasks)?"-tasks":ReferenceEquals(state,terminalStations)?"-stations":ReferenceEquals(state,terminalKeep)?"-keep":ReferenceEquals(state,longSearch)?"-long-materials":"")+(ReferenceEquals(state,partitionUi)?"-partition":ReferenceEquals(state,keepUi)?"-keep-page2":"");
                    using var target=new RenderTarget2D(device,size.X,size.Y);
                    device.SetRenderTarget(target); device.Clear(new Color(12,16,24));
                    using var batch=new SpriteBatch(device);
                    batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.LinearClamp,
                        DepthStencilState.None,RasterizerState.CullNone,null,Main.UIScaleMatrix);
                    state.Draw(batch); batch.End();
                    if (ReferenceEquals(state,terminal))
                    {
                        var info=Field<UIBrowserDetails>(terminal,"_info").GetDimensions();
                        Assert(info.Width>terminal.Layout.W*.9f,"terminal recipe reader fills width at "+size.X);
                        var actionNames=new[]{"_quantityButton","_quantityInput","_orderButton","_keepButton"};
                        var actionRects=actionNames.Select(name=>Field<UIElement>(terminal,name).GetDimensions().ToRectangle()).ToArray();
                        Assert(actionRects.All(rect=>info.ToRectangle().Contains(rect)) && actionRects.SelectMany((a,i)=>actionRects.Skip(i+1).Select(b=>!a.Intersects(b))).All(ok=>ok),"terminal action row stays within reader without overlaps at "+size.X);
                    }
                    Assert(new[]{"_frameTop","_frameLeft","_frameRight","_frameBottom","_frameCorner"}
                        .All(name=>!WindowField<UIFrameGrip>(state,name).HighlightOnHover),
                        label+" keeps window edges quiet at "+size.X);
                    if (ReferenceEquals(state,closedTerminal) || ReferenceEquals(state,terminalStations))
                    {
                        var gridBounds=Field<UIItemGrid>((TerminalUIState)state,"_grid").GetDimensions().ToRectangle();
                        var stations=Field<UIStationGrid>((TerminalUIState)state,"_stationGrid");
                        var stationBounds=stations.GetDimensions().ToRectangle();
                        var filter=Field<UIButton>((TerminalUIState)state,"_craftableButton");
                        Assert(Math.Abs(filter.GetDimensions().X-gridBounds.X)<1 && Field<UIElement>((TerminalUIState)state,"_status").Parent==null
                            && Field<UIElement>((TerminalUIState)state,"_selectionLabel").Parent==null
                            && !stations.Children.OfType<UIText>().Any(),"craft catalogue left-aligns the filter and has no count or station captions at "+size.X);
                        Assert(gridBounds.Width>=state.Layout.W-48 && stationBounds.Top-gridBounds.Bottom<=8
                            && !gridBounds.Intersects(stationBounds),"craft catalogue fills available width and reaches the station slots at "+size.X);
                    }
                    if (ReferenceEquals(state,driveUi))
                    {
                        var contents=Field<UIStorageContents>(state,"_contents").GetDimensions().ToRectangle();
                        var window=WindowField<UIWindowPanel>(state,"<Window>k__BackingField").GetDimensions().ToRectangle();
                        var toggle=Field<UIElement>(state,"_tabButton").GetDimensions().ToRectangle();
                        Assert(!WindowField<UIWindowPanel>(state,"<Window>k__BackingField").Children.OfType<UIPlayerInventoryPanel>().Any()
                            && contents.Bottom<=toggle.Top && toggle.Bottom<=window.Bottom && window.Bottom-contents.Bottom<70,
                            "drive removes its duplicate backpack and expands contents to its bottom controls at "+size.X);
                    }
                    if (ReferenceEquals(state,assemblerUi))
                    {
                        var window = WindowField<UIWindowPanel>(state,"<Window>k__BackingField").GetDimensions().ToRectangle();
                        Assert(window.Contains(Field<UIElement>(state,"_progressText").GetDimensions().ToRectangle()),
                            "legacy assembler progress text fits its compact window at " + size.X);
                        Assert(!WindowField<UIWindowPanel>(state,"<Window>k__BackingField").Children.OfType<UIPlayerInventoryPanel>().Any(),
                            label + " uses the native backpack and reserves its compact body for stations at " + size.X);
                    }
                    if (ReferenceEquals(state,terminalTasks) || ReferenceEquals(state,terminalKeep))
                    {
                        var management=Field<UIIntegratedCraftingPanel>(state,"_management");
                        int expected=ReferenceEquals(state,terminalTasks) ? 2 : ReferenceEquals(state,terminalKeep) ? 13 : 3;
                        Assert(Field<UIList>(management,"_list").Count==expected,
                            label+" renders actual network rows rather than an empty-state placeholder at "+size.X);
                    }
                    device.SetRenderTargets(oldTargets);
                    using var file=File.Create(Path.Combine(dir,label+"-"+size.X+"x"+size.Y+".png"));
                    target.SaveAsPng(file,size.X,size.Y);
                    Mod.Logger.Info("AE2 UI RENDERED: "+label+" "+size.X+"x"+size.Y+" scale="+Main.UIScale);
                }
            }
            ValidateActualAeLayerHover(drive, dir);
            Assert(layoutBefore == null ? !File.Exists(layoutFile) : File.Exists(layoutFile) && layoutBefore.SequenceEqual(File.ReadAllBytes(layoutFile)),
                "menu UI regression does not persist temporary window geometry into personal layout settings");
            Mod.Logger.Info("AE2 UI REGRESSION COMPLETE: rendered in menu without loading a world/player. " + dir);
        }
        catch (Exception ex) { Mod.Logger.Error("AE2 UI REGRESSION FAILED",ex); }
        finally
        {
            UIChoiceMenu.Close();
            device.SetRenderTargets(oldTargets); restoreViewport();
            world?.Dispose();
            Main.myPlayer=playerIndex;Main.player[playerIndex]=previousPlayer;
            foreach(var binding in bindings) binding.Property.SetValue(null,binding.Value);
            for(int index=restoreLayout.Count-1;index>=0;index--) restoreLayout[index]();
            if(ReferenceEquals(TileEntity.ByID,previousEntities) && ReferenceEquals(TileEntity.ByPosition,previousPositions)
                && ReferenceEquals(Main.chest,previousChests) && ReferenceEquals(Main.player[playerIndex],previousPlayer)
                && bindings.All(binding=>Equals(binding.Property.GetValue(null),binding.Value)))
                Mod.Logger.Info("AE2 UI FIXTURE RESTORED: original tile entity/chest/player references and active UI network bindings");
            else Mod.Logger.Error("AE2 UI REGRESSION FAILED: temporary network fixture state was not completely restored");
        }
    }

    private void ValidateActualAeLayerHover(MEDiskDriveEntity drive, string directory)
    {
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
        var restore = new List<Action>();
        System.Reflection.FieldInfo SaveField(Type type, string name, object instance = null)
        {
            var field = type.GetField(name, fields) ?? throw new MissingFieldException(type.FullName,name);
            object value = field.GetValue(instance); restore.Add(() => field.SetValue(instance,value)); return field;
        }
        foreach (var property in typeof(UISystem).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            if (property.SetMethod != null && (typeof(UIState).IsAssignableFrom(property.PropertyType)
                || property.PropertyType == typeof(UserInterface)))
            { object value = property.GetValue(null); restore.Add(() => property.SetValue(null,value)); }
        foreach (var field in typeof(AEUiSession).GetFields(fields).Where(field=>field.IsStatic && !field.IsInitOnly && !field.IsLiteral))
        { object value = field.GetValue(null); restore.Add(() => field.SetValue(null,value)); }
        var statesReady = SaveField(typeof(UISystem),"_statesReady");
        SaveField(typeof(UserInterface),"ActiveInstance");
        foreach (string name in new[] { "gameMenu", "playerInventory", "blockInput", "screenWidth", "screenHeight", "lastMouseX", "lastMouseY",
            "mouseItem", "HoverItem", "hoverItemName", "mouseText", "mouseTextColor", "mouseTextColorChange", "spriteBatch" }) SaveField(typeof(Main),name);
        var mouseCache = SaveField(typeof(Main),"_mouseTextCache",Main.instance);
        var currentInputProfile = SaveField(typeof(Terraria.GameInput.PlayerInput),"_currentProfile");
        SaveField(typeof(Terraria.GameInput.PlayerInput),"Triggers");
        var zoomFields = new Dictionary<string,System.Reflection.FieldInfo>();
        foreach (string name in new[] { "_originalMouseX", "_originalMouseY", "_originalLastMouseX", "_originalLastMouseY", "_originalScreenWidth", "_originalScreenHeight" })
            zoomFields[name] = SaveField(typeof(Terraria.GameInput.PlayerInput),name);
        float originalScale = Main.UIScaleWanted; restore.Add(()=>Main.UIScale=originalScale);
        bool mouseInterface = Main.LocalPlayer.mouseInterface, cursorIcon = Main.LocalPlayer.cursorItemIconEnabled;
        restore.Add(()=> { Main.LocalPlayer.mouseInterface=mouseInterface; Main.LocalPlayer.cursorItemIconEnabled=cursorIcon; });
        var device = Main.instance.GraphicsDevice; var targets = device.GetRenderTargets();
        var viewport = device.Viewport; var scissor = device.ScissorRectangle;
        var blend = device.BlendState; var depth = device.DepthStencilState; var rasterizer = device.RasterizerState; var sampler = device.SamplerStates[0];
        using var preserveMouse = AEUiSession.SuppressMouse();
        using var batch = new SpriteBatch(device);
        using var target = new RenderTarget2D(device,1280,720);
        try
        {
            // A menu-only fixture has not necessarily run the in-game input update
            // that rebuilds profiles after mods register their keybinds. Use the
            // game's normal profile initializer on a private copy, keeping users'
            // assignments and restoring the original profile reference afterwards.
            var inputProfile = Terraria.GameInput.PlayerInput.CurrentProfile;
            var bindings = (IEnumerable<KeyValuePair<string,ModKeybind>>)typeof(KeybindLoader)
                .GetField("modKeybinds",fields)!.GetValue(null);
            string[] modKeys = bindings.Select(binding=>binding.Key).ToArray();
            int missingKeys = inputProfile.InputModes.Values.Sum(mode=>modKeys.Count(key=>!mode.KeyStatus.ContainsKey(key)));
            var readyProfile = new Terraria.GameInput.PlayerInputProfile(inputProfile.Name);
            readyProfile.Initialize(Terraria.GameInput.PresetProfiles.Redigit);
            foreach (var field in typeof(Terraria.GameInput.PlayerInputProfile).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                if (!field.IsInitOnly && field.Name != nameof(Terraria.GameInput.PlayerInputProfile.InputModes)) field.SetValue(readyProfile,field.GetValue(inputProfile));
            foreach (var mode in inputProfile.InputModes)
                foreach (var key in mode.Value.KeyStatus)
                    if (readyProfile.InputModes[mode.Key].KeyStatus.ContainsKey(key.Key))
                        readyProfile.InputModes[mode.Key].KeyStatus[key.Key] = new List<string>(key.Value);
            currentInputProfile.SetValue(null,readyProfile);
            var readyTriggers = new Terraria.GameInput.TriggersPack(); readyTriggers.Initialize();
            Terraria.GameInput.PlayerInput.Triggers = readyTriggers;
            Assert(readyProfile.InputModes.Values.All(mode=>modKeys.All(mode.KeyStatus.ContainsKey)),
                "native tooltip fixture has initialized bindings for every registered mod in all input modes");
            Mod.Logger.Info("AE2 TOOLTIP INPUT FIXTURE: original menu profile missing registered bindings=" + missingKeys
                + "; private initialized profile supplied; original profile and assignments preserved.");
            SetViewport(1280,720,1f);
            var state = new DriveUIState(); state.Activate(); state.SetDrive(drive);
            state.Layout.X=120; state.Layout.Y=50; state.Layout.W=800; state.Layout.H=600; state.OnActivate();
            state.Update(new GameTime());
            var ui = new UserInterface(); ui.SetState(state);
            typeof(UISystem).GetProperty(nameof(UISystem.Interface))!.SetValue(null,new UserInterface());
            typeof(UISystem).GetProperty(nameof(UISystem.SecondaryInterface))!.SetValue(null,ui);
            typeof(UISystem).GetProperty(nameof(UISystem.OverlayInterface))!.SetValue(null,new UserInterface());
            typeof(UISystem).GetProperty(nameof(UISystem.DriveState))!.SetValue(null,state);
            statesReady.SetValue(null,true); // Already initialized the only drawn fixture; leave other saved UI instances alone.
            var slot = Field<List<UIItemSlotElement>>(state,"_slots")[0];
            var point = slot.GetDimensions().Center();
            foreach (string name in new[] { "_originalMouseX", "_originalLastMouseX" }) zoomFields[name].SetValue(null,(int)point.X);
            foreach (string name in new[] { "_originalMouseY", "_originalLastMouseY" }) zoomFields[name].SetValue(null,(int)point.Y);
            zoomFields["_originalScreenWidth"].SetValue(null,1280); zoomFields["_originalScreenHeight"].SetValue(null,720);
            Main.mouseX=(int)point.X; Main.mouseY=(int)point.Y;
            Terraria.GameInput.PlayerInput.MouseInfo=new Microsoft.Xna.Framework.Input.MouseState((int)point.X,(int)point.Y,0,
                Microsoft.Xna.Framework.Input.ButtonState.Released,Microsoft.Xna.Framework.Input.ButtonState.Released,Microsoft.Xna.Framework.Input.ButtonState.Released,
                Microsoft.Xna.Framework.Input.ButtonState.Released,Microsoft.Xna.Framework.Input.ButtonState.Released);
            Main.mouseItem=new Item(); Main.mouseText=false; Main.HoverItem=new Item(); Main.hoverItemName=string.Empty;
            mouseCache.SetValue(Main.instance,Activator.CreateInstance(mouseCache.FieldType));
            Main.gameMenu=false; AEUiSession.Synchronize(); Main.spriteBatch=batch;
            device.SetRenderTarget(target); device.Clear(new Color(12,16,24));
            var layers = new List<GameInterfaceLayer> { new LegacyGameInterfaceLayer("Vanilla: Mouse Text",()=>true,InterfaceScaleType.UI) };
            ModContent.GetInstance<UISystem>().ModifyInterfaceLayers(layers);
            var layer = layers.Single(entry=>entry.Name==AEUiSession.LayerName);
            // GameInterfaceLayer.Draw performs its own Begin/End and zoom setup.
            Assert(layer.Draw() && Main.HoverItem.type==drive.Disks[0].type && !string.IsNullOrEmpty(Main.hoverItemName),
                "actual AE interface layer draws the drive and native disk tooltip within its real SpriteBatch lifecycle");
            device.SetRenderTargets(targets);
            using var file = File.Create(Path.Combine(directory,"AE-interface-layer-disk-tooltip-1280x720.png"));
            target.SaveAsPng(file,1280,720);
            Mod.Logger.Info("AE2 UI RENDERED: actual AE interface layer with disk hover 1280x720");
            var armorSearch = NewSearch();
            armorSearch.Layout.X = 40; armorSearch.Layout.Y = 40; armorSearch.Layout.W = 750; armorSearch.Layout.H = 480; armorSearch.OnActivate();
            var categories = Field<UIBrowserCategoryBar>(armorSearch, "_categories");
            categories.GetType().GetMethod("SelectCategory", fields).Invoke(categories,
                new object[] { Array.FindIndex(BrowserCategories.Items, category => category.Name == "盔甲") });
            var armorGrid = Field<UIItemGrid>(armorSearch, "_grid");
            int armorIndex = Field<List<int>>(armorGrid, "_items").IndexOf(ItemID.WoodHelmet);
            Assert(armorIndex >= 0, "merged armor category retains native set components");
            armorGrid.ScrollRows(armorIndex / armorGrid.Columns);
            int row = armorIndex / armorGrid.Columns - Field<int>(armorGrid, "_rowOffset");
            var gridBounds = armorGrid.GetDimensions();
            point = new Vector2(gridBounds.X + armorIndex % armorGrid.Columns * armorGrid.CellSize + armorGrid.CellSize / 2,
                gridBounds.Y + row * armorGrid.CellSize + armorGrid.CellSize / 2);
            // Discard the private interface without deactivating its test drive and saving its geometry.
            typeof(UISystem).GetProperty(nameof(UISystem.SecondaryInterface))!.SetValue(null, new UserInterface());
            UISystem.Interface.SetState(armorSearch);
            foreach (string name in new[] { "_originalMouseX", "_originalLastMouseX" }) zoomFields[name].SetValue(null, (int)point.X);
            foreach (string name in new[] { "_originalMouseY", "_originalLastMouseY" }) zoomFields[name].SetValue(null, (int)point.Y);
            Main.mouseX = (int)point.X; Main.mouseY = (int)point.Y;
            Terraria.GameInput.PlayerInput.MouseInfo = new Microsoft.Xna.Framework.Input.MouseState((int)point.X, (int)point.Y, 0,
                Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released,
                Microsoft.Xna.Framework.Input.ButtonState.Released, Microsoft.Xna.Framework.Input.ButtonState.Released);
            armorSearch.Update(new GameTime()); ArmorSetIndex.Request(ItemID.WoodHelmet, out _);
            for (int tick = 0; tick < 100 && ArmorSetIndex.Request(ItemID.WoodHelmet, out _).Count == 0; tick++) ArmorSetIndex.Advance();
            Main.mouseText = false; Main.HoverItem = new Item(); Main.hoverItemName = "";
            mouseCache.SetValue(Main.instance, Activator.CreateInstance(mouseCache.FieldType));
            device.SetRenderTarget(target); device.Clear(new Color(12, 16, 24));
            Assert(layer.Draw() && Main.HoverItem.type == ItemID.WoodHelmet, "actual AE armor hover renders its native tooltip with queried set effects");
            device.SetRenderTargets(targets);
            using var armorFile = File.Create(Path.Combine(directory, "AE-interface-layer-armor-tooltip-1280x720.png"));
            target.SaveAsPng(armorFile, 1280, 720); Mod.Logger.Info("AE2 UI RENDERED: actual AE armor category and set tooltip 1280x720");
        }
        finally
        {
            for (int i=restore.Count-1;i>=0;i--) restore[i]();
            device.SetRenderTargets(targets); device.Viewport=viewport; device.ScissorRectangle=scissor;
            device.BlendState=blend; device.DepthStencilState=depth; device.RasterizerState=rasterizer; device.SamplerStates[0]=sampler;
        }
    }

    private static List<Action> CaptureLayoutState()
    {
        var restore=new List<Action>();
        const System.Reflection.BindingFlags statics=System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        // WindowLayout.Store replaces entries; retaining the original entry references
        // therefore isolates its in-memory cache without changing Save's implementation.
        foreach(var field in typeof(WindowLayout).GetFields(statics))
        {
            if(field.IsLiteral) continue;
            object original=field.GetValue(null);
            if(original is IDictionary dictionary)
            {
                var entries=new List<DictionaryEntry>();
                foreach(DictionaryEntry entry in dictionary) entries.Add(entry);
                restore.Add(()=> { dictionary.Clear(); foreach(var entry in entries) dictionary.Add(entry.Key,entry.Value); });
            }
            else if(!field.IsInitOnly) restore.Add(()=>field.SetValue(null,original));
        }
        foreach(Type type in typeof(UISystem).Assembly.GetTypes())
            foreach(var field in type.GetFields(statics))
                if(field.FieldType==typeof(WindowGeometry) && field.GetValue(null) is WindowGeometry geometry)
                    foreach(var valueField in typeof(WindowGeometry).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    {
                        object value=valueField.GetValue(geometry);
                        restore.Add(()=>valueField.SetValue(geometry,value));
                    }
        return restore;
    }
}

