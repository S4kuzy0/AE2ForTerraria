using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MEDisk1K : MEStorageDisk
	{
		public override long CapacityTypes => 32;

		public override void SetDefaults()
		{
			Item.width = 16;
			Item.height = 16;
			Item.maxStack = 1;
			Item.value = Item.buyPrice(silver: 30);
			Item.rare = ItemRarityID.Blue;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Amethyst, 4)
				.AddIngredient(ItemID.Topaz, 2)
				.AddIngredient(ItemID.Glass, 4)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

