using System.Linq;
using AE2ForTerraria.Core;
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
	public class MEDiskDriveTile : ModTile
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
				ModContent.GetInstance<MEDiskDriveEntity>().Hook_AfterPlacement, -1, 0, false);
			TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MEDiskDrive>());

			AddMapEntry(new Color(120, 215, 255),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MEDiskDrive.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			if (TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				&& entity is MEDiskDriveEntity drive)
			{
				UISystem.OpenDrive(drive);
			}

			return true;
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MEDiskDrive>();
		}

		public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            if (TileEntity.ByPosition.TryGetValue(new Point16(i, j), out var entity)
                && entity is MEDiskDriveEntity storage && (storage.Ledger.StoredTotalAmount > 0 || storage.Disks.Any(item=>item!=null && !item.IsAir)))
            {
                if (Main.netMode != NetmodeID.Server)
                    UIStatus.Toast("请先取出存储物品和磁盘，再拆除驱动器");
                return false;
            }
            return true;
        }
        public override bool CanExplode(int i, int j) { bool damaged = false; return CanKillTile(i, j, ref damaged); }

		public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
		{
			if (fail || effectOnly)
			{
				return;
			}

            bool damaged = false;
            if (!CanKillTile(i, j, ref damaged)) { fail = true; return; }

			var position = new Point16(i, j);
			if (!TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEDiskDriveEntity drive)
			{
				return;
			}

			var source = new EntitySource_TileBreak(i, j);

			// 拆掉时把磁盘和里面的物品都吐出来,不丢数据
			foreach (Item disk in drive.Disks)
			{
				if (disk != null && disk.type > 0 && disk.stack > 0)
				{
					Item.NewItem(source, i * 16, j * 16, 16, 16, disk);
				}
			}

			drive.Kill(i, j);
		}
	}
}
