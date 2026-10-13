using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MECraftingTerminal : ModItem
	{
		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MECraftingTerminalTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(silver: 40);
			Item.rare = ItemRarityID.Green;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Topaz, 8)
				.AddIngredient(ItemID.Sapphire, 6)
				.AddIngredient(ItemID.Glass, 10)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

