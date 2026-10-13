using System.Collections.Generic;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>存储总线方块:挨着箱子放,把箱子里的东西搬进网络。</summary>
	public class MEStorageBusTile : ModTile
	{
		public override void SetStaticDefaults()
		{
			Main.tileSolid[Type] = false;
			Main.tileBlockLight[Type] = false;
			Main.tileFrameImportant[Type] = true;

			TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
			TileObjectData.newTile.StyleHorizontal = true;
			TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
				ModContent.GetInstance<MEStorageBusEntity>().Hook_AfterPlacement, -1, 0, false);
			TileObjectData.addTile(Type);

			AddMapEntry(new Color(180, 255, 200),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MEStorageBusTile.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			var position = new Point16(i, j);

			if (!TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEStorageBusEntity bus)
			{
				return true;
			}

			Player player = Main.LocalPlayer;
			Item held = player?.HeldItem;

			// 手持物品 = 直接把它加进 / 移出分区清单;空手 = 打开配置界面
			if (held != null && held.type > 0)
			{
				if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
				{
					// 客户端自己先算一份,发给服务器定夺
					bus.Filter ??= new List<int>();
					if (bus.Filter.Contains(held.type))
					{
						bus.Filter.Remove(held.type);
					}
					else
					{
						bus.Filter.Add(held.type);
					}

					MENet.SendBusConfig(position, bus.ExportMode, bus.IsWhitelist, bus.Filter);
					return true;
				}

				if (bus.Filter == null)
				{
					bus.Filter = new List<int>();
				}

				if (bus.Filter.Contains(held.type))
				{
					bus.Filter.Remove(held.type);
				}
				else
				{
					bus.Filter.Add(held.type);
				}

				bus.MarkFilterDirty();

				if (Main.netMode == Terraria.ID.NetmodeID.Server)
				{
					MENet.SyncBusConfig(position);
				}

				UIStatus.Toast(ModText.Format("MsgBusFilterToggled",
					"分区清单:{0} {1}(共 {2} 项)",
					bus.Filter.Contains(held.type) ? ModText.Get("MsgAdded", "已加入") : ModText.Get("MsgRemoved", "已移出"),
					Lang.GetItemNameValue(held.type), bus.Filter.Count));
				return true;
			}

			UISystem.OpenStorageBus(bus);
			return true;
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MEStorageBus>();
		}

		public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
		{
			if (!fail && !effectOnly)
			{
				ModContent.GetInstance<MEStorageBusEntity>().Kill(i, j);
			}
		}
	}
}
