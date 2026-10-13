using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>ME 存储单元:固定容量的存储方块(不需要磁盘)。</summary>
	public class MEStorageUnit : ModItem
	{
		public override void SetStaticDefaults() => Item.ResearchUnlockCount = 0;

		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MEStorageUnitTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(silver: 60);
			Item.rare = ItemRarityID.Green;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Amethyst, 12)
				.AddIngredient(ItemID.Topaz, 8)
				.AddIngredient(ItemID.Sapphire, 4)
				.AddIngredient(ItemID.Glass, 10)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}
