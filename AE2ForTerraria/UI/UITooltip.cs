using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 光标旁的提示框。网格里的物品以前只有图标,连名字都看不到。
	/// </summary>
	public static class UITooltip
	{
		private const float Scale = 0.85f;
		private const float SubScale = 0.75f;

		public static void Draw(SpriteBatch spriteBatch, string title, string sub = null, Color? titleColor = null)
		{
			if (string.IsNullOrEmpty(title))
			{
				return;
			}

			var font = FontAssets.MouseText.Value;
			Vector2 titleSize = font.MeasureString(title) * Scale;
			Vector2 subSize = string.IsNullOrEmpty(sub)
				? Vector2.Zero
				: font.MeasureString(sub) * SubScale;

			float width = Math.Max(titleSize.X, subSize.X) + 16f;
			float height = titleSize.Y + (string.IsNullOrEmpty(sub) ? 0f : subSize.Y + 2f) + 10f;

			// 光标右下,贴边时翻到另一侧
			float x = Main.mouseX + 18f;
			float y = Main.mouseY + 18f;

			if (x + width > Main.screenWidth)
			{
				x = Main.mouseX - width - 6f;
			}

			if (y + height > Main.screenHeight)
			{
				y = Main.mouseY - height - 6f;
			}

			x = Math.Clamp(x, 4f, Math.Max(4f, Main.screenWidth - width - 4f));
			y = Math.Clamp(y, 4f, Math.Max(4f, Main.screenHeight - height - 4f));

			var rect = new Rectangle((int)x, (int)y, (int)width, (int)height);

			// 深底 + 统一描边,再按稀有度给标题一条色带
			UITheme.FillRounded(spriteBatch, rect, new Color(14, 18, 30) * 0.95f, 4);

			Color accent = titleColor ?? UITheme.BorderHot;
			UITheme.Fill(spriteBatch, new Rectangle(rect.X + 4, rect.Y + 1, Math.Max(1, rect.Width - 8), 2),
				accent * 0.9f);
			UITheme.Outline(spriteBatch, rect, UITheme.Border, 1);

			Utils.DrawBorderString(spriteBatch, title, new Vector2(x + 8f, y + 5f),
				titleColor ?? UITheme.Text, Scale);

			if (!string.IsNullOrEmpty(sub))
			{
				Utils.DrawBorderString(spriteBatch, sub,
					new Vector2(x + 8f, y + 7f + titleSize.Y), UITheme.Muted, SubScale);
			}
		}
	}
}
