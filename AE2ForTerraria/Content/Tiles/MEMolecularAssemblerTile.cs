using System;
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
	public class MEMolecularAssemblerTile : ModTile
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
				ModContent.GetInstance<MEMolecularAssemblerEntity>().Hook_AfterPlacement, -1, 0, false);
			TileObjectData.addTile(Type);
            RegisterItemDrop(ModContent.ItemType<Items.MEMolecularAssembler>());

			AddMapEntry(new Color(255, 120, 140),
				Language.GetText("Mods.AE2ForTerraria.Tiles.MEMolecularAssembler.MapEntry"));
		}

		public override bool RightClick(int i, int j)
		{
			if (!TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				|| entity is not MEMolecularAssemblerEntity assembler)
			{
				return true;
			}

			Player player = Main.LocalPlayer;
			Item held = player.HeldItem;

			// 手持物品右键 = 直接设定合成目标;空手 = 打开界面
			if (held != null && held.type > 0)
			{
				var key = ItemKey.Crafted(held.type);
				string name = Lang.GetItemNameValue(held.type);

				// 多人:目标由服务器设定
				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.SetTarget(assembler.Position, key);
					UIStatus.Toast(ModText.Get("MsgTargetSet", "合成目标已设为:") + name);
					return true;
				}

				assembler.Target = key;
				assembler.LastStatus = ModText.Get("LabelTarget", "当前目标") + ":" + name;
				UIStatus.Toast(ModText.Get("MsgTargetSet", "合成目标已设为:") + name);
				return true;
			}

			UISystem.OpenAssembler(assembler);
			return true;
		}

		/// <summary>
		/// 方块本身画一条进度线:站在机器前就能看出在不在干活、还差多少,
		/// 不用每次都按开界面。
		/// </summary>
		public override void PostDraw(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
		{
			if (Main.gameMenu || Main.dedServ)
			{
				return;
			}

			if (Vector2.Distance(Main.LocalPlayer.Center,
				new Vector2(i * 16f + 8f, j * 16f + 8f)) > 16f * 14f)
			{
				return;
			}

			if (!TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				|| entity is not MEMolecularAssemblerEntity assembler)
			{
				return;
			}

			bool idle = assembler.ActiveKey.IsAir;
			float fraction = idle ? 0f : assembler.ProgressFraction;

			// PostDraw 是屏幕坐标 + 可能的越界余量
			Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
			var topLeft = new Vector2(i * 16f - Main.screenPosition.X, j * 16f - Main.screenPosition.Y) + zero;

			var texture = Terraria.GameContent.TextureAssets.MagicPixel.Value;
			var bar = new Rectangle((int)topLeft.X + 2, (int)topLeft.Y - 3, 12, 2);

			spriteBatch.Draw(texture, bar, new Color(14, 18, 32) * 0.85f);

			if (idle)
			{
				// 空闲:画一条暗灰短线,表示「没在跑」
				spriteBatch.Draw(texture, new Rectangle(bar.X, bar.Y, 12, 2),
					new Color(90, 100, 120) * 0.7f);
				return;
			}

			int filled = Math.Max(1, (int)(12f * fraction));
			spriteBatch.Draw(texture, new Rectangle(bar.X, bar.Y, filled, 2),
				new Color(120, 220, 255));
		}

		public override void MouseOver(int i, int j)
		{
			Player player = Main.LocalPlayer;
			player.noThrow = 2;
			player.cursorItemIconEnabled = true;
			player.cursorItemIconID = ModContent.ItemType<Items.MEMolecularAssembler>();
		}

        public override bool CanKillTile(int i,int j,ref bool blockDamaged)
        {
            if (TileEntity.ByPosition.TryGetValue(new Point16(i,j),out var entity)
                && entity is MEMolecularAssemblerEntity assembler
                && assembler.Stations.Any(item=>item!=null && !item.IsAir))
            {
                if (Main.netMode!=NetmodeID.Server) UIStatus.Toast("请先取出工作站，再拆除装配室");
                return false;
            }
            return true;
        }
        public override bool CanExplode(int i,int j) { bool damaged=false; return CanKillTile(i,j,ref damaged); }

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
				|| entity is not MEMolecularAssemblerEntity assembler)
			{
				return;
			}

			// 把装在里面的工作站吐出来
			var source = new EntitySource_TileBreak(i, j);
			foreach (Item station in assembler.Stations)
			{
				if (station != null && station.type > 0 && station.stack > 0)
				{
					Item.NewItem(source, i * 16, j * 16, 16, 16, station);
				}
			}

			assembler.Kill(i, j);
		}
	}
}
