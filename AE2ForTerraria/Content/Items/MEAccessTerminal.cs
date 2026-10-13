using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>ME 访问终端:打开网络库存，并绑定远程访问入口。</summary>
	public class MEAccessTerminal : ModItem
	{
		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MEAccessTerminalTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(gold: 1);
			Item.rare = ItemRarityID.LightPurple;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Sapphire, 8)
				.AddIngredient(ItemID.Emerald, 6)
				.AddIngredient(ItemID.Diamond, 2)
				.AddIngredient(ItemID.Glass, 8)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}
