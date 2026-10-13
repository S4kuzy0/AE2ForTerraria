using AE2ForTerraria.Systems;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles
{
	public class MECraftingTerminalTile : ModTile
	{
		public override void SetStaticDefaults()
		{
			Main.tileSolid[Type] = false;
			Main.tileBlockLight[Type] = false;
			Main.tileFrameImportant[Type] = true;

			TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
			TileObjectData.newTile.LavaDeath = false;
			TileObjectData.newTile.StyleHorizontal = true;
			TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(ModContent.GetInstance<MECraftingTerminalEntity>().Hook_AfterPlacement, -1, 0, false);
            TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MECraftingTerminal>());

			AddMapEntry(new Color(150, 180, 255),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MECraftingTerminal.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			MachineTiles.EnsureTerminal(i, j);
            UISystem.OpenTerminal(i, j, TerminalViewMode.Craft);
			return true;
		}

        public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            if (TileEntity.ByPosition.TryGetValue(new Point16(i,j),out var entity) && entity is MECraftingTerminalEntity terminal)
                foreach (var item in terminal.Stations)
                    if (item != null && !item.IsAir) { if (Main.netMode != Terraria.ID.NetmodeID.Server) UIStatus.Toast("请先取出工作站，再拆除合成终端"); return false; }
            return true;
        }
        public override bool CanExplode(int i,int j) { bool damaged=false; return CanKillTile(i,j,ref damaged); }
        public override void KillTile(int i,int j,ref bool fail,ref bool effectOnly,ref bool noItem)
        {
            if (fail || effectOnly) return;
            bool damaged = false;
            if (!CanKillTile(i, j, ref damaged)) { fail = true; return; }
            ModContent.GetInstance<MECraftingTerminalEntity>().Kill(i,j);
        }

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MECraftingTerminal>();
		}
	}
}

