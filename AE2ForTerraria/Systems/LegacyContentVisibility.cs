using AE2ForTerraria.Content.Items;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Retire redundant machines from new acquisition while keeping all saved content IDs and behavior.</summary>
public sealed class LegacyContentVisibility : ModSystem
{
    public static bool IsHiddenFromCatalog(int itemType) =>
        ContentSamples.ItemsByType.TryGetValue(itemType, out var sample)
        && sample.ModItem is MECraftingCpu or MEMolecularAssembler or MEStorageUnit or MEAccessTerminal;

    public override void PostAddRecipes()
    {
        for (int i = 0; i < Recipe.numRecipes; i++)
            if (Main.recipe[i]?.createItem is { } item && IsHiddenFromCatalog(item.type))
                Main.recipe[i].DisableRecipe();
    }
}
