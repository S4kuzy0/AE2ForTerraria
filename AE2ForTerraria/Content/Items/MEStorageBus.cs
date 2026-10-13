using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>存储总线:贴着箱子放,自动把箱子里的物品搬进 ME 网络。</summary>
	public class MEStorageBus : ModItem
	{
		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MEStorageBusTile>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(silver: 80);
			Item.rare = ItemRarityID.LightRed;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Sapphire, 6)
				.AddIngredient(ItemID.Emerald, 4)
				.AddIngredient(ItemID.HallowedBar, 4)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}
