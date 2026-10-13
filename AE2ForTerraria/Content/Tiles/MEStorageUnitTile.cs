using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;
using AE2ForTerraria.Systems;
using AE2ForTerraria.Core;
using Microsoft.Xna.Framework;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>ME 存储单元:不依赖磁盘的固定容量存储方块,可用升级件提升等级。</summary>
	public class MEStorageUnitTile : ModTile
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
				ModContent.GetInstance<MEStorageUnitEntity>().Hook_AfterPlacement, -1, 0, false);
			TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MEStorageUnit>());

			AddMapEntry(new Color(120, 215, 255),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MEStorageUnitTile.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			if (!TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				|| entity is not MEStorageUnitEntity unit)
			{
				return true;
			}

			Player player = Main.LocalPlayer;
			Item held = player.HeldItem;

			// 手持升级件 -> 升级容量
			if (held != null && held.type == ModContent.ItemType<Items.MEStorageUpgrade>())
			{
				if (unit.Level >= MEStorageUnitEntity.MaxLevel)
				{
					Main.NewText(ModText.Get("MsgUnitMax", "这个存储单元已经是最高级了"), 255, 200, 150);
					return true;
				}

				// 多人:升级也交给服务器
				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.UpgradeUnit(unit.Position);
					UIStatus.Toast(ModText.Get("MsgUpgradeSent", "已把升级请求发给服务器"));
					return true;
				}

				if (StorageOperations.TryUpgradeStorageUnit(unit, player))
				{
					UIStatus.Toast(ModText.Format("MsgUnitUpgradedDetail",
						"存储单元升级到 Lv.{0}(容量 {1} 字节)", unit.Level, unit.CurrentCapacity));
				}

				return true;
			}

			// 多人:存入交给服务器执行
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				// 拿着普通物品右键 = 打开界面(里面有「存入全部」按钮),
				// 不再直接吸走背包 —— 免得想看一眼内容却把背包清空了
				UISystem.OpenStorageUnit(unit);
				return true;
			}

			UISystem.OpenStorageUnit(unit);
			return true;
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MEStorageUnit>();
		}

		public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            if (TileEntity.ByPosition.TryGetValue(new Point16(i, j), out var entity)
                && entity is MEStorageUnitEntity storage && storage.Ledger.StoredTotalAmount > 0)
            {
                if (Main.netMode != NetmodeID.Server)
                    UIStatus.Toast("存储中还有物品，请先转移或取出后再拆除");
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
				|| entity is not MEStorageUnitEntity unit)
			{
				return;
			}

			var source = new EntitySource_TileBreak(i, j);

			unit.Kill(i, j);
		}
	}
}
