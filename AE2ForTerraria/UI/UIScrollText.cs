using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 一块可以滚轮上下滚的纯文本(帮助页用)。
	///
	/// tML 的 UIText 不做裁剪 —— 内容超出框会直接画到框外面去。
	/// 所以这里自己一行一行画,只画落在框里的那些行,滚轮往下滚 = 往后看。
	/// </summary>
	public sealed class UIScrollText : UIElement
	{
		private readonly List<string> _lines = new List<string>();

		public float TextScale = 0.72f;
		public float LineHeight = 19f;
		public Color TextColor = UITheme.TextDim;

		private int _scroll;

		public void SetText(string text)
		{
			_lines.Clear();

			if (!string.IsNullOrEmpty(text))
			{
				foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
				{
					_lines.Add(line);
				}
			}

			_scroll = 0;
		}

		private int MaxScroll
		{
			get
			{
				float content = _lines.Count * LineHeight + 16f;
				return Math.Max(0, (int)(content - GetDimensions().Height));
			}
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (!ContainsPoint(Main.MouseScreen))
			{
				return;
			}

			// 滚轮往下滚 = 往后看(和浏览器网格、Recipe Browser 一致)
			int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;

			if (wheel == 0)
			{
				return;
			}

			_scroll = Math.Clamp(_scroll - wheel * (int)LineHeight * 2, 0, MaxScroll);
			Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
			Terraria.GameInput.PlayerInput.LockVanillaMouseScroll("AE2ForTerraria");
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var box = dims.ToRectangle();

			UITheme.FillRounded(spriteBatch, box, UITheme.PanelAlt * 0.9f, 4);
			UITheme.Outline(spriteBatch, box, UITheme.Border);

			float y = dims.Y + 8f - _scroll;
			float left = dims.X + 10f;

			foreach (string line in _lines)
			{
				if (y > dims.Y + dims.Height - 4f)
				{
					break;
				}

				if (y + LineHeight >= dims.Y + 4f && !string.IsNullOrEmpty(line))
				{
					Terraria.Utils.DrawBorderString(spriteBatch, line,
						new Vector2(left, y), TextColor, TextScale);
				}

				y += LineHeight;
			}

			// 还有内容没露出来时,右下角给个小箭头
			if (MaxScroll > 0)
			{
				UITheme.DrawTriangleDown(spriteBatch,
					(int)(dims.X + dims.Width - 14f),
					(int)(dims.Y + dims.Height - 16f), UITheme.Muted);
			}
		}
	}
}
