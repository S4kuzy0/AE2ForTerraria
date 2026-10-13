using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MEDisk16K : MEStorageDisk
	{
		public override long CapacityTypes => 128;

		public override void SetDefaults()
		{
			Item.width = 16;
			Item.height = 16;
			Item.maxStack = 1;
			Item.value = Item.buyPrice(gold: 2);
			Item.rare = ItemRarityID.Orange;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Sapphire, 8)
				.AddIngredient(ItemID.Emerald, 6)
				.AddIngredient(ItemID.Ruby, 4)
				.AddIngredient(ItemID.Glass, 8)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

