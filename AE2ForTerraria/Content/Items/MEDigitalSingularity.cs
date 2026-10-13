using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Content.Items;

/// <summary>Endgame capacity module. Contents still belong to the disk drive, never this item.</summary>
public sealed class MEDigitalSingularity : MEStorageDisk
{
    public override long CapacityBytes => long.MaxValue;
    public override long CapacityTypes => long.MaxValue;
    public override long CapacityPerType => long.MaxValue;

    public override void SetDefaults()
    {
        Item.width = 32;
        Item.height = 32;
        Item.maxStack = 1;
        Item.value = Item.buyPrice(gold: 20);
        Item.rare = ItemRarityID.Red;
    }

    public override void AddRecipes() => CreateRecipe()
        .AddIngredient(ItemID.LunarBar, 8)
        .AddTile(TileID.LunarCraftingStation)
        .Register();
}
