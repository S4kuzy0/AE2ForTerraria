using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 统一配色与控件样式。
	///
	/// 以前每个窗口各自写 `new Color(...)`,于是同一个"面板底"在浏览器、终端、
	/// 驱动器里是三种深浅;按钮更是随手一个色块。这里把色值收成一套令牌,
	/// 所有窗口都从这里取 —— 改一次全局生效。
	///
	/// 另一条硬性约束:**tML 的字体没有 emoji**。所以所有图标都用这里画的
	/// 方块 / 圆圈 / 三角 / 十字(`DrawLock`, `DrawKeep`, `DrawCross`, ...),
	/// 不要往字符串里塞 emoji。
	/// </summary>
	public static class UITheme
	{
		// —— 令牌 ——
		public static readonly Color WindowBg = new Color(63, 82, 151);
		public static readonly Color WindowBorder = new Color(25, 37, 80);
		public static readonly Color TitleBar = new Color(48, 67, 126);
		public static readonly Color TitleBarHot = new Color(58, 74, 108);
		public static readonly Color Panel = new Color(40, 58, 113);
		public static readonly Color PanelAlt = new Color(48, 67, 126);
		public static readonly Color Field = new Color(25, 39, 78);
		public static readonly Color Slot = new Color(47, 65, 118);
		public static readonly Color ItemBorder = Color.Black;
		public static readonly Color SlotInner = new Color(72, 84, 118);
		public static readonly Color Border = new Color(58, 71, 99);
		public static readonly Color BorderHot = new Color(96, 140, 214);
		public static readonly Color Divider = new Color(58, 71, 99) * 0.85f;
		public static readonly Color Zebra = new Color(255, 255, 255, 9);

		public static readonly Color Accent = new Color(76, 134, 224);
		public static readonly Color AccentHot = new Color(105, 162, 245);
		public static readonly Color Btn = new Color(51, 72, 133);
		public static readonly Color BtnHot = new Color(66, 88, 157);
		public static readonly Color BtnDisabled = new Color(40, 49, 83);

		public static readonly Color Text = new Color(232, 237, 247);
		public static readonly Color TextDim = new Color(196, 208, 228);
		public static readonly Color Muted = new Color(159, 176, 204);
		public static readonly Color Ok = new Color(111, 208, 140);
		public static readonly Color Warn = new Color(242, 180, 87);
		public static readonly Color Danger = new Color(232, 112, 95);
		public static readonly Color Info = new Color(111, 198, 232);
		public static readonly Color Lock = new Color(214, 148, 46);
		public static readonly Color Keep = new Color(96, 176, 214);
		public static readonly Color Select = new Color(120, 240, 170);
		public static readonly Color TabOff = new Color(120, 138, 172);

		/// <summary>稀有度描边色(和原版稀有度颜色一致)。</summary>
		public static Color Rarity(int rare)
		{
			switch (rare)
			{
				case ItemRarityID.Blue: return new Color(150, 190, 255);
				case ItemRarityID.Green: return new Color(140, 240, 140);
				case ItemRarityID.Orange: return new Color(255, 180, 90);
				case ItemRarityID.LightRed: return new Color(255, 140, 140);
				case ItemRarityID.Pink: return new Color(255, 160, 220);
				case ItemRarityID.LightPurple: return new Color(210, 160, 255);
				case ItemRarityID.Lime: return new Color(200, 255, 120);
				case ItemRarityID.Yellow: return new Color(255, 240, 120);
				case ItemRarityID.Cyan: return new Color(140, 250, 250);
				case ItemRarityID.Red: return new Color(255, 110, 110);
				case ItemRarityID.Purple: return new Color(200, 130, 255);
				default: return new Color(200, 205, 215);
			}
		}

		public static Color CapacityColor(float ratio)
		{
			return ratio >= 0.95f ? Danger : ratio >= 0.8f ? Warn : Info;
		}

		public static Color Blend(Color a, Color b, float t)
		{
			t = MathHelper.Clamp(t, 0f, 1f);
			return new Color(
				(int)(a.R + (b.R - a.R) * t),
				(int)(a.G + (b.G - a.G) * t),
				(int)(a.B + (b.B - a.B) * t),
				(int)(a.A + (b.A - a.A) * t));
		}

		/// <summary>提亮(悬停 / 选中时用),不改透明度。</summary>
		public static Color Lighten(Color c, float amount = 0.12f)
		{
			return Blend(c, new Color(255, 255, 255, c.A), amount);
		}

		// —— 绘制工具 ——

		public static void Fill(SpriteBatch sb, Rectangle r, Color c)
		{
			if (r.Width <= 0 || r.Height <= 0)
			{
				return;
			}

			sb.Draw(TextureAssets.MagicPixel.Value, r, c);
		}

		public static void Outline(SpriteBatch sb, Rectangle r, Color c, int thickness = 1)
		{
			Fill(sb, new Rectangle(r.X, r.Y, r.Width, thickness), c);
			Fill(sb, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
			Fill(sb, new Rectangle(r.X, r.Y, thickness, r.Height), c);
			Fill(sb, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
		}

		public static void FillOutline(SpriteBatch sb, Rectangle r, Color fill, Color border,
			int thickness = 1)
		{
			Fill(sb, r, fill);
			Outline(sb, r, border, thickness);
		}

		/// <summary>圆角矩形(纯色块画出来的,不引入贴图)。radius 一般 3~5。</summary>
		public static void FillRounded(SpriteBatch sb, Rectangle r, Color c, int radius = 4)
		{
			if (r.Width <= 0 || r.Height <= 0)
			{
				return;
			}

			radius = Math.Clamp(radius, 0, Math.Min(r.Width, r.Height) / 2);

			if (radius == 0)
			{
				Fill(sb, r, c);
				return;
			}

			Fill(sb, new Rectangle(r.X + radius, r.Y, r.Width - radius * 2, r.Height), c);
			Fill(sb, new Rectangle(r.X, r.Y + radius, radius, r.Height - radius * 2), c);
			Fill(sb, new Rectangle(r.Right - radius, r.Y + radius, radius, r.Height - radius * 2), c);

			for (int i = 0; i < radius; i++)
			{
				int inset = radius - i;
				int width = r.Width - inset * 2;
				Fill(sb, new Rectangle(r.X + inset, r.Y + i, width, 1), c);
				Fill(sb, new Rectangle(r.X + inset, r.Bottom - 1 - i, width, 1), c);
			}
		}

		/// <summary>两点画线(Bresenham 近似),图标用。</summary>
		public static void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color c, int thickness = 1)
		{
			int steps = (int)Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) + 1;

			for (int i = 0; i <= steps; i++)
			{
				Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
				Fill(sb, new Rectangle((int)p.X, (int)p.Y, thickness, thickness), c);
			}
		}

		/// <summary>进度条(容量 / 合成进度都用它)。</summary>
		public static void Bar(SpriteBatch sb, Rectangle r, float ratio, Color color)
		{
			Fill(sb, r, new Color(18, 22, 32));

			int w = (int)((r.Width - 4) * MathHelper.Clamp(ratio, 0f, 1f));
			if (w > 0)
			{
				Fill(sb, new Rectangle(r.X + 2, r.Y + 2, w, r.Height - 4), color);
			}
		}

		public static void DrawCross(SpriteBatch sb, float x, float y, Color color, float size = 9f)
		{
			// 两像素宽的斜线用阶梯近似(不引入额外贴图)
			for (int i = 0; i <= (int)size; i++)
			{
				Fill(sb, new Rectangle((int)(x + i), (int)(y + i), 2, 2), color);
				Fill(sb, new Rectangle((int)(x + size - i), (int)(y + i), 2, 2), color);
			}
		}

		public static void DrawReset(SpriteBatch sb, float x, float y, Color color)
		{
			// 一个缺口的圆 + 箭头,表示"重置布局"
			for (int a = 40; a <= 320; a += 12)
			{
				double rad = a * Math.PI / 180.0;
				Fill(sb, new Rectangle((int)(x + 6 + Math.Cos(rad) * 6),
					(int)(y + 6 + Math.Sin(rad) * 6), 2, 2), color);
			}

			Fill(sb, new Rectangle((int)x + 9, (int)y, 4, 2), color);
			Fill(sb, new Rectangle((int)x + 11, (int)y - 1, 2, 4), color);
		}

		/// <summary>放大镜(搜索框左侧)。</summary>
		public static void DrawMagnifier(SpriteBatch sb, int x, int y, Color color)
		{
			const int radius = 4;

			for (int a = 0; a < 360; a += 18)
			{
				double rad = a * Math.PI / 180.0;
				Fill(sb, new Rectangle(
					(int)(x + radius + Math.Cos(rad) * radius),
					(int)(y + radius + Math.Sin(rad) * radius), 1, 1), color);
			}

			Line(sb, new Vector2(x + radius + 3, y + radius + 3),
				new Vector2(x + radius + 6, y + radius + 6), color, 2);
		}

		// Redrawn from JEI's MIT-licensed silhouette; see ThirdPartyNotices.txt.
		private static readonly string[] WrenchPixels =
		{
			"   ....         ",
			"   .hhh.        ",
			"    .sbh.       ",
			"..   .sh.       ",
			".h.  .sh.       ",
			".bh..hbh.       ",
			".sbhhbbbh.      ",
			" .ssssbbbh.     ",
			"  ....sbbbh.    ",
			"      .sbbbh.   ",
			"       .sbbbh.  ",
			"        .sbbbh. ",
			"         .sbbbh.",
			"          .sb.h.",
			"           .sss.",
			"            ... "
		};

		private static readonly (Rectangle Bounds, Color Color)[] WrenchRuns = CreateWrenchRuns();

		private static (Rectangle Bounds, Color Color)[] CreateWrenchRuns()
		{
			var runs = new List<(Rectangle, Color)>();
			for (int y = 0; y < WrenchPixels.Length; y++)
			{
				string row = WrenchPixels[y];
				for (int x = 0; x < row.Length;)
				{
					char pixel = row[x];
					int start = x++;
					while (x < row.Length && row[x] == pixel) x++;
					if (pixel == ' ') continue;
					Color color = pixel switch
					{
						'.' => new Color(17, 24, 33),
						'h' => new Color(255, 174, 163),
						's' => new Color(152, 42, 55),
						_ => new Color(228, 81, 88)
					};
					// The approved 18-pixel canvas has a one-pixel transparent margin.
					runs.Add((new Rectangle(start - 8, y - 8, x - start, 1), color));
				}
			}
			return runs.ToArray();
		}

		/// <summary>居中的像素红扳手；禁用时保留阴影并采用按钮的灰色。</summary>
		public static void DrawWrench(SpriteBatch sb, int cx, int cy, Color color, bool enabled = true)
		{
			foreach (var run in WrenchRuns)
			{
				Rectangle bounds = run.Bounds;
				bounds.Offset(cx, cy);
				Color ink = run.Color;
				if (!enabled)
				{
					float shade = Math.Max(ink.R, Math.Max(ink.G, ink.B)) / 255f;
					ink = new Color((int)(color.R * shade), (int)(color.G * shade),
						(int)(color.B * shade), color.A);
				}
				Fill(sb, bounds, ink);
			}
		}

		/// <summary>锁定角标:琥珀色小方块。</summary>
		public static void DrawLock(SpriteBatch sb, int x, int y)
		{
			FillOutline(sb, new Rectangle(x, y, 7, 7), Lock, new Color(120, 78, 20));
			Fill(sb, new Rectangle(x + 2, y - 2, 3, 3), new Color(120, 78, 20));
		}

		/// <summary>维持库存角标:青色小方块。</summary>
		public static void DrawKeep(SpriteBatch sb, int x, int y)
		{
			FillOutline(sb, new Rectangle(x, y, 7, 7), Keep, new Color(40, 96, 128));
		}

		public static void DrawTriangle(SpriteBatch sb, int cx, int cy, bool left, Color color)
		{
			for (int i = 0; i < 6; i++)
			{
				int w = 6 - i;
				int x = left ? cx + i : cx - i;
				Fill(sb, new Rectangle(x, cy - w / 2 - 1, 1, Math.Max(1, w)), color);
			}
		}

		public static void DrawTriangleUp(SpriteBatch sb, int cx, int cy, Color color)
		{
			for (int i = 0; i < 6; i++)
			{
				Fill(sb, new Rectangle(cx - i, cy - i, 1 + i * 2, 1), color);
			}
		}

		public static void DrawTriangleDown(SpriteBatch sb, int cx, int cy, Color color)
		{
			for (int i = 0; i < 6; i++)
			{
				Fill(sb, new Rectangle(cx - 5 + i, cy + i, 11 - i * 2, 1), color);
			}
		}

		/// <summary>五角星(收藏)。filled=true 线条更粗。</summary>
		public static void DrawStar(SpriteBatch sb, float cx, float cy, bool filled, Color color,
			float radius = 6f)
		{
			var points = new Vector2[10];

			for (int i = 0; i < 10; i++)
			{
				float angle = -MathHelper.PiOver2 + i * MathHelper.Pi / 5f;
				float r = i % 2 == 0 ? radius : radius * 0.45f;
				points[i] = new Vector2(cx + (float)Math.Cos(angle) * r,
					cy + (float)Math.Sin(angle) * r);
			}

			for (int i = 0; i < 10; i++)
			{
				Line(sb, points[i], points[(i + 1) % 10], color, filled ? 2 : 1);
			}

			if (filled)
			{
				Fill(sb, new Rectangle((int)cx - 1, (int)cy - 1, 3, 3), color);
			}
		}

		/// <summary>严重度圆点(缺料 / 健康检查)。</summary>
		public static void DrawDot(SpriteBatch sb, int cx, int cy, int radius, Color color)
		{
			for (int dy = -radius; dy <= radius; dy++)
			{
				int half = (int)Math.Sqrt(Math.Max(0, radius * radius - dy * dy));
				Fill(sb, new Rectangle(cx - half, cy + dy, half * 2 + 1, 1), color);
			}
		}

		/// <summary>鼠标悬停描边(网格 / 行 / 卡片统一用它)。</summary>
		public static void DrawHotOutline(SpriteBatch sb, Rectangle r)
		{
			Outline(sb, r, BorderHot);
		}

		/// <summary>胶囊标签(严重度 / 计数)。文字由调用方保证短。</summary>
		public static void DrawPill(SpriteBatch sb, int x, int y, string text, Color color,
			float scale = 0.7f)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * scale;
			var r = new Rectangle(x, y, (int)size.X + 12, (int)size.Y + 4);

			FillRounded(sb, r, new Color(color.R, color.G, color.B, 46), r.Height / 2);
			Outline(sb, r, color * 0.8f);
			Utils.DrawBorderString(sb, text, new Vector2(r.X + 6f, r.Y + 2f), color, scale);
		}

		/// <summary>小色块(网络构成的图例)。</summary>
		public static void DrawSwatch(SpriteBatch sb, int x, int y, Color color, int size = 7)
		{
			FillRounded(sb, new Rectangle(x, y, size, size), color, 1);
		}
	}

	/// <summary>
	/// 统一按钮:圆角、普通 / 悬停 / 禁用三态,可选"主操作"样式。
	/// 直接替代 `new UITextPanel&lt;string&gt;(...)`。
	///
	/// 文字仍然交给基类画(它自己有缩放和居中逻辑),
	/// 我们只在底下垫一层圆角块 —— 这样 `BackgroundColor = ...`
	/// 这种老代码里到处都有的状态着色照样生效。
	/// </summary>
	public sealed class UIButton : UITextPanel<string>
	{
		public bool Primary;
		public bool Selected;
		public bool Flat;
        public bool Dropdown;
		public bool Enabled = true;

		private bool _hover;
        private float _labelScale;

		public UIButton(string text, float scale = 0.78f, bool primary = false)
			: base(text, scale, false)
		{
			SetPadding(4f);
            Primary = primary;
			BackgroundColor = primary ? UITheme.Accent : UITheme.Btn;
			TextColor = primary ? new Color(16, 20, 30) : UITheme.Text;
			BorderColor = Color.Transparent;
		}

        public override void SetText(string text,float scale,bool large)
        {
            base.SetText(text,scale,large); _labelScale=scale;
            MinWidth.Set(0f,0f); MinHeight.Set(0f,0f);
        }
		public override void LeftClick(UIMouseEvent evt) { if (Enabled) base.LeftClick(evt); }
        public override void RightClick(UIMouseEvent evt) { if (Enabled) base.RightClick(evt); }
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _hover = ContainsPoint(Main.MouseScreen);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var rect = dims.ToRectangle();

			if (rect.Width <= 0 || rect.Height <= 0)
			{
				return;
			}

			Color fill;
			Color text;

			if (!Enabled)
			{
				fill = UITheme.BtnDisabled;
				text = UITheme.Muted;
			}
			else if (Primary || Selected)
			{
				fill = _hover ? UITheme.AccentHot : UITheme.Accent;
				text = Color.White;
			}
			else
			{
				fill = _hover ? UITheme.Lighten(BackgroundColor, 0.18f) : Flat ? Color.Transparent : BackgroundColor;
				text = TextColor;
			}

			Color border = !Enabled
				? UITheme.Border * 0.6f
				: _hover ? UITheme.BorderHot : Flat ? Color.Transparent : UITheme.Border;

			int radius = rect.Height <= 34 ? Math.Min(5, rect.Height / 2) : 3;

			UITheme.FillRounded(spriteBatch, rect, fill, radius);
			UITheme.Outline(spriteBatch, rect, border, 1);

            float width=FontAssets.MouseText.Value.MeasureString(Text).X;
            float labelWidth=dims.Width-(Dropdown ? 18f : 0f);
            float scale=Math.Min(_labelScale,Math.Max(0.1f,(labelWidth-10f)/Math.Max(1f,width)));
            Utils.DrawBorderString(spriteBatch,Text,new Vector2(dims.X+labelWidth/2f,dims.Y+(dims.Height-16f*scale)/2f),
                text,scale,0.5f,0f);
            if (Dropdown) UITheme.DrawTriangleDown(spriteBatch,rect.Right-12,rect.Center.Y,text);

		}
	}

	/// <summary>
	/// 下划线标签页:选中项底下一条强调色,而不是整块变色。
	/// 分类(物品 / 怪物)这种"平级切换"用它。
	/// </summary>
	public sealed class UIUnderlineTab : UITextPanel<string>
	{
		public bool Selected;

		private bool _hover;

		public UIUnderlineTab(string text, float scale = 0.82f)
			: base(text, scale, false)
		{
			SetPadding(0f);
			BackgroundColor = Color.Transparent;
			BorderColor = Color.Transparent;
			TextColor = UITheme.TabOff;
		}
		public override void SetText(string text, float scale, bool large)
		{
			base.SetText(text, scale, large);
			MinWidth.Set(0f, 0f); MinHeight.Set(0f, 0f);
		}

		public void Refresh()
		{
			TextColor = Selected ? UITheme.Text : _hover ? UITheme.TextDim : UITheme.TabOff;
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			bool hover = ContainsPoint(Main.MouseScreen);
			if (hover != _hover)
			{
				_hover = hover;
				Refresh();
			}
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var rect = dims.ToRectangle();

			// 悬停时给一层很淡的底,提示"这里能点"
			if (_hover && !Selected)
			{
				UITheme.Fill(spriteBatch,
					new Rectangle(rect.X, rect.Y + 2, rect.Width, rect.Height - 4),
					new Color(255, 255, 255, 8));
			}

			Color previousBackground = BackgroundColor;
			Color previousBorder = BorderColor;
			Color previousText = TextColor;

			BackgroundColor = Color.Transparent;
			BorderColor = Color.Transparent;
			TextColor = Selected ? UITheme.Text : _hover ? UITheme.TextDim : UITheme.TabOff;

			base.DrawSelf(spriteBatch);

			BackgroundColor = previousBackground;
			BorderColor = previousBorder;
			TextColor = previousText;

			if (Selected)
			{
				UITheme.Fill(spriteBatch,
					new Rectangle(rect.X + 4, rect.Bottom - 2, Math.Max(1, rect.Width - 8), 2),
					UITheme.Accent);
			}
		}
	}

	/// <summary>
	/// 图标按钮:文字为空,图形由代码画(收藏星、关闭叉、左右箭头、重置)。
	/// 用它可以彻底躲开「tML 字体里到底有没有 ★ ◀ ×」这个问题。
	/// </summary>
	public sealed class UIIconButton : UITextPanel<string>
	{
		public enum IconKind
		{
			Star,
			Cross,
			Left,
			Right,
			Reset,
			Wrench
		}

		public IconKind Icon = IconKind.Star;

		/// <summary>星形等图标:true = 填充态(选中 / 开启)。</summary>
		public bool Filled;

		/// <summary>true = 用危险色(关闭按钮悬停)。</summary>
		public bool Danger;

		public bool Enabled = true;

		private bool _hover;

		public Color? GlyphColor;

		public UIIconButton(IconKind icon) : base(string.Empty, 0.8f, false)
		{
			// Icons are drawn directly; the empty text panel's default minimum must not enlarge them.
			SetPadding(0f);
			MinWidth.Set(0f, 0f);
			MinHeight.Set(0f, 0f);
			Icon = icon;
			BackgroundColor = UITheme.Btn;
			BorderColor = Color.Transparent;
			TextColor = Color.Transparent;
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);
			_hover = ContainsPoint(Main.MouseScreen);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var rect = dims.ToRectangle();

			if (rect.Width <= 0 || rect.Height <= 0)
			{
				return;
			}

			Color fill = !Enabled
				? UITheme.BtnDisabled
				: _hover
					? UITheme.Lighten(BackgroundColor, 0.2f)
					: BackgroundColor;

			if (Filled && Enabled)
			{
				fill = _hover ? UITheme.Warn : UITheme.Warn * 0.8f;
			}

			Color border = _hover
				? Danger ? UITheme.Danger : UITheme.BorderHot
				: UITheme.Border;

			int radius = Math.Min(5, Math.Min(rect.Width, rect.Height) / 2);
			UITheme.FillRounded(spriteBatch, rect, fill, radius);
			UITheme.Outline(spriteBatch, rect, border);

			Color glyph = !Enabled
				? UITheme.Muted
				: Filled ? new Color(38, 26, 10)
					: _hover ? Danger ? UITheme.Danger : UITheme.Text
						: Danger ? UITheme.Muted : UITheme.TextDim;

			int cx = rect.X + rect.Width / 2;
			int cy = rect.Y + rect.Height / 2;
			if (Enabled && GlyphColor.HasValue) glyph = GlyphColor.Value;

			switch (Icon)
			{
				case IconKind.Star:
					UITheme.DrawStar(spriteBatch, cx, cy, Filled, glyph, 6f);
					break;

				case IconKind.Cross:
					UITheme.DrawCross(spriteBatch, cx - 4.5f, cy - 4.5f, glyph, 9f);
					break;

				case IconKind.Left:
					UITheme.DrawTriangle(spriteBatch, cx, cy, true, glyph);
					break;

				case IconKind.Right:
					UITheme.DrawTriangle(spriteBatch, cx, cy, false, glyph);
					break;

				case IconKind.Reset:
					UITheme.DrawReset(spriteBatch, cx - 6f, cy - 6f, glyph);
					break;

				case IconKind.Wrench:
					UITheme.DrawWrench(spriteBatch, cx, cy, glyph, Enabled);
					break;
			}
		}
	}

	/// <summary>
	/// 搜索框外框:圆角输入框 + 框内放大镜 + 聚焦时的强调色描边。
	/// 文字由宿主塞进来的 UIText 子元素负责(它自己处理光标位置)。
	/// </summary>
	public sealed class UISearchField : UIElement
	{
		public bool Focused;
		public bool ShowIcon = true;

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var rect = dims.ToRectangle();

			if (rect.Width <= 0 || rect.Height <= 0)
			{
				return;
			}

			bool hover = ContainsPoint(Main.MouseScreen);

			UITheme.FillRounded(spriteBatch, rect, UITheme.Field, 4);
			UITheme.Outline(spriteBatch, rect,
				Focused ? UITheme.Accent : hover ? UITheme.BorderHot : UITheme.Border);

			if (ShowIcon)
			{
				UITheme.DrawMagnifier(spriteBatch, rect.X + 6, rect.Y + (rect.Height - 11) / 2,
					Focused ? UITheme.AccentHot : UITheme.Muted);
			}
		}
	}

	/// <summary>
	/// 「网络容量」一行:左边文字 + 右边进度条。终端、驱动器、存储单元共用,
	/// 快满时自动变黄 / 变红,不用玩家自己去算。
	/// </summary>
	public sealed class UICapacityBar : UIElement
	{
		public long Used;
		public long Max;
		public string Label = string.Empty;

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var rect = dims.ToRectangle();

			if (rect.Width <= 8 || rect.Height <= 4)
			{
				return;
			}

			float ratio = Max > 0 ? MathHelper.Clamp(Used / (float)Max, 0f, 1f) : 0f;
			Color color = Max > 0 ? UITheme.CapacityColor(ratio) : UITheme.Muted;

			string text = Label + "  " + UIFormat.Count(Used) + " / "
				+ (Max > 0 ? UIFormat.Count(Max) : "—")
				+ (Max > 0 ? "  (" + (int)(ratio * 100f) + "%)" : string.Empty);

			Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * 0.7f;
			Utils.DrawBorderString(spriteBatch, text,
				new Vector2(rect.X, rect.Y + (rect.Height - 14) / 2f),
				Max > 0 && ratio >= 0.95f ? UITheme.Danger : UITheme.TextDim, 0.7f);

			var bar = new Rectangle(rect.X + (int)size.X + 8, rect.Y + rect.Height / 2 - 3,
				Math.Max(40, rect.Width - (int)size.X - 8), 7);

			UITheme.Bar(spriteBatch, bar, ratio, color);
		}
	}

	/// <summary>
	/// 细滚动条。网格按行滚动,这里把拖动换算成滚动比例 ——
	/// 以前按页码换算,一屏好几行时拖一下会跳一整页。
	/// </summary>
	public sealed class UIGridScrollBar : UIElement
	{
		/// <summary>目标网格(它提供滚动比例与可见比例)。</summary>
		public UIItemGrid Target;

		private bool _dragging;

		public UIGridScrollBar()
		{
			Width.Set(8f, 0f);
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (Target == null || Target.PageCount <= 1 || Height.Pixels <= 4f)
			{
				_dragging = false;
				return;
			}

			if (ContainsPoint(Main.MouseScreen) && Main.mouseLeft)
			{
				_dragging = true;
			}

			if (!Main.mouseLeft)
			{
				_dragging = false;
			}

			if (_dragging)
			{
				CalculatedStyle dims = GetDimensions();
				Target.ScrollToRatio(MathHelper.Clamp(
					(Main.MouseScreen.Y - dims.Y) / Math.Max(1f, dims.Height), 0f, 1f));

				Main.LocalPlayer.mouseInterface = true;
			}
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var track = new Rectangle((int)dims.X, (int)dims.Y, (int)dims.Width, (int)dims.Height);

			UITheme.FillRounded(spriteBatch, track, UITheme.Field, 3);

			if (Target == null || Target.PageCount <= 1)
			{
				return;
			}

			float ratio = Target.ScrollRatio;
			int thumbH = Math.Max(18, (int)(track.Height * Target.VisibleRatio));
			int thumbY = track.Y + (int)((track.Height - thumbH) * ratio);

			UITheme.FillRounded(spriteBatch,
				new Rectangle(track.X + 1, thumbY, track.Width - 2, thumbH),
				_dragging ? UITheme.AccentHot : UITheme.Accent, 3);
		}
	}
}
