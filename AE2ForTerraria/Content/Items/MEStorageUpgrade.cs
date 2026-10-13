using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Items
{
	/// <summary>
	/// 存储单元升级件。手持它右键 ME 存储单元即可升一级:
	/// Lv.1 8K 字节 -> Lv.2 32K -> Lv.3 128K。
	/// </summary>
	public class MEStorageUpgrade : ModItem
	{
		public override void SetDefaults()
		{
			Item.width = 16;
			Item.height = 16;
			Item.maxStack = 99;
			Item.consumable = true;
			Item.value = Item.buyPrice(gold: 1);
			Item.rare = ItemRarityID.LightPurple;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Emerald, 8)
				.AddIngredient(ItemID.Ruby, 6)
				.AddIngredient(ItemID.Diamond, 4)
				.AddIngredient(ItemID.Glass, 8)
				.AddTile(TileID.WorkBenches)
				.Register();
		}
	}
}
