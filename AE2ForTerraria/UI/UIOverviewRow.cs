using System;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 网络概览里的一行。
	///
	/// 以前一行是 4 个独立控件(两行 UIText + 一个占用条面板 + 一个隐形点击框),
	/// 位置全靠绝对坐标算 —— 窗口一缩放就容易错位。现在整行自己画:
	/// 左侧强调条 + 隔行底色 + 名字 + 构成色块 + 容量条 + 趋势三角形 + 严重度药丸。
	/// </summary>
	public sealed class UIOverviewRow : UIElement
	{
		public string Name = string.Empty;
		public string Position = string.Empty;
		public int Blocks;
		public long Types;
		public long Items;
		public long UsedBytes;
		public long MaxBytes;

		/// <summary>九类方块的数量:驱动器 / 单元 / 终端 / 装配室 / CPU / 收集器 / 总线 / 桥接 / 线缆。</summary>
		public int[] Composition;

		public string Warning = string.Empty;
		public int Severity;
		public long TrendBytes;
		public long TrendItems;
		public int TrendMinutes;

		public bool Selected;
		public bool Striped;
		public Action OnClick;

		private static readonly Color[] KindColors =
		{
			new Color(96, 148, 232),   // 驱动器
			new Color(111, 198, 232),  // 存储单元
			new Color(111, 208, 140),  // 终端
			new Color(242, 180, 87),   // 装配室
			new Color(206, 150, 245),  // CPU
			new Color(150, 220, 150),  // 收集器
			new Color(232, 150, 190),  // 总线
			new Color(160, 170, 220),  // 桥接
			new Color(130, 140, 160),  // 线缆
		};

		private static string[] _labels;

		private static string[] Labels
		{
			get
			{
				if (_labels == null)
				{
					_labels = new[]
					{
						ModText.Get("LblDrives", "驱动器"),
						ModText.Get("LblUnits", "单元"),
						ModText.Get("LblTerminals", "终端"),
						ModText.Get("LblAssemblers", "装配室"),
						ModText.Get("LblCpus", "CPU"),
						ModText.Get("LblCollectors", "收集器"),
						ModText.Get("LblBuses", "总线"),
						ModText.Get("LblBridges", "桥接"),
						ModText.Get("LblCables", "线缆"),
					};
				}

				return _labels;
			}
		}

		public UIOverviewRow()
		{
			OnLeftClick += (evt, element) => OnClick?.Invoke();
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (ContainsPoint(Main.MouseScreen))
			{
				Main.LocalPlayer.mouseInterface = true;
			}
		}

		/// <summary>0 = 正常,1 = 警告,2 = 严重(存不进 / 没法合成)。</summary>
		public static int SeverityOf(string warning, float ratio, long maxBytes)
		{
			if (maxBytes <= 0)
			{
				return 2;
			}

			if (ratio >= 0.95f)
			{
				return 2;
			}

			if (string.IsNullOrEmpty(warning))
			{
				return 0;
			}

			return 1;
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var rect = dims.ToRectangle();
			bool hover = ContainsPoint(Main.MouseScreen);

			// 斑马纹 + 选中底色 + 悬停提亮
			if (Striped)
			{
				UITheme.Fill(spriteBatch, rect, UITheme.Zebra);
			}

			if (Selected)
			{
				UITheme.FillRounded(spriteBatch, rect, UITheme.Accent * 0.22f, 3);
				UITheme.Fill(spriteBatch, new Rectangle(rect.X, rect.Y + 2, 3, rect.Height - 4),
					UITheme.Accent);
			}
			else if (hover)
			{
				UITheme.FillRounded(spriteBatch, rect,
					UITheme.Blend(UITheme.Accent * 0.35f, UITheme.Accent * 0.5f, 0.5f), 3);
			}

			float x = dims.X + 10f;
			float y = dims.Y + 3f;
			float right = dims.X + dims.Width - 10f;

			float ratio = MaxBytes > 0 && MaxBytes != long.MaxValue
				? MathHelper.Clamp(UsedBytes / (float)MaxBytes, 0f, 1f)
				: 0f;

			Color severityColor = Severity >= 2 ? UITheme.Danger : UITheme.Warn;

			// —— 第一行:名字 + 坐标 / 方块 / 类 / 件 ——
			if (Severity > 0)
			{
				UITheme.DrawDot(spriteBatch, (int)x + 3, (int)y + 7, 3, severityColor);
				x += 12f;
			}

			Vector2 nameSize = Utils.DrawBorderString(spriteBatch, Name, new Vector2(x, y),
				Selected ? UITheme.Text : UITheme.TextDim, 0.8f);

			string meta = Position + "   " + Blocks + " " + ModText.Get("LabelBlocks", "方块")
				+ "   " + Types + " " + ModText.Get("LabelKinds", "类")
				+ "   " + UIFormat.Count(Items) + " " + ModText.Get("LabelItems", "件");

			Utils.DrawBorderString(spriteBatch, meta,
				new Vector2(x + nameSize.X + 10f, y + 1f), UITheme.Muted, 0.7f);

			if (Severity > 0)
			{
				Vector2 warnSize = FontAssets.MouseText.Value.MeasureString(Warning) * 0.7f;
				UITheme.DrawPill(spriteBatch, (int)(right - warnSize.X - 12f), (int)y - 1,
					Warning, severityColor, 0.7f);
			}

			// —— 第二行:方块构成色块 + 趋势 ——
			y = dims.Y + 18f;
			x = dims.X + 10f;

			float reserved = 0f;

			if (TrendMinutes > 0)
			{
				string trend = (TrendBytes >= 0 ? "+" : "") + UIFormat.Count(TrendBytes)
					+ " / " + (TrendItems >= 0 ? "+" : "") + TrendItems
					+ "   " + TrendMinutes + " " + ModText.Get("LblMinutes", "分钟");

				Vector2 trendSize = FontAssets.MouseText.Value.MeasureString(trend) * 0.7f;
				reserved = trendSize.X + 22f;

				bool up = TrendBytes >= 0;
				Color trendColor = up ? UITheme.Ok : UITheme.Warn;

				if (up)
				{
					UITheme.DrawTriangleUp(spriteBatch, (int)(right - trendSize.X - 12f), (int)y + 6,
						trendColor);
				}
				else
				{
					UITheme.DrawTriangleDown(spriteBatch, (int)(right - trendSize.X - 12f), (int)y + 6,
						trendColor);
				}

				Utils.DrawBorderString(spriteBatch, trend, new Vector2(right - trendSize.X, y + 1f),
					trendColor, 0.7f);
			}

			if (Composition != null)
			{
				int hidden = 0;

				for (int i = 0; i < Composition.Length && i < KindColors.Length; i++)
				{
					if (Composition[i] <= 0)
					{
						continue;
					}

					string entry = Labels[i] + " " + Composition[i];
					Vector2 size = FontAssets.MouseText.Value.MeasureString(entry) * 0.7f;

					// 放不下就不再往下画,末尾用「+N」收尾
					if (x + 14f + size.X > right - reserved)
					{
						hidden++;
						continue;
					}

					UITheme.DrawSwatch(spriteBatch, (int)x, (int)y + 3, KindColors[i], 7);
					Utils.DrawBorderString(spriteBatch, entry, new Vector2(x + 11f, y + 1f),
						UITheme.TextDim, 0.7f);

					x += 14f + size.X + 12f;
				}

				if (hidden > 0)
				{
					Utils.DrawBorderString(spriteBatch, "+" + hidden, new Vector2(x, y + 1f),
						UITheme.Muted, 0.7f);
				}
			}

			// —— 第三行:容量条 ——
			y = dims.Y + 33f;

			if (MaxBytes == long.MaxValue)
			{
				Utils.DrawBorderString(spriteBatch, ModText.Get("LabelCapacity", "容量") + " · "
					+ ModText.Get("LabelUsed", "已用") + " " + UIFormat.Count(UsedBytes) + " "
					+ ModText.Get("LabelBytes", "字节") + " · " + ModText.Get("DigitalSingularityCapacity", "数字奇点"),
					new Vector2(x, y), UITheme.TextDim, 0.7f);
			}
			else if (MaxBytes > 0)
			{
				string text = UIFormat.Count(UsedBytes) + " / " + UIFormat.Count(MaxBytes)
					+ " " + ModText.Get("LabelBytes", "字节")
					+ "  (" + (int)(ratio * 100f) + "%)";

				Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * 0.7f;
				Color barColor = UITheme.CapacityColor(ratio);

				UITheme.Bar(spriteBatch,
					new Rectangle((int)x, (int)y + 2, Math.Max(60, (int)(right - x - size.X - 10f)), 7),
					ratio, barColor);

				Utils.DrawBorderString(spriteBatch, text,
					new Vector2(right - size.X, y), ratio >= 0.95f ? UITheme.Danger : UITheme.TextDim,
					0.7f);
			}
			else
			{
				Utils.DrawBorderString(spriteBatch, ModText.Get("StatusNoCapacity", "无容量"),
					new Vector2(x, y), UITheme.Warn, 0.7f);
			}
		}
	}
}
