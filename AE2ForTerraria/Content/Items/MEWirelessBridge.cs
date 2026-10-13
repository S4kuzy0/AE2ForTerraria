using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>无线桥接器:同频道的两个桥接器会把两块网络连成一个。</summary>
	public class MEWirelessBridge : ModItem
	{
		public override void SetDefaults()
		{
			Item.DefaultToPlaceableTile(ModContent.TileType<MEWirelessBridgeTile2x2>());
			Item.width = 16;
			Item.height = 16;
			Item.value = Item.buyPrice(gold: 2);
			Item.rare = ItemRarityID.Pink;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Diamond, 4)
				.AddIngredient(ItemID.Emerald, 6)
				.AddIngredient(ItemID.Ruby, 6)
				.AddIngredient(ItemID.Wire, 20)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}
