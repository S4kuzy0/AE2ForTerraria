using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MEDisk4K : MEStorageDisk
	{
		public override long CapacityTypes => 64;

		public override void SetDefaults()
		{
			Item.width = 16;
			Item.height = 16;
			Item.maxStack = 1;
			Item.value = Item.buyPrice(silver: 80);
			Item.rare = ItemRarityID.Green;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Topaz, 6)
				.AddIngredient(ItemID.Sapphire, 4)
				.AddIngredient(ItemID.Ruby, 2)
				.AddIngredient(ItemID.Glass, 6)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

