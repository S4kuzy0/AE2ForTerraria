using System;
using System.IO;
using AE2ForTerraria.UI;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Opt-in core regression on the menu draw thread, before UI/live fixtures start.</summary>
public sealed class ClientCoreRegressionSystem : ModSystem
{
    private static readonly FieldInfo LoadingField = typeof(ModLoader).GetField("isLoading",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo RecipeSetupField = typeof(RecipeLoader).GetField("setupRecipes",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly PropertyInfo RecipeOwnerProperty = typeof(RecipeLoader).GetProperty("CurrentMod",
        BindingFlags.Static | BindingFlags.NonPublic);
    private bool _done;
    private int _menuFrames;

    public override bool IsLoadingEnabled(Mod mod)
        => !Main.dedServ && Environment.GetEnvironmentVariable("AE2_RUN_REGRESSION") == "1";

    public override void Load()
    {
        _done = false;
        _menuFrames = 0;
        On_Main.Draw += RunAfterMenuDraw;
    }

    public override void Unload() => On_Main.Draw -= RunAfterMenuDraw;

    private void RunAfterMenuDraw(On_Main.orig_Draw orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        if (_done || !Main.gameMenu || Main.menuMode != 0 || !StorageSystem.Ready
            || LoadingField == null || (bool)LoadingField.GetValue(null)) return;
        if (++_menuFrames < 10) return;
        _done = true;
        using var preferences = BrowserPreferences.Isolate(Path.Combine(Main.SavePath, "AE2-Validation", "core-browser-" + Guid.NewGuid().ToString("N")));

        // This is synchronous after the current draw returns: no main-thread drawing
        // can observe the temporary player, network or tile state borrowed by a fixture.
        Recipe[] recipes = Main.recipe;
        int recipeCount = Recipe.numRecipes;
        Mod.Logger.Info("AE2 CLIENT CORE REGRESSION BEGIN: loaded main menu, synchronous Draw thread; no world/player opened.");
        try
        {
            try { RunWithTemporaryRecipeContext(); }
            finally
            {
                Main.recipe = recipes;
                Recipe.numRecipes = recipeCount;
                // Unlike server PostAddRecipes, no later PostSetupRecipes rebuild follows this pass.
                StorageSystem.Recipes.Build();
            }
            Mod.Logger.Info("AE2 CLIENT CORE REGRESSION RETURNED: recipe creation context, loaded recipe array and final index restored.");
        }
        catch (Exception ex)
        {
            Mod.Logger.Error("AE2 CLIENT CORE REGRESSION FAILED during menu execution or final index restoration", ex);
        }
    }

    private void RunWithTemporaryRecipeContext()
    {
        if (RecipeSetupField == null || RecipeOwnerProperty?.SetMethod == null)
            throw new InvalidOperationException("Cannot locate the tModLoader temporary recipe creation context.");
        object setup = RecipeSetupField.GetValue(null);
        object owner = RecipeOwnerProperty.GetValue(null);
        try
        {
            // Recipe.Create checks exactly these two values in the installed tML API.
            // The controlled tests only build unregistered recipes and swap temporary
            // arrays; they never call Register or the game's recipe setup pipeline.
            RecipeSetupField.SetValue(null, true);
            RecipeOwnerProperty.SetValue(null, Mod);
            RegressionChecks.RunIfRequested();
        }
        finally
        {
            try { RecipeOwnerProperty.SetValue(null, owner); }
            finally { RecipeSetupField.SetValue(null, setup); }
        }
    }
}
