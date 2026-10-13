using AE2ForTerraria.Systems;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// Right click binds this network and immediately opens its stored items.
	/// </summary>
	public class MEAccessTerminalTile : ModTile
	{
		public override void SetStaticDefaults()
		{
			Main.tileSolid[Type] = false;
			Main.tileBlockLight[Type] = false;
			Main.tileFrameImportant[Type] = true;

			TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
			TileObjectData.newTile.LavaDeath = false;
			TileObjectData.newTile.StyleHorizontal = true;
			TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MEAccessTerminal>());

			AddMapEntry(new Color(150, 180, 255),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MEAccessTerminalTile.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			Player player = Main.LocalPlayer;
			var modPlayer = player.GetModPlayer<MEPlayer>();

			modPlayer.BindTerminal(i, j);
            UISystem.OpenTerminal(i, j, TerminalViewMode.Access);

			return true;
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MEAccessTerminal>();
		}
	}
}
