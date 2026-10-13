using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 物品、配方、NPC 查询。紧凑工具栏与分类在上，点击条目打开覆盖详情。
	/// 空白区域及四边拖动窗口，只有右下角斜线手柄缩放；输入沿用游戏原生 IME。
	/// </summary>
	public sealed class SearchUIState : MovableWindowState
	{
		private enum SortMode
		{
			Default,
			Tier,
			Name,
			Recent, Value, Rarity, Damage, Defence, UseTime, Pick, Axe, Hammer, Fishing, Bait, Tile, WingFlight
		}

		private enum Category
		{
			Item,
            Recipes,
			Npc
		}

		private static readonly WindowGeometry Geometry =
			new WindowGeometry(900f, 680f, 560f, 430f);

		// —— 版式 ——
		/// <summary>第一行(分类标签 / 排序 / 收藏 / 重置 / 关闭)。</summary>
		private const float HeaderTop = Pad;
		private const float HeaderHeight = 26f;
		private const float SearchTop = HeaderTop + HeaderHeight + 6f;
		private const float SearchHeight = 28f;
		private const float GridTop = SearchTop + SearchHeight + 5f;

		private UIUnderlineTab _itemTab;
		private UIUnderlineTab _npcTab;
        private UIUnderlineTab _recipeTab;
        private UIBrowserCategoryBar _categories;
        private UIBrowserDetails _details;
        private UIButton _modButton, _filterButton;
        private string _modFilter;
        private int _filter;
        private int _dropQuery, _rightType;
        private double _rightAt = -10;
        private UIButton _clearDrop;
        private bool IsFavorite(int type) => BrowserFavorites.Contains(type, _category == Category.Npc);

		// 搜索框完全自己做:一个面板当外框,一个 UIText 显示查询串。
		private UISearchField _searchPanel;
		private UIText _searchText;
		private string _query = string.Empty;
		private UIButton _sortButton;
		private UIIconButton _wrench;
		private UIButton _starButton;
		private bool _favoritesOnly;
		
		private readonly List<int> _recent = new List<int>();
		private UIButton _helpButton;
		private UIText _status;
		private UIText _statusHint;
		private UIItemGrid _grid;
		private UIGridScrollBar _scrollBar;

		private readonly List<int> _allItems = new List<int>();
		private string[] _itemMods = Array.Empty<string>();
		private readonly List<int> _filtered = new List<int>();

		private Category _category = Category.Item;
		private SortMode _sort = SortMode.Default;
		private bool _built;

		private bool _typing;
		private bool _cheatMode;
		private bool _prevMouseLeft;
		private bool _wrenchAttached;
		private static bool _loggedOpen;

		/// <summary>光标位置(0.._query.Length)。</summary>
		private int _caret;

		/// <summary>Ctrl+A 之后置位:下一次输入 / 删除会先把整串清掉。</summary>
		private bool _selectAll;

		private static KeyboardState _prevKeys;

		private static void LogOnce(string message)
		{
			if (_loggedOpen || !MEConfig.DiagnosticsEnabled)
			{
				return;
			}

			_loggedOpen = true;
			Terraria.ModLoader.ModContent.GetInstance<AE2ForTerraria>()?.Logger.Info("[诊断] " + message);
		}

		public override bool SearchBoxFocused => _typing;
		protected override bool CornerResizeOnly => true;
		protected override bool HighlightFrame => false;
		protected override float ResetButtonWidth => 48f;
		protected override float ChromeGap => 4f;
		protected override float FramelessChromeHeight => HeaderHeight;

		public override void OnInitialize()
		{
			// Keep the existing preference key so a UI update preserves the player's layout.
			Geometry.Key = "SearchUIState.v3";

			// 没有标题栏:**任意空位**按住左键就能拖(见 UIWindowPanel)
			InitWindow(Geometry, ModText.Get("TitleItemBrowser", "物品浏览器"), frameless: true);

			_itemTab = new UIUnderlineTab(ModText.Get("TabItems", "物品"), 0.85f);
			_itemTab.OnLeftClick += (evt, element) => SwitchCategory(Category.Item);
			Window.Append(_itemTab);

			_npcTab = new UIUnderlineTab(ModText.Get("TabNpcs", "怪物"), 0.85f);
			_npcTab.OnLeftClick += (evt, element) => SwitchCategory(Category.Npc);
			Window.Append(_npcTab);
            _npcTab.SetText("怪物 / NPC");
            _recipeTab = new UIUnderlineTab("配方", .85f);
            _recipeTab.OnLeftClick += (_, _) => SwitchCategory(Category.Recipes);
            Window.Append(_recipeTab);
            Window.BackgroundColor = new Color(42, 54, 87);
            _categories = new UIBrowserCategoryBar { Compact = true };
            _categories.Changed = () => { ApplyLayout(); Refresh(); };
            Window.Append(_categories);
            _details = new UIBrowserDetails(Window);
            _details.FavoriteChanged = Refresh;
            _modButton = new UIButton("全部模组");
            _modButton.OnLeftClick += (_, _) => ShowModMenu();
            Window.Append(_modButton);
            _filterButton = new UIButton("筛选");
            _filterButton.OnLeftClick += (_, _) => UIChoiceMenu.Show(Window, _filterButton,
                _category == Category.Npc ? new[] { "全部", "有掉落物" } : new[] { "全部", "材料", "有配方", "有掉落来源", "时装", "非时装" },
                (index, label) => { _filter = index; _filterButton.Selected = index != 0; Refresh(); });
            Window.Append(_filterButton);
            _clearDrop = new UIButton("清除反查");
            _clearDrop.OnLeftClick += (_, _) => { _dropQuery = 0; _clearDrop.Remove(); Refresh(); };

			// 圆角输入框:框内左侧放大镜,聚焦时描边变强调色
			_searchPanel = new UISearchField { OverflowHidden = true };
			_searchPanel.OnLeftClick += (evt, element) =>
			{
				_typing = true;
				UpdateSearchText();
				UpdateStatus();
			};
			Window.Append(_searchPanel);

			_searchText = new UIText(string.Empty, 0.85f, false) { TextOriginX = 0 };
			_searchPanel.Append(_searchText);
			UpdateSearchText();

			// 帮助:**单独开一个子页面**
			_helpButton = new UIButton("帮助", 0.78f, false);
			_helpButton.OnLeftClick += (evt, element) =>
			{
				_typing = false;
				UISystem.OpenHelp();
			};
			Window.Append(_helpButton);

			// 收藏:★ 只显示收藏,Ctrl+左键点格子切换收藏
			_starButton = new UIButton("收藏");
			_starButton.OnLeftClick += (evt, element) =>
			{
				_favoritesOnly = !_favoritesOnly;
				_starButton.Selected = _favoritesOnly;
				Refresh();
			};
			Window.Append(_starButton);

			_sortButton = new UIButton(SortLabel(), 0.78f, false);
			_sortButton.OnLeftClick += (evt, element) =>
			{
				_typing = false;
				CycleSort();
			};
			Window.Append(_sortButton);

			_grid = new UIItemGrid { CellSize = 43, NpcLabels = true, InputBlocked = () => _details.BlocksPointer || UIChoiceMenu.IsOpen || UISystem.SecondaryInterface?.CurrentState != null || UISystem.OverlayInterface?.CurrentState != null };
			// 回车 = 打开这一项的来源(和左键同一个动作)
			_grid.OnActivated = key =>
			{
				if (!key.IsAir)
				{
					Activate(key.Type, false);
				}
			};
			_grid.OnLeftClick += (evt, element) => OnGridLeftClick();
            _grid.OnUsed = key => Activate(key.Type, true);
			_grid.OnRightClick += (evt, element) =>
			{
				_typing = false;

				if (_grid.HoveredType > 0)
				{
					int type = _grid.HoveredType;
                    if (_category != Category.Npc && type == _rightType && UIStatus.Now - _rightAt < .35)
                    {
                        SwitchCategory(Category.Npc); _dropQuery = type; Window.Append(_clearDrop); ApplyLayout(); Refresh(); _rightType = 0;
                    }
                    else { Activate(type, true); _rightType = type; _rightAt = UIStatus.Now; }
				}
			};
			Window.Append(_grid);
			AddDragTarget(_categories);
			Window.AdditionalDragTarget = (target, point) => target == _grid
				&& _grid.InputBlocked?.Invoke() != true && !_grid.HasEntryAt(point);

			// 细滚动条:网格右边可以拖
			_scrollBar = new UIGridScrollBar { Target = _grid };
			Window.Append(_scrollBar);

			// 作弊模式:红扳手(点亮 = Shift + 左键直接拿物品)
			_wrench = new UIIconButton(UIIconButton.IconKind.Wrench);
			_wrench.OnLeftClick += (evt, element) => ToggleCheat();
			Window.Append(_wrench);
			ApplyWrenchVisual();
			_wrenchAttached = true;

			_status = new UIText(string.Empty, 0.7f, false);
			_status.TextColor = UITheme.TextDim; _status.TextOriginX = 0;
			Window.Append(_status);

			// 状态拆两行:窗口拉到最小宽度时一整行必然溢出
			_statusHint = new UIText(string.Empty, 0.7f, false);
			_statusHint.TextColor = UITheme.Muted;
			Window.Append(_statusHint);

			// 状态文字那一块看起来就是空位,点它也能拖窗口
			AddDragTarget(_status);
			AddDragTarget(_statusHint);

			ApplyLayout();
		}

		public void ResetForOpen()
		{
			_category = Category.Item;
            _dropQuery = 0; _clearDrop.Remove();
            _categories.SetMode(false); _details.Close(); ApplyLayout(); Refresh();
			_grid.NpcMode = false;
			// Search takes keyboard focus only after an explicit click.
			_typing = false;
			_caret = _query?.Length ?? 0;
			_selectAll = false;
			ApplyTabVisuals();
			UpdateSearchText();

			GameDataIndex.EnsureBuilt();
			LogOnce("浏览器已打开");
		}

		public override void OnDeactivate()
		{
			base.OnDeactivate();
            UIChoiceMenu.Close();
			ReleaseIme();
		}

		/// <summary>关窗口时放开输入法。</summary>
		public static void ReleaseIme()
		{
			ImeInput.Blur();
		}

		/// <summary>模组卸载时调一次,免得重载后输入法状态卡住。</summary>
		public static void ReleaseImeForUnload()
		{
			ImeInput.Blur();
		}

		private void SwitchCategory(Category category)
		{
			_typing = false;

			if (_category == category)
			{
				return;
			}

			_category = category;
            if (category == Category.Npc && _sort != SortMode.Name) { _sort = SortMode.Default; _sortButton.SetText(SortLabel()); }
            _dropQuery = 0; _clearDrop.Remove();
            _filter = 0; _filterButton.Selected = false;
            _categories.SetMode(category == Category.Npc);
            _details.Close();
            ApplyLayout();
			_grid.NpcMode = category == Category.Npc;
			ApplyTabVisuals();
			Refresh();
		}

		private void ApplyTabVisuals()
		{
			// 下划线标签:选中项底下一条强调色,而不是整块变色
			_itemTab.Selected = _category == Category.Item;
			_npcTab.Selected = _category == Category.Npc;
			_itemTab.Refresh();
			_npcTab.Refresh();
            _recipeTab.Selected = _category == Category.Recipes; _recipeTab.Refresh();
		}

		private void ToggleCheat()
		{
			// 配置里没开作弊模式就不允许用
			if (MEConfig.Instance == null || !MEConfig.Instance.CheatModeEnabled)
			{
				Main.NewText(ModText.Get("MsgCheatDisabled",
					"作弊模式已在配置里关闭(设置 → 模组配置 → AE2 for Terraria)"), 255, 200, 150);
				return;
			}

			_typing = false;
			_cheatMode = !_cheatMode;
			ApplyWrenchVisual();

			Main.NewText(_cheatMode
				? ModText.Get("MsgCheatOn", "作弊模式:开(Shift + 左键 直接拿物品)")
				: ModText.Get("MsgCheatOff", "作弊模式:关"), 255, 200, 150);

			UpdateStatus();
		}

		/// <summary>红色 = 作弊模式开着,灰蓝 = 关着。</summary>
		private void ApplyWrenchVisual()
		{
			if (_wrench == null)
			{
				return;
			}

			_wrench.BackgroundColor = _cheatMode
				? new Color(200, 40, 40)
				: UITheme.Btn;
			_wrench.GlyphColor = UITheme.Danger;
		}

		/// <summary>
		/// 按配置决定扳手按钮是否显示。
		/// 配置是服务器端的,开服的人不开作弊模式,客户端就看不到这个按钮。
		/// </summary>
		private void SyncWrenchVisibility()
		{
			bool want = MEConfig.Instance != null && MEConfig.Instance.CheatModeEnabled;
			if (want == _wrenchAttached)
			{
				return;
			}

			if (want)
			{
				Window.Append(_wrench);
			}
			else
			{
				_wrench.Remove();
				_cheatMode = false;
				ApplyWrenchVisual();
			}

			_wrenchAttached = want;
			ApplyLayout();
		}

		private void OnGridLeftClick()
		{
			_typing = false;
			if (Window.Dragged) return;

			if (_grid.HoveredType <= 0)
			{
				return;
			}

			// Ctrl + 左键 = 收藏 / 取消收藏
			if (IsCtrlDown() || Main.keyState.IsKeyDown(Keys.LeftAlt) || Main.keyState.IsKeyDown(Keys.RightAlt))
			{
				int type = _grid.HoveredType;

                BrowserFavorites.Toggle(type, _category == Category.Npc);
                UIStatus.Toast(IsFavorite(type) ? "已收藏" : "已取消收藏");
				Refresh();
				return;
			}

			if (_cheatMode && _category == Category.Item && IsShiftDown())
			{
				GiveItem(_grid.HoveredType);
				return;
			}

			Activate(_grid.HoveredType, false);
		}

		private static bool IsShiftDown()
		{
			return Main.keyState.IsKeyDown(Keys.LeftShift)
				|| Main.keyState.IsKeyDown(Keys.RightShift);
		}

		private static bool IsCtrlDown()
		{
			return Main.keyState.IsKeyDown(Keys.LeftControl)
				|| Main.keyState.IsKeyDown(Keys.RightControl);
		}

		private void GiveItem(int itemType)
		{
			int maxStack = Terraria.ID.ContentSamples.ItemsByType[itemType].maxStack;
			if (maxStack <= 0)
			{
				maxStack = 1;
			}

			Player player = Main.LocalPlayer;
			player.QuickSpawnItem(new EntitySource_Misc("AE2ForTerraria"), itemType, maxStack);

			Main.NewText(ModText.Get("MsgCheatGave", "作弊:获得") + " "
				+ Lang.GetItemNameValue(itemType) + " x" + maxStack, 255, 180, 180);
		}

		private void Activate(int type, bool uses)
		{
			if (type <= 0)
			{
				return;
			}

			RememberRecent(type);

			if (_category == Category.Npc)
			{
				_details.ShowNpc(type);
			}
			else
			{
				ShowDetails(type, uses);
			}
		}

        public void ShowDetails(int type, bool uses = false) { _typing = false; _details.Show(ItemKey.Crafted(type), uses: uses); }
        public void ShowNpcDetails(int type) { _typing = false; _details.ShowNpc(type); }
        public bool CloseDetails() { if (!_details.IsOpen) return false; _details.Close(); return true; }
        private static void Place(UIElement e, float x, float y, float w, float h)
        { e.Left.Set(x, 0); e.Top.Set(y, 0); e.Width.Set(w, 0); e.Height.Set(h, 0); }
        protected override void ApplyLayout()
        {
            base.ApplyLayout();
            if (_categories == null || _grid == null) return;
            float w = Geo.W, h = Geo.H, inner = w - Pad * 2;
            Place(_itemTab, Pad, Pad, 52, 26); Place(_recipeTab, Pad + 56, Pad, 52, 26);
            Place(_npcTab, Pad + 112, Pad, 108, 26);
            float favoriteX = w - Pad - 26 - ChromeGap - ResetButtonWidth - ChromeGap - ResetButtonWidth;
            Place(_starButton, favoriteX, Pad, ResetButtonWidth, HeaderHeight);
            Place(_helpButton, favoriteX - 52, Pad, 48, HeaderHeight);
            Place(_filterButton, favoriteX - 104, Pad, 48, HeaderHeight);
            Place(_searchPanel, Pad, SearchTop, inner - 156, SearchHeight);
            Place(_searchText, 26, 4, inner - 200, 24);
            Place(_modButton, w - Pad - 150, SearchTop, 150, SearchHeight);
            Place(_categories, Pad, GridTop, inner, 24);
            float top = GridTop + _categories.Arrange(inner) + 3;
            float sortX = w - Pad - 104 - (_wrenchAttached ? 110 : 0);
            Place(_status, Pad, top + 5, Math.Max(120, sortX - Pad - 112), 22);
            Place(_clearDrop, sortX - 106, top, 100, 26);
            Place(_sortButton, sortX, top, 104, 26);
            Place(_wrench, w - Pad - 104, top, 104, 26);
            top += 32;
            float gridHeight = Math.Max(43, h - 24 - top);
            Place(_grid, Pad, top, inner - 12, gridHeight);
            Place(_scrollBar, w - Pad - 9, top, 9, gridHeight);
            _statusHint.Remove();
            // Cover the catalogue without rebuilding it, so returning keeps search and scroll.
            _details.SetBounds(Pad, SearchTop, inner, Math.Max(120, h - 24 - SearchTop));
            Window.Recalculate();
        }

		protected override void UpdateWindow(GameTime gameTime)
		{
			if (!_built && !Main.gameMenu)
			{
				BuildLists();
				_built = true;
				Refresh();
			}

			// 窗口内任何一次左键点击都保持输入态(不只是点搜索框)
			bool mouseLeftNow = Main.mouseLeft;
			if (mouseLeftNow && !_prevMouseLeft && Window.ContainsPoint(Main.MouseScreen))
			{
				_typing = !_details.IsOpen && _searchPanel.ContainsPoint(Main.MouseScreen);

				// 刚聚焦时先清掉「上一刻按下的键」,免得打开窗口的那一下按键被当成输入
				Main.clrInput();
				UpdateSearchText();
			}

			_prevMouseLeft = mouseLeftNow;

			// 搜索框聚焦时描边变强调色(一眼看出"现在能打字")
			if (_searchPanel != null)
			{
				_searchPanel.Focused = _typing;
			}

			SyncWrenchVisibility();
			HandleTyping(gameTime);

			// 搜索框在输入时方向键归它(移动光标);否则方向键在网格里选格子
			_grid.KeyboardEnabled = !_typing && !_details.IsOpen;
		}

		/// <summary>
		/// 文本输入。**文字全部交给原版的 `Main.GetInputText`** ——
		/// 中文输入法上屏、打字、退格、Ctrl+V 粘贴、Ctrl+Z 清空都在它里面,
		/// 我们只负责(1)把输入法打开,(2)把原版"总在末尾增删"的变化搬到我们的光标位置上。
		/// </summary>
		private void HandleTyping(GameTime gameTime)
		{
			KeyboardState now = Keyboard.GetState();

			if (!_typing || _searchPanel == null)
			{
				_prevKeys = now;
				ImeInput.Blur();
				return;
			}

			// 输入法打开(和聊天框同一套)
			ImeInput.Focus();

			bool ctrl = now.IsKeyDown(Keys.LeftControl) || now.IsKeyDown(Keys.RightControl);
			bool changed = false;

			string vanilla = Main.GetInputText(_query ?? string.Empty);
			changed |= ApplyVanillaEdit(vanilla);

			// 方向键 / Home / End / Delete 原版不管,自己处理
			foreach (Keys key in now.GetPressedKeys())
			{
				if (_prevKeys.IsKeyDown(key))
				{
					continue;
				}

				switch (key)
				{
					case Keys.Left:
						_caret = Math.Max(0, _caret - 1);
						_selectAll = false;
						break;

					case Keys.Right:
						_caret = Math.Min(_query.Length, _caret + 1);
						_selectAll = false;
						break;

					case Keys.Home:
						_caret = 0;
						break;

					case Keys.End:
						_caret = _query.Length;
						break;

					case Keys.Delete:
						if (!string.IsNullOrEmpty(_query))
						{
							if (_selectAll)
							{
								_query = string.Empty;
								_caret = 0;
								_selectAll = false;
							}
							else if (_caret < _query.Length)
							{
								_query = _query.Remove(_caret, 1);
							}

							changed = true;
						}

						break;
				}
			}

			// Ctrl+A = 全选(近似:下一次输入 / 删除先把整串清掉)
			if (ctrl && now.IsKeyDown(Keys.A) && _prevKeys.IsKeyUp(Keys.A))
			{
				_selectAll = !string.IsNullOrEmpty(_query);
			}

			// 回车 / Esc 结束输入(回车是原版给的标志,必须看它,
			// 否则输入法用回车上屏时会被我们自己吞掉)
			if (Main.inputTextEnter
				|| (now.IsKeyDown(Keys.Escape) && _prevKeys.IsKeyUp(Keys.Escape)))
			{
				_typing = false;
				_selectAll = false;
			}

			_prevKeys = now;
			_caret = Math.Clamp(_caret, 0, _query.Length);

			UpdateSearchText();

			if (changed)
			{
				Refresh();
			}
			else
			{
				UpdateStatus();
			}
		}

		/// <summary>
		/// 把原版文本输入的结果并到带光标的查询串上。
		/// 原版只会「在末尾追加」或「从末尾删」,所以:
		///  - 变长 → 多出来的是新输入(含输入法上屏的一整串),插到光标处;
		///  - 变短 → 删掉的是末尾那些字符,改成删光标前面的;
		///  - 被清空 → 全清。
		/// </summary>
		private bool ApplyVanillaEdit(string vanilla)
		{
			string text = _query ?? string.Empty;

			if (vanilla == text)
			{
				return false;
			}

			if (vanilla.Length >= text.Length && vanilla.StartsWith(text, StringComparison.Ordinal))
			{
				string inserted = vanilla.Substring(text.Length);

				if (inserted.Length == 0)
				{
					return false;
				}

				if (_selectAll)
				{
					text = string.Empty;
					_caret = 0;
					_selectAll = false;
				}

				_caret = Math.Clamp(_caret, 0, text.Length);
				_query = text.Insert(_caret, inserted);
				_caret += inserted.Length;
				return true;
			}

			if (vanilla.Length < text.Length && text.StartsWith(vanilla, StringComparison.Ordinal))
			{
				if (vanilla.Length == 0)
				{
					_query = string.Empty;
					_caret = 0;
					_selectAll = false;
					return true;
				}

				int removed = text.Length - vanilla.Length;

				if (_selectAll)
				{
					_query = string.Empty;
					_caret = 0;
					_selectAll = false;
					return true;
				}

				int caret = Math.Clamp(_caret, 1, text.Length);
				int start = Math.Max(0, caret - removed);

				_query = text.Remove(start, caret - start);
				_caret = start;
				return true;
			}

			// 其它情况(理论上不会发生):照原版结果走
			_query = vanilla;
			_caret = Math.Clamp(_caret, 0, vanilla.Length);
			_selectAll = false;
			return true;
		}

		private void UpdateSearchText()
		{
			if (_searchText == null)
			{
				return;
			}

			string shown = string.IsNullOrEmpty(_query)
				? ModText.Get("SearchHint", "点这里输入(支持中文 / 拼音)")
				: _query;

			if (!_typing)
			{
				_searchText.SetText(shown);
				return;
			}

			// 光标画在 _caret 的位置
			int caret = string.IsNullOrEmpty(_query)
				? shown.Length
				: Math.Clamp(_caret, 0, shown.Length);

			_searchText.SetText(shown.Insert(caret, "|"));
		}

		private void BuildLists()
		{
			_allItems.Clear();
			var owners = new HashSet<string>(StringComparer.Ordinal);
			int total = ItemLoader.ItemCount;

			for (int type = 1; type < total; type++)
			{
				if (LegacyContentVisibility.IsHiddenFromCatalog(type)
					|| !ContentSamples.ItemsByType.TryGetValue(type, out var item) || item.IsAir
					|| string.IsNullOrWhiteSpace(Lang.GetItemNameValue(type)))
				{
					continue;
				}

				_allItems.Add(type);
				string owner = item.ModItem?.Mod.Name ?? "Terraria";
				if (owner != "ModLoader") owners.Add(owner);
			}
			_itemMods = owners.OrderBy(name => name == "Terraria" ? 0 : 1).ThenBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
			if (_modFilter != null && !owners.Contains(_modFilter)) { _modFilter = null; _modButton.SetText("全部模组"); }
			_built = true;

			// 一次性把「名字 / 全拼 / 首字母」算好,免得第一次按键卡一下。
			PinyinIndex.Prewarm(_allItems);

			NpcIndex.EnsureBuilt();
		}

		private void ShowModMenu()
		{
			if (!_built) BuildLists();
			UIChoiceMenu.Show(Window, _modButton, new[] { "全部模组" }.Concat(_itemMods).ToArray(),
				(index, label) => { _modFilter = index == 0 ? null : label; _modButton.SetText(label); Refresh(); });
		}

		/// <summary>鼠标停在格子上时显示名字。输入法候选词也在这里画(压在所有东西上面)。</summary>
		public override void Draw(SpriteBatch spriteBatch)
		{
			base.Draw(spriteBatch);

			if (!_typing || Window == null)
			{
				return;
			}

			// 候选词列表画在搜索框下面一点(和聊天框的候选框是同一套)
			ImeInput.DrawCandidates(new Vector2(
				Window.Left.Pixels + 24f,
				Window.Top.Pixels + SearchTop + SearchHeight + 38f));
		}

        private string SortLabel() => "排序：" + SortName(_sort);

        private void CycleSort()
        {
            SortMode[] modes = _category == Category.Npc ? new[] { SortMode.Default, SortMode.Name } : Enum.GetValues<SortMode>();
            UIChoiceMenu.Show(Window, _sortButton, modes.Select(SortName).ToArray(),
                (index, label) => { _sort = modes[index]; _sortButton.SetText(SortLabel()); Refresh(); });
        }
        private static string SortName(SortMode mode) => mode switch
        {
            SortMode.Tier => "获取阶段", SortMode.Name => "名称", SortMode.Recent => "最近查看", SortMode.Value => "价值",
            SortMode.Rarity => "稀有度", SortMode.Damage => "伤害", SortMode.Defence => "防御", SortMode.UseTime => "使用速度",
            SortMode.Pick => "镐力", SortMode.Axe => "斧力", SortMode.Hammer => "锤力", SortMode.Fishing => "渔力",
            SortMode.Bait => "饵力", SortMode.Tile => "物块编号", SortMode.WingFlight => "翅膀飞行时间", _ => "默认"
        };
        private static float SortValue(Item item, SortMode mode) => mode switch
        {
            SortMode.Value => item.value, SortMode.Rarity => item.rare, SortMode.Damage => item.damage, SortMode.Defence => item.defense,
            SortMode.UseTime => -item.useTime, SortMode.Pick => item.pick, SortMode.Axe => item.axe, SortMode.Hammer => item.hammer,
            SortMode.Fishing => item.fishingPole, SortMode.Bait => item.bait, SortMode.Tile => -item.createTile,
            SortMode.WingFlight => item.wingSlot > 0 ? ArmorIDs.Wing.Sets.Stats[item.wingSlot].FlyTime : 0, _ => 0
        };

		private void Refresh()
		{
			string query = _query?.Trim() ?? string.Empty;
			_filtered.Clear();

			if (_category == Category.Npc)
			{
				foreach (int type in NpcIndex.AllTypes)
				{
					if ((_dropQuery == 0 || GameDataIndex.GetDrops(type).Any(d => d.item == _dropQuery)) && _categories.Matches(type) && (!_favoritesOnly || IsFavorite(type)) && (_filter != 1 || GameDataIndex.GetDrops(type).Count > 0)
                        && (_modFilter == null || (NPCLoader.GetNPC(type)?.Mod.Name ?? "Terraria") == _modFilter) && NpcIndex.Matches(type, query))
					{
						_filtered.Add(type);
					}
				}

				_filtered.Sort((a,b) => _sort == SortMode.Name ? string.Compare(NpcIndex.GetName(a), NpcIndex.GetName(b), StringComparison.OrdinalIgnoreCase) : a.CompareTo(b));
			}
			else
			{
				// 以 @ 开头 = 按模组筛选(和 JEI 的 @mod 一个意思)。
				string modFilter = _modFilter;
				string nameQuery = query;

				if (query.StartsWith("@"))
				{
					modFilter = query.Substring(1).Trim();
					nameQuery = string.Empty;
				}

				foreach (int type in _allItems)
				{
					if (!_categories.Matches(type) || _category == Category.Recipes && StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(type)).Count == 0 || !MatchesFilter(type) || _favoritesOnly && !IsFavorite(type))
					{
						continue;
					}

					if (modFilter != null)
					{
						string modName = UIItemNames.ModNameOf(type);
						if (modName == null || (query.StartsWith("@")
							? modName.IndexOf(modFilter, StringComparison.OrdinalIgnoreCase) < 0
							: !string.Equals(modName, modFilter, StringComparison.Ordinal)))
						{
							continue;
						}
					}
					if (!PinyinIndex.Matches(type, nameQuery))
					{
						continue;
					}

					_filtered.Add(type);
				}

				ApplySort();
			}

			_grid.SetItems(_filtered);
			UpdateStatus();
		}

		private void ApplySort()
		{
			if ((int)_sort >= (int)SortMode.Value)
            {
                _filtered.Sort((a,b) => { int result = SortValue(ContentSamples.ItemsByType[b], _sort).CompareTo(SortValue(ContentSamples.ItemsByType[a], _sort)); return result != 0 ? result : a.CompareTo(b); });
                SortFavoritesFirst(); return;
            }
            // 收藏永远置顶(和终端一致);最近查看按打开顺序倒序
			if (_sort == SortMode.Recent)
			{
				_filtered.Sort((a, b) =>
				{
					int rankA = _recent.IndexOf(a);
					int rankB = _recent.IndexOf(b);

					if (rankA < 0 && rankB < 0)
					{
						return a.CompareTo(b);
					}

					if (rankA < 0)
					{
						return 1;
					}

					return rankB < 0 ? -1 : rankA.CompareTo(rankB);
				});
				return;
			}

			if (_sort == SortMode.Name)
			{
				_filtered.Sort((a, b) => string.Compare(
					Lang.GetItemNameValue(a), Lang.GetItemNameValue(b), StringComparison.OrdinalIgnoreCase));
				SortFavoritesFirst();
				return;
			}

			if (_sort == SortMode.Tier && StorageSystem.Progression != null)
			{
				_filtered.Sort((a, b) =>
				{
					int tierA = TierOf(a);
					int tierB = TierOf(b);
					if(tierA!=tierB) return tierA.CompareTo(tierB);
                    bool favoriteA=BrowserFavorites.Contains(a),favoriteB=BrowserFavorites.Contains(b);
                    return favoriteA!=favoriteB ? (favoriteA ? -1 : 1) : a.CompareTo(b);
				});
				return;
			}

			_filtered.Sort();
			SortFavoritesFirst();
		}

		/// <summary>收藏置顶。用稳定插入而不是重排,保留各个排序模式内部的相对顺序。</summary>
		private void SortFavoritesFirst()
		{
			if (_filtered.Count == 0)
			{
				return;
			}

			var starred = new List<int>();

			for (int i = _filtered.Count - 1; i >= 0; i--)
			{
				if (IsFavorite(_filtered[i]))
				{
					starred.Add(_filtered[i]);
					_filtered.RemoveAt(i);
				}
			}

			if (starred.Count == 0)
			{
				return;
			}

			starred.Reverse();
			_filtered.InsertRange(0, starred);
		}

		/// <summary>记一笔「最近查看」(去重 + 只留最近 40 个)。</summary>
		private void RememberRecent(int type)
		{
			if (type <= 0)
			{
				return;
			}

			_recent.Remove(type);
			_recent.Insert(0, type);

			while (_recent.Count > 40)
			{
				_recent.RemoveAt(_recent.Count - 1);
			}
		}

		private static int TierOf(int type)
		{
			if (StorageSystem.Progression == null)
			{
				return int.MaxValue;
			}

			int tier = StorageSystem.Progression.GetTierIndex(ItemKey.Crafted(type));
			return tier < 0 ? int.MaxValue : tier;
		}

        private bool MatchesFilter(int type)
        {
            var item = ContentSamples.ItemsByType[type];
            return _filter switch { 1 => item.material, 2 => StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(type)).Count > 0,
                3 => GameDataIndex.GetNpcSources(type).Count > 0, 4 => item.vanity, 5 => !item.vanity, _ => true };
        }
        protected override void DrawHoverTooltip(SpriteBatch batch)
        {
            _grid?.DrawTooltip(batch); _details?.DrawTooltip(batch);
        }
        private void UpdateStatus()
        {
            if (_status != null) _status.SetText(_filtered.Count == 0 ? "没有匹配结果" : "共 " + _filtered.Count + " 项");
        }
	}

}
