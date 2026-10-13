using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MEDropCollector : ModItem
	{
		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MEDropCollectorTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(silver: 50);
			Item.rare = ItemRarityID.LightRed;
		}

		public override void AddRecipes()
		{
			// 翡翠 + 红玉 + 钻石
			CreateRecipe()
				.AddIngredient(ItemID.Emerald, 8)
				.AddIngredient(ItemID.Ruby, 4)
				.AddIngredient(ItemID.Diamond, 2)
				.AddIngredient(ItemID.Glass, 6)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

