using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>独立合成 CPU:给网络加一个合成队列。</summary>
	public class MECraftingCpu : ModItem
	{
		public override void SetStaticDefaults() => Item.ResearchUnlockCount = 0;

		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MECraftingCpuTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(gold: 1);
			Item.rare = ItemRarityID.Orange;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Sapphire, 10)
				.AddIngredient(ItemID.Emerald, 6)
				.AddIngredient(ItemID.Ruby, 6)
				.AddIngredient(ItemID.Glass, 8)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}
