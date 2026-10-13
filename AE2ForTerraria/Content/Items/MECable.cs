using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>网络线缆:便宜地把 ME 网络连过去。</summary>
	public class MECable : ModItem
	{
		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MECableTile>());
			Item.width = 16;
			Item.height = 16;
			Item.maxStack = 9999;
			Item.value = Item.buyPrice(copper: 50);
			Item.rare = ItemRarityID.Blue;
		}

		public override void AddRecipes()
		{
			CreateRecipe(25)
				.AddIngredient(ItemID.Amethyst, 3)
				.AddIngredient(ItemID.Glass, 5)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}
