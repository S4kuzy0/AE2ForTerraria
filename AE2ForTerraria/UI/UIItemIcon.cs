using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>只负责画一个物品图标的小控件(用于配方行),可点击。</summary>
	public sealed class UIItemIcon : UIElement
	{
		public int ItemType;
		public int IconSize = 34;

		public static UIItemIcon Create(int itemType, int size, Action<int> onClicked)
		{
			var icon = new UIItemIcon { ItemType = itemType, IconSize = size };
			icon.Width.Set(size, 0f);
			icon.Height.Set(size, 0f);

			if (onClicked != null)
			{
				icon.OnLeftClick += (evt, element) => onClicked(itemType);
			}

			return icon;
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);
			if (ContainsPoint(Main.MouseScreen))
			{
				Main.LocalPlayer.mouseInterface = true;
                BrowserHover.Item(Core.ItemKey.Crafted(ItemType));
			}
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			if (ItemType <= 0)
			{
				return;
			}

			CalculatedStyle dims = GetDimensions();
			var box = new Rectangle((int)dims.X, (int)dims.Y, IconSize, IconSize);
			bool hovered = ContainsPoint(Main.MouseScreen);

			UITheme.FillRounded(spriteBatch, box, hovered ? UITheme.BtnHot : UITheme.Panel, 3);
			UITheme.Outline(spriteBatch, box, hovered ? UITheme.BorderHot : UITheme.Border);

			Main.instance.LoadItem(ItemType);
			Texture2D texture = TextureAssets.Item[ItemType].Value;
			if (texture == null)
			{
				return;
			}

			Rectangle frame = Main.itemAnimations[ItemType] != null
				? Main.itemAnimations[ItemType].GetFrame(texture, 0)
				: texture.Frame(1, 1, 0, 0);

			float scale = 1f;
			int longest = Math.Max(frame.Width, frame.Height);
			if (longest > IconSize - 8)
			{
				scale = (IconSize - 8f) / longest;
			}

			var center = new Vector2(box.X + box.Width / 2f, box.Y + box.Height / 2f);
			spriteBatch.Draw(texture, center, frame, Color.White, 0f,
				new Vector2(frame.Width / 2f, frame.Height / 2f), scale, SpriteEffects.None, 0f);
		}
	}
}
