using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	public class MEMolecularAssembler : ModItem
	{
		public override void SetStaticDefaults() => Item.ResearchUnlockCount = 0;

		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MEMolecularAssemblerTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(gold: 1);
			Item.rare = ItemRarityID.Orange;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Sapphire, 12)
				.AddIngredient(ItemID.Emerald, 8)
				.AddIngredient(ItemID.Ruby, 6)
				.AddIngredient(ItemID.StoneBlock, 16)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}

