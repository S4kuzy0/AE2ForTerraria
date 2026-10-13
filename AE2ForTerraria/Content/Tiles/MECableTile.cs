using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// ME 网络线缆:只是把网络连过去的便宜方块,本身不提供容量也不做事。
	/// 有了它就不必为了拉距离而堆一排驱动器。
	/// </summary>
	public class MECableTile : ModTile
	{
		public override void SetStaticDefaults()
		{
			Main.tileSolid[Type] = false;
			Main.tileBlockLight[Type] = false;
			Main.tileFrameImportant[Type] = true;
			Main.tileNoFail[Type] = true;

			TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
			TileObjectData.newTile.StyleHorizontal = true;
			TileObjectData.addTile(Type);

			AddMapEntry(new Color(120, 200, 255),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MECableTile.MapEntry"));
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MECable>();
		}
	}
}
