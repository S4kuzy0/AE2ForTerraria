using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
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
	/// 合成终端右侧面板:物品详情 + 配方材料 + 缺料清单(带获取途径与时间点)。
	///
	/// 以前是一坨等高、同色的纯文本,分不出"哪一行是标题、哪一行缺料"。
	/// 现在每一行带**类型**(分组标题 / 正常 / 齐全 / 缺料),缺料行的左侧还有
	/// 绿 / 红状态点、右侧右对齐灰字(时间点 + 获取途径)。
	/// 内容比面板高时可以用滚轮翻。
	/// </summary>
	public sealed class UIRecipePanel : UIElement
	{
		private enum LineKind
		{
			Normal,
			Section,
			Good,
			Bad,
			Muted
		}

		private sealed class Line
		{
			public string Text;
			public string Right;
			public LineKind Kind;
			public Color Dot = Color.Transparent;
            public int ItemType;
		}

		private const float LineHeight = 21f;
		private const float TextScale = 0.8f;

		private readonly List<Line> _lines = new List<Line>();
        private readonly List<(Line Source, string Text, bool First)> _wrappedLines = new();
        private float _wrappedWidth = -1;
		private string _title = ModText.Safe("LblPickItem", "点击左侧物品查看详情");
		private Color _titleColor = UITheme.Text;
		private string _subtitle = string.Empty;

		private int _scroll;
        public int ScrollOffset { get => _scroll; set => _scroll = Math.Max(0, value); }

        private TerminalCraftingAvailability _availability => TerminalCraftingCacheSystem.Current;
        private long _quantity = 1;

		public void Show(ItemKey key, long quantity = 1)
		{
			_quantity = Math.Max(1,quantity);
            _lines.Clear();
            _wrappedWidth = -1;
			_scroll = 0;
			_titleColor = UITheme.Text;
			_subtitle = string.Empty;

			if (key.IsAir)
			{
				_title = ModText.Get("LblPickItem", "点击左侧物品查看详情");
				return;
			}

			_title = Lang.GetItemNameValue(key.Type);
			Item sample = ContentSamples.ItemsByType[key.Type];
			_titleColor = RarityColor(sample.rare);

			// 词缀名拼在副标题里,不然"传奇 泰拉刃"和"泰拉刃"看起来一模一样
			string prefix = UIItemNames.PrefixName(key.Prefix);
			if (!string.IsNullOrEmpty(prefix))
			{
				_subtitle = prefix;
			}

			// 模组来源(35 个模组的环境里同名物品很常见)
			string modName = UIItemNames.ModNameOf(key.Type);
			if (!string.IsNullOrEmpty(modName))
			{
				_subtitle = string.IsNullOrEmpty(_subtitle)
					? modName
					: _subtitle + "   ·   " + modName;
			}

			Add(LineKind.Normal, ModText.Get("LblNetStored", "网络存储") + ": "
				+ UIFormat.Count(UISystem.GetStoredAmount(key)),
				ModText.Get("LblRarity", "稀有度") + ": " + sample.rare
				+ "   " + ModText.Get("LblMaxStack", "最大堆叠") + ": " + sample.maxStack);

			if (StorageSystem.Progression != null)
			{
				int tier = StorageSystem.Progression.GetTierIndex(key);
				Add(LineKind.Normal, ModText.Get("LblTier", "时间点") + ": "
					+ StorageSystem.Progression.GetTierName(tier));
			}

			if (StorageSystem.Acquisition != null)
			{
				string describe = StorageSystem.Acquisition.Describe(key);
				if (!string.IsNullOrEmpty(describe))
				{
					Add(LineKind.Normal, ModText.Get("LblAcquire", "获取途径") + ": " + describe);
				}
			}

			AppendRecipeInfo(key);
			AppendPlanInfo(key);
		}

		private void Add(LineKind kind, string text, string right = null, Color dot = default, int itemType = 0)
		{
			_lines.Add(new Line
			{
				Kind = kind,
				Text = text,
				Right = right,
				Dot = dot,
                ItemType = itemType
			});
		}

		/// <summary>分组标题:画一条分隔线 + 灰色小字。</summary>
		private void AddSection(string text)
		{
			Add(LineKind.Section, text);
		}

		private void AppendRecipeInfo(ItemKey key)
		{
			if (StorageSystem.Recipes == null)
			{
				return;
			}

			IReadOnlyList<int> recipes = StorageSystem.Recipes.GetRecipesFor(key);
			if (recipes.Count == 0)
			{
				Add(LineKind.Muted, ModText.Get("LblNoRecipe", "没有配方 —— 只能通过获取途径得到"));
				return;
			}

			Add(LineKind.Normal, ModText.Get("LblRecipeCount", "配方数量") + ": " + recipes.Count);

			Recipe first = Main.recipe[recipes[0]];
			if (first == null)
			{
				return;
			}

			AddSection("参考配方材料 · 左键查来源，右键查用途");

			foreach (Item ingredient in first.requiredItem)
			{
				if (ingredient == null || ingredient.type <= 0 || ingredient.stack <= 0)
				{
					continue;
				}

				// 状态点:网络里够不够这一项。缺的那几项一眼就能看出来。
				long stored = UISystem.GetStoredAmount(ItemKey.Crafted(ingredient.type));
				bool enough = stored >= ingredient.stack;

				Add(LineKind.Normal,
					Lang.GetItemNameValue(ingredient.type) + " x" + ingredient.stack,
					UIFormat.Count(stored) + "/" + ingredient.stack,
					enough ? UITheme.Ok : UITheme.Danger, ingredient.type);
			}

			var stations = RecipeStation.GetRequiredStations(first);
			if (stations.Count == 0)
			{
				Add(LineKind.Muted, ModText.Get("LblNeedStation", "需要工作站") + ": "
					+ ModText.Get("LblNoStation", "无(徒手可做)"));
			}
			else
			{
				var names = new List<string>();
				foreach (int tile in stations)
				{
					names.Add(RecipeStation.DescribeStation(tile));
				}

				Add(LineKind.Muted, ModText.Get("LblNeedStation", "需要工作站") + ": "
					+ string.Join(" / ", names));
			}
		}

		private void AppendPlanInfo(ItemKey key)
		{
			if (StorageSystem.Planner == null || UISystem.ActiveNetworkContents == null)
			{
				return;
			}

            // Quantity, locked stock, stations and arbitrary mod conditions must
            // match the catalogue and executor, even when stock has not changed.
            CraftPlan plan = _availability.Plan(key, _quantity);

			AddSection("本次新增合成 " + _quantity + " 件 · 已有库存保留");
            foreach (string blocked in plan.BlockedReasons) Add(LineKind.Bad,blocked);
            if (plan.Limited) Add(LineKind.Bad,"规划达到复杂度上限，请拆分任务");
            if (Main.netMode == NetmodeID.MultiplayerClient && UISystem.ActiveCraftingTerminal == null && UISystem.ActiveAssembler == null)
                Add(LineKind.Muted,"服务器将在执行前复核工作站与环境条件");
            if (plan.FullySatisfied)
            {
				Add(LineKind.Good, "材料试算齐全；执行前还会检查产物容量");
                foreach (var output in plan.Outputs)
                    Add(LineKind.Normal,"产出 / 余料: " + UIItemNames.Display(output.Key) + " ×" + output.Value, itemType:output.Key.Type);

				// 合成步骤预览(以前 CraftPlan.Steps 填了但没人读)
				if (plan.Steps.Count > 0)
				{
					AddSection(ModText.Get("LblCraftSteps", "合成步骤") + " (" + plan.Steps.Count + ")");

					foreach (string step in plan.Steps)
					{
						Add(LineKind.Normal, step, null, UITheme.Ok);
					}
				}

				return;
			}

			AddSection(ModText.Get("LblMissing", "缺料"));

			foreach (MissingIngredient missing in plan.Missing)
			{
				// 右侧:时间点 + 获取途径(以前这两段挤在同一行正文里,长一点就被截断)
				string tier = missing.TierIndex >= 0 && StorageSystem.Progression != null
					? StorageSystem.Progression.GetTierName(missing.TierIndex)
					: string.Empty;
				string acquisition = missing.Acquisition ?? string.Empty;
				string right = string.IsNullOrEmpty(tier) ? acquisition : tier;

				Add(LineKind.Bad, missing.ItemName + " x" + missing.Amount, right, UITheme.Danger, missing.Key.Type);

				if (!string.IsNullOrEmpty(tier) && !string.IsNullOrEmpty(acquisition))
				{
					Add(LineKind.Muted, "    " + ModText.Get("LblAcquire", "获取途径") + ": " + acquisition);
				}
			}
		}

        private void EnsureWrapped()
        {
            float width = Math.Max(24, GetDimensions().Width - 36);
            if (_wrappedWidth == width) return;
            _wrappedWidth = width; _wrappedLines.Clear();
            foreach (var line in _lines)
            {
                string full = string.IsNullOrEmpty(line.Right) ? line.Text : line.Text + " · " + line.Right;
                bool first = true;
                foreach (string paragraph in (full ?? "").Split('\n'))
                {
                    string rest = paragraph;
                    do
                    {
                        int length = rest.Length;
                        float scale = line.Kind == LineKind.Section ? .72f : TextScale;
                        while (length > 1 && FontAssets.MouseText.Value.MeasureString(rest.Substring(0, length)).X * scale > width) length--;
                        _wrappedLines.Add((line, rest.Substring(0, length), first)); first = false;
                        rest = rest.Substring(length);
                    } while (rest.Length > 0);
                }
            }
        }
        private static float WrappedHeight((Line Source, string Text, bool First) line) => line.Source.Kind == LineKind.Section && line.First ? LineHeight + 6f : LineHeight;
		private Line HoveredLine()
        {
            EnsureWrapped();
            var dims=GetDimensions();
            float top=dims.Y+10f+22f+(string.IsNullOrEmpty(_subtitle)?0f:16f)+6f;
            if (!ContainsPoint(Main.MouseScreen) || Main.mouseY < top) return null;
            float y=top-_scroll;
            foreach (var line in _wrappedLines)
            {
                float height=WrappedHeight(line);
                if (Main.mouseY>=y && Main.mouseY<y+height) return line.Source;
                y+=height;
            }
            return null;
        }
        public UIRecipePanel()
        {
            OnLeftClick += (_,_) => { var line=HoveredLine(); if (line?.ItemType>0) UISystem.OpenRecipe(line.ItemType,false); };
            OnRightClick += (_,_) => { var line=HoveredLine(); if (line?.ItemType>0) UISystem.OpenRecipe(line.ItemType,true); };
        }
        public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (!ContainsPoint(Main.MouseScreen))
			{
				return;
			}

			var hovered = HoveredLine();
            if (hovered?.ItemType > 0) BrowserHover.Item(ItemKey.Crafted(hovered.ItemType));
            // 滚轮翻详情(长缺料清单以前会被直接截掉)
			int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
			if (wheel != 0)
			{
				_scroll -= wheel * (int)LineHeight * 2;
				_scroll = Math.Clamp(_scroll, 0, MaxScroll());
				Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
			}
		}

		private int MaxScroll()
		{
            EnsureWrapped();
			CalculatedStyle dims = GetDimensions();
			float content = 38f + (string.IsNullOrEmpty(_subtitle)?0f:16f);
            foreach (var line in _wrappedLines) content += WrappedHeight(line);
			return Math.Max(0, (int)(content - dims.Height + 12f));
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
            EnsureWrapped();
            _scroll = Math.Clamp(_scroll, 0, MaxScroll());
			CalculatedStyle dims = GetDimensions();

			UITheme.FillRounded(spriteBatch, dims.ToRectangle(), UITheme.Panel * 0.94f, 4);
			UITheme.Outline(spriteBatch, dims.ToRectangle(), UITheme.Border);

			float x = dims.X + 10f;
			float y = dims.Y + 10f;

			Utils.DrawBorderString(spriteBatch, Clip(_title, 0.95f, dims.Width - 20f),
				new Vector2(x, y), _titleColor, 0.95f);
			y += 22f;

			if (!string.IsNullOrEmpty(_subtitle))
			{
				Utils.DrawBorderString(spriteBatch, Clip(_subtitle, 0.72f, dims.Width - 20f),
					new Vector2(x, y), UITheme.Muted, 0.72f);
				y += 16f;
			}

			// 标题和正文之间一条分隔线
			UITheme.Fill(spriteBatch,
				new Rectangle((int)x, (int)y, (int)Math.Max(1f, dims.Width - 20f), 1), UITheme.Divider);
			y += 6f;

			float bodyTop = y;
            y -= _scroll;

			foreach (var wrapped in _wrappedLines)
			{
                Line line = wrapped.Source;
				// 滚动后超出面板的部分直接跳过(但仍然要占高度)
				if (y > dims.Y + dims.Height - 4f)
				{
					break;
				}

				if (y >= bodyTop && y + LineHeight <= dims.Y + dims.Height)
				{
                    if (line.Kind == LineKind.Section && wrapped.First)
                        UITheme.Fill(spriteBatch, new Rectangle((int)x, (int)y + 2, (int)Math.Max(1, dims.Width - 20), 1), UITheme.Divider);
                    Color color = line.Kind switch { LineKind.Section => UITheme.Info, LineKind.Good => UITheme.Ok, LineKind.Bad => UITheme.Danger, LineKind.Muted => UITheme.Muted, _ => UITheme.Text };
                    if (wrapped.First && line.Dot != Color.Transparent) UITheme.DrawDot(spriteBatch, (int)x + 3, (int)y + 8, 3, line.Dot);
                    Utils.DrawBorderString(spriteBatch, wrapped.Text, new Vector2(x + 12, y + (line.Kind == LineKind.Section && wrapped.First ? 6 : 0)), color, line.Kind == LineKind.Section ? .72f : TextScale);
				}

				y += WrappedHeight(wrapped);
			}

			if (MaxScroll() > 0)
			{
				// 有内容被滚出去时,右下角给个提示
				UITheme.DrawTriangleDown(spriteBatch, (int)(dims.X + dims.Width - 14f),
					(int)(dims.Y + dims.Height - 16f), UITheme.Muted);
			}
		}

		/// <summary>按像素宽度截断(以前按 28 个字符,中文 14 个字就被砍了)。</summary>
		private static string Clip(string text, float scale, float maxWidth)
		{
			if (maxWidth <= 8f) return string.Empty;
            if (string.IsNullOrEmpty(text))
			{
				return text;
			}

			if (FontAssets.MouseText.Value.MeasureString(text).X * scale <= maxWidth)
			{
				return text;
			}

			for (int length = text.Length - 1; length > 1; length--)
			{
				string candidate = text.Substring(0, length) + "…";

				if (FontAssets.MouseText.Value.MeasureString(candidate).X * scale <= maxWidth)
				{
					return candidate;
				}
			}

			return text.Substring(0, 1) + "…";
		}

		private static Color RarityColor(int rarity)
		{
			return UITheme.Rarity(rarity);
		}
	}
}
