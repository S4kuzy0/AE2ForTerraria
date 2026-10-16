using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// JEI 式的配方覆盖窗口:一次显示一条配方,◀ ▶ 翻页。
	/// 来源 = 所有能做出它的配方 + 微光/宝藏袋/怪物掉落/商店等;
	/// 用途 = 所有消耗它的配方 + 其它用途(例如拿去召唤 Boss)。
	/// 点配方里的任何材料会跳到那个物品。
	/// </summary>
	public sealed class RecipeOverlayUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = WindowGeometry.Compact();

		private const int IngredientSize = 40;
		private const int IngredientStep = 46;
		private const int OutputSize = 56;

		private sealed class Entry
		{
			public bool IsRecipe;
			public int RecipeIndex;
			public string Text = string.Empty;
		}

		private readonly List<Entry> _entries = new List<Entry>();
		private readonly List<UIElement> _spawned = new List<UIElement>();

		private int _target;
		private bool _uses;
		private int _page;

		private UIText _title;
		private UIText _subtitle;
		private UIText _detail;
		private UIText _pageLabel;
		private UIIconButton _prev;
		private UIIconButton _next;

		public override void OnInitialize()
		{
			InitWindow(Geometry, string.Empty);

			_title = new UIText(string.Empty, 1f, false);
			Window.Append(_title);

			_subtitle = new UIText(string.Empty, 0.78f, false);
			Window.Append(_subtitle);

			_detail = new UIText(string.Empty, 0.8f, false);
			Window.Append(_detail);

			_pageLabel = new UIText(string.Empty, 0.78f, false);
			Window.Append(_pageLabel);

			_prev = new UIIconButton(UIIconButton.IconKind.Left);
			_prev.OnLeftClick += (evt, element) => Turn(-1);
			Window.Append(_prev);

			_next = new UIIconButton(UIIconButton.IconKind.Right);
			_next.OnLeftClick += (evt, element) => Turn(1);
			Window.Append(_next);

			RequestLayout();
		}

		public void Show(int itemType, bool uses)
		{
			GameDataIndex.EnsureBuilt();
			_target = itemType;
			_uses = uses;
			_page = 0;
			BuildEntries();
			Refresh();
		}

		private void BuildEntries()
		{
			_entries.Clear();

			if (_target <= 0)
			{
				return;
			}

			var key = ItemKey.Crafted(_target);

			if (StorageSystem.Recipes != null)
			{
				IReadOnlyList<int> recipes = _uses
					? StorageSystem.Recipes.GetRecipesUsing(_target)
					: StorageSystem.Recipes.GetRecipesFor(key);

				foreach (int recipeIndex in recipes)
				{
					_entries.Add(new Entry { IsRecipe = true, RecipeIndex = recipeIndex });
				}
			}

			if (StorageSystem.Acquisition != null)
			{
				IReadOnlyList<string> extras = _uses
					? StorageSystem.Acquisition.GetUses(key)
					: StorageSystem.Acquisition.GetSources(key);

				foreach (string text in extras)
				{
					_entries.Add(new Entry { Text = text });
				}

				// 「宝藏袋里能开出什么」:这个物品本身是袋子 / 宝箱时,
				// 把里面的内容也当成一条来源条目显示出来。
				if (!_uses)
				{
					IReadOnlyList<int> contents = StorageSystem.Acquisition.GetTreasureBagContents(_target);
					if (contents.Count > 0)
					{
						var parts = new List<string>();
						int shown = 0;

						foreach (int content in contents)
						{
							if (content <= 0)
							{
								continue;
							}

							if (shown++ >= 12)
							{
								parts.Add(ModText.Format("LblMoreEntries", "…还有 {0} 件",
									contents.Count - 12));
								break;
							}

							parts.Add(Lang.GetItemNameValue(content));
						}

						_entries.Add(new Entry
						{
							Text = ModText.Get("LblTreasureBag", "—— 宝藏袋 / 宝箱内容 ——")
								+ "\n" + string.Join("、", parts)
						});
					}
				}
			}
		}

		private void Turn(int delta)
		{
			if (_entries.Count == 0)
			{
				return;
			}

			_page += delta;
			if (_page < 0)
			{
				_page = _entries.Count - 1;
			}
			else if (_page >= _entries.Count)
			{
				_page = 0;
			}

			Refresh();
		}

		private void Refresh()
		{
			foreach (UIElement element in _spawned)
			{
				element.Remove();
			}

			_spawned.Clear();

			if (_target <= 0)
			{
				_title.SetText(ModText.Get("LblNoItemSelected", "未选择物品"));
				_subtitle.SetText(string.Empty);
				_detail.SetText(string.Empty);
				_pageLabel.SetText(string.Empty);
				return;
			}

			string name = Lang.GetItemNameValue(_target);
			_title.SetText((_uses
				? ModText.Get("LblUsesShort", "【用途】")
				: ModText.Get("LblSourcesShort", "【来源】")) + name);

			if (_entries.Count == 0)
			{
				_subtitle.SetText(_uses
					? ModText.Get("LblNoUses", "暂无已知用途")
					: ModText.Get("LblNoSources", "暂无已知来源"));
				_detail.SetText(_uses
					? ModText.Get("HintNoUses", "可能只是材料 / 装饰,或用于其它模组的机制")
					: ModText.Get("HintNoSources", "只能通过怪物掉落 / 商店 / 微光 / 宝藏袋等方式获得"));
				_pageLabel.SetText(string.Empty);
				return;
			}

			_page = Math.Max(0, Math.Min(_page, _entries.Count - 1));
			Entry entry = _entries[_page];
			_pageLabel.SetText((_page + 1) + " / " + _entries.Count);

			if (!entry.IsRecipe)
			{
				_subtitle.SetText(_uses
					? ModText.Get("LblUseEntry", "其它用途")
					: ModText.Get("LblSourceEntry", "非合成来源"));
				_detail.SetText(entry.Text);
				return;
			}

			Recipe recipe = entry.RecipeIndex >= 0 && entry.RecipeIndex < Recipe.numRecipes
				? Main.recipe[entry.RecipeIndex]
				: null;

			if (recipe == null)
			{
				_subtitle.SetText(ModText.Get("LblRecipeFailed", "配方读取失败"));
				_detail.SetText(string.Empty);
				return;
			}

			_subtitle.SetText(_uses
				? ModText.Get("LblRecipeConsumes", "这条配方会消耗它")
				: ModText.Get("LblRecipeMakes", "这条配方能做出它"));
			BuildRecipeCard(recipe);
		}

		private void BuildRecipeCard(Recipe recipe)
		{
			// 材料:列数跟着窗口宽度走(以前固定 3 列,材料多的配方会挤成一团)
			float areaLeft = Pad + 16f;
			float areaWidth = Math.Max(180f, Geo.W - areaLeft - 150f);
			int columns = Math.Max(2, Math.Min(8, (int)(areaWidth / IngredientStep)));

			// 纵向能放几行:从 96 往下到详情文字上方
			float areaHeight = Math.Max(60f, Geo.H - 96f - 120f);
			int maxRows = Math.Max(1, (int)(areaHeight / IngredientStep));
			int maxSlots = columns * maxRows;

			var ingredients = new List<Item>();
			foreach (Item ingredient in recipe.requiredItem)
			{
				if (ingredient != null && ingredient.type > 0 && ingredient.stack > 0)
				{
					ingredients.Add(ingredient);
				}
			}

			int slot = 0;
			foreach (Item ingredient in ingredients)
			{
				if (slot >= maxSlots)
				{
					break;
				}

				UIItemIcon icon = UIItemIcon.Create(ingredient.type, IngredientSize, OpenIngredient);
				Window.Append(icon);
				_spawned.Add(icon);
				icon.Left.Set(areaLeft + slot % columns * IngredientStep, 0f);
				icon.Top.Set(96f + slot / columns * IngredientStep, 0f);
				slot++;
			}

			UIItemIcon output = UIItemIcon.Create(recipe.createItem.type, OutputSize, OpenIngredient);
			Window.Append(output);
			_spawned.Add(output);
			output.Left.Set(Geo.W - 120f, 0f);
			output.Top.Set(110f, 0f);

			var stations = RecipeStation.GetRequiredStations(recipe);
			string stationText = stations.Count == 0
				? ModText.Get("LblHandCraft", "徒手可做")
				: ModText.Get("LblNeedStation", "需要工作站") + ":" + JoinStationNames(stations);

			string outText = ModText.Get("LblOutput", "产出") + ":"
				+ Lang.GetItemNameValue(recipe.createItem.type)
				+ " x" + recipe.createItem.stack;

			// 这个物品在这一条配方里的时间点(阶段)
			string tierLine = string.Empty;
			if (StorageSystem.Progression != null)
			{
				int tier = StorageSystem.Progression.GetTierIndex(key: ItemKey.Crafted(_target));
				tierLine = "\n" + ModText.Get("LblTier", "时间点") + ": "
					+ StorageSystem.Progression.GetTierName(tier);
			}

			// 稀有度 / 最大堆叠,JEI 的配方页也会给这些
			string sampleLine = string.Empty;
			try
			{
				Item sample = Terraria.ID.ContentSamples.ItemsByType[_target];
				sampleLine = "\n" + ModText.Get("LblRarity", "稀有度") + ": " + sample.rare
					+ "   " + ModText.Get("LblMaxStack", "最大堆叠") + ": " + sample.maxStack;
			}
			catch
			{
				// 取不到样例就跳过这一行
			}

			string moreLine = ingredients.Count > slot
				? "\n" + ModText.Format("LblMoreIngredients",
					"…还有 {0} 种材料(放大窗口可显示更多)", ingredients.Count - slot)
				: string.Empty;

			_detail.SetText(outText + sampleLine + tierLine + "\n" + stationText + moreLine
				+ "\n" + ModText.Get("HintClickIngredient", "(点材料可跳到那个物品)"));
		}

		private static string JoinStationNames(List<int> stations)
		{
			var names = new List<string>();
			foreach (int tile in stations)
			{
				names.Add(RecipeStation.DescribeStation(tile));
			}

			return string.Join(" / ", names);
		}

		private void OpenIngredient(int itemType)
		{
			if (itemType > 0)
			{
				Show(itemType, false);
			}
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			float w = Geo.W;
			float h = Geo.H;

			_title.Left.Set(Pad + 16f, 0f);
			_title.Top.Set(34f, 0f);

			_subtitle.Left.Set(Pad + 16f, 0f);
			_subtitle.Top.Set(62f, 0f);

			_detail.Left.Set(Pad + 16f, 0f);
			_detail.Top.Set(h - 110f, 0f);

			_pageLabel.Left.Set(w - 160f, 0f);
			_pageLabel.Top.Set(h - 44f, 0f);

			_prev.Left.Set(w - 108f, 0f);
			_prev.Top.Set(h - 48f, 0f);
			_prev.Width.Set(34f, 0f);
			_prev.Height.Set(30f, 0f);

			_next.Left.Set(w - 66f, 0f);
			_next.Top.Set(h - 48f, 0f);
			_next.Width.Set(34f, 0f);
			_next.Height.Set(30f, 0f);

			Window.Recalculate();

			// 尺寸变了要重排配方卡片
			Refresh();
		}
	}
}
