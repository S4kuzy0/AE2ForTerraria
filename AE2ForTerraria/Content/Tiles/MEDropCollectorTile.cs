using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles
{
	public class MEDropCollectorTile : ModTile
	{
		public override void SetStaticDefaults()
		{
			Main.tileSolid[Type] = false;
			Main.tileBlockLight[Type] = false;
			Main.tileFrameImportant[Type] = true;

			TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
			TileObjectData.newTile.LavaDeath = false;
			TileObjectData.newTile.StyleHorizontal = true;
			TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
				ModContent.GetInstance<MEDropCollectorEntity>().Hook_AfterPlacement, -1, 0, false);
			TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MEDropCollector>());

			AddMapEntry(new Color(130, 255, 170),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MEDropCollector.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			// 打开**这一块**收集器自己的过滤界面(以前所有收集器共用一份全局配置)
			if (TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				&& entity is MEDropCollectorEntity collector)
			{
				UISystem.OpenDropCollector(collector);
			}

			return true;
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MEDropCollector>();
		}

		public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
		{
			if (!fail && !effectOnly)
			{
				ModContent.GetInstance<MEDropCollectorEntity>().Kill(i, j);
			}
		}
	}
}
