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
	/// <summary>独立合成 CPU 方块。</summary>
	public class MECraftingCpuTile : ModTile
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
				ModContent.GetInstance<MECraftingCpuEntity>().Hook_AfterPlacement, -1, 0, false);
			TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MECraftingCpu>());

			AddMapEntry(new Color(255, 120, 140),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MECraftingCpuTile.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			if (!TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				|| entity is not MECraftingCpuEntity cpu)
			{
				return true;
			}

			Player player = Main.LocalPlayer;
			Item held = player.HeldItem;

			// 手持物品右键 = 加进合成队列;空手 = 打开队列界面
			if (held != null && held.type > 0)
			{
				long count = player.controlDown ? 10 : 1;
				string name = Lang.GetItemNameValue(held.type);

				// 多人:入队交给服务器
				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.Order(cpu.Position, ItemKey.Crafted(held.type), count);
					UIStatus.Toast(ModText.Get("MsgQueued", "已加入合成队列:") + name + " x" + count);
					return true;
				}

				if (!cpu.Enqueue(ItemKey.Crafted(held.type), count))
				{
					UIStatus.Toast("物品或数量无效", new Microsoft.Xna.Framework.Color(255, 180, 180));
					return true;
				}

				UIStatus.Toast(ModText.Get("MsgQueued", "已加入合成队列:") + name + " x" + count
					+ "(" + ModText.Get("LabelQueue", "队列") + " " + cpu.Jobs.Count + " 项)");
				return true;
			}

			UISystem.OpenCraftingCpu(cpu);
			return true;
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MECraftingCpu>();
		}

		public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
		{
			if (!fail && !effectOnly)
			{
				ModContent.GetInstance<MECraftingCpuEntity>().Kill(i, j);
			}
		}
	}
}
