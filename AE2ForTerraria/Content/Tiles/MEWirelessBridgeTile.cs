using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>无线桥接器方块。</summary>
	public class MEWirelessBridgeTile : ModTile
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
				ModContent.GetInstance<MEWirelessBridgeEntity>().Hook_AfterPlacement, -1, 0, false);
			TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MEWirelessBridge>());

			AddMapEntry(new Color(200, 170, 255),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MEWirelessBridgeTile.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			if (!TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				|| entity is not MEWirelessBridgeEntity bridge)
			{
				return true;
			}

            UISystem.OpenBridge(bridge);

			return true;
		}

		/// <summary>
		/// 放好 / 换频道之后提示这个频道上现在有几个桥接器。
		/// 频道撞车会把两块本来无关的网络连在一起,以前完全没有提示。
		/// </summary>
		public static void AnnounceChannel(int i, int j, int channel)
		{
			int count = 0;

			foreach (Terraria.DataStructures.Point16 point in
				WirelessRegistry.SameChannel(channel))
			{
				if (point.X != i || point.Y != j)
				{
					count++;
				}
			}

			var color = new Microsoft.Xna.Framework.Color(200, 170, 255);

			if (count > 0)
			{
				UIStatus.Toast(ModText.Format("MsgChannelShared",
					"频道 {0} 上已有 {1} 个桥接器", channel, count), color);
			}
			else
			{
				UIStatus.Toast(ModText.Format("MsgChannelFree",
					"频道 {0} 目前只有这一个桥接器", channel), color);
			}
		}

		/// <summary>放下桥接器时立刻查一次频道占用,免得铺完才发现撞车。</summary>
		public override void PlaceInWorld(int i, int j, Item item)
		{
			if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
			{
				return;
			}

			MENetwork.InvalidateCache();
			AnnounceChannel(i, j, 0);
		}

		/// <summary>
		/// 频道可视化:在方块上方画出频道号,不用再靠聊天栏猜。
		/// 只在玩家靠近时画(和游戏里其它方块信息一样的距离)。
		/// </summary>
		public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
		{
			if (Main.gameMenu || Main.dedServ)
			{
				return;
			}

			float distance = Vector2.Distance(Main.LocalPlayer.Center,
				new Vector2(i * 16f + 8f, j * 16f + 8f));
			if (distance > 16f * 12f)
			{
				return;
			}

			if (!TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				|| entity is not MEWirelessBridgeEntity bridge)
			{
				return;
			}

			string label = bridge.Channel.ToString();
			Vector2 size = Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(label);
			var world = new Vector2(i * 16f + 8f - size.X / 2f, j * 16f - size.Y - 2f);

			// PostDraw 是在「屏幕坐标 + 可能存在的越界余量」下画的,
			// 所以要按原版那套补上 offScreenRange,否则放大/缩小视口时字会飘。
			Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
			Vector2 screen = world - Main.screenPosition + zero;

			Terraria.Utils.DrawBorderString(spriteBatch, label,
				screen + new Vector2(1f, 1f), new Color(20, 16, 40), 0.8f);
			Terraria.Utils.DrawBorderString(spriteBatch, label, screen,
				new Color(205, 175, 255), 0.8f);
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MEWirelessBridge>();
		}

		public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
		{
			if (!fail && !effectOnly)
			{
				ModContent.GetInstance<MEWirelessBridgeEntity>().Kill(i, j);
			}
		}
	}
}
