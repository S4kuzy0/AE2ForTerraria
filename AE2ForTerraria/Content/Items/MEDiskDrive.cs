using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MEDiskDrive : ModItem
	{
		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MEDiskDriveTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(silver: 30);
			Item.rare = ItemRarityID.Blue;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Amethyst, 10)
				.AddIngredient(ItemID.Topaz, 6)
				.AddIngredient(ItemID.StoneBlock, 12)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

