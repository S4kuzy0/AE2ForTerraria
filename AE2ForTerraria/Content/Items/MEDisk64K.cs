using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MEDisk64K : MEStorageDisk
	{
		public override long CapacityTypes => 256;

		public override void SetDefaults()
		{
			Item.width = 16;
			Item.height = 16;
			Item.maxStack = 1;
			Item.value = Item.buyPrice(gold: 5);
			Item.rare = ItemRarityID.LightRed;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Emerald, 10)
				.AddIngredient(ItemID.Ruby, 6)
				.AddIngredient(ItemID.Diamond, 3)
				.AddIngredient(ItemID.Glass, 10)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

