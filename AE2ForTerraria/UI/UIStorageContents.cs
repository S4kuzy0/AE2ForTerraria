using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 「这个存储方块里有什么」的可复用面板:
	/// 搜索框 + 内容图标网格 + 翻页 + 全部取出,左键取一叠、右键取全部。
	///
	/// 驱动器原本自己写了一份,存储单元则完全没有界面 ——
	/// 现在两边共用这一块,体验一致,也不用维护两份。
	/// </summary>
	public sealed class UIStorageContents : UIElement
	{
		private IMEStorage _storage;
		private Point16 _position;

		private UIText _header;
		private UITextBox _search;
		private UIItemGrid _grid;
		private UIGridScrollBar _scrollBar;
		private UITextPanel<string> _takeAllButton;
		private UITextPanel<string> _clearToGroundButton;
		private UITextPanel<string> _exportToChestButton;
		private UITextPanel<string> _pageButton;
		private double _clearConfirmUntil;

		private readonly List<ItemKey> _items = new List<ItemKey>();
		private string _lastQuery;
		private double _confirmUntil;

		/// <summary>
		/// 上次重建列表时的「账本版本 + 搜索词 + 排序模式」。
		/// 三者都没变就跳过重建(每帧重建 + 排序在几千种物品时很烧 CPU)。
		/// </summary>
		private int _builtVersion = int.MinValue;
		private string _builtQuery;
		private int _builtSortMode = -1;
		/// <summary>网络锚点(算一次缓存住),用来画「锁定」角标。</summary>
		private Point16 _anchor = new Point16(-1, -1);

		/// <summary>
		/// 排序模式:**驱动器 / 存储单元共用**(静态),所以在哪个窗口改的都一样。
		/// 0 = 按类型(默认)  1 = 按名称  2 = 数量从多到少  3 = 带词缀的排前面
		/// </summary>
		private static int SharedSortMode;
        private static bool SharedDescending;
        private bool _builtDescending;

		private UITextPanel<string> _sortButton;
        private UIIconButton _directionButton;

		/// <summary>宿主在内容变化后刷新一下状态行(容量之类)。</summary>
		public Action OnChanged;

		/// <summary>控件是否已经建好。见 <see cref="OnInitialize"/> 的说明。</summary>
		private bool _built;

		public override void OnInitialize()
		{
			// 可能有两种触发时机:框架的 Activate(),或者宿主在**自己的** OnInitialize 里
			// 直接调 Place() 时由我们补一次。加个标记保证只会建一遍,
			// 免得拖动 / 缩放窗口时又重来一次(那样会多出一整套控件)。
			if (_built)
			{
				return;
			}

			_built = true;

			_header = new UIText(string.Empty, 0.75f, false) { DynamicallyScaleDownToWidth=true };
			Append(_header);

			_search = new UITextBox(string.Empty, 0.8f, false);
			_search.BackgroundColor = UITheme.Field;
			Append(_search);

			_grid = new UIItemGrid
			{
				InputBlocked = () => UIChoiceMenu.IsOpen,
                CountProvider = key => _storage?.Ledger.GetAmount(key) ?? 0,

				// 锁定标记:和终端用同一套角标
				BadgeProvider = key => _anchor.X >= 0 && NetworkRegistry.IsLocked(_anchor, key.Type)
					? "lock"
					: null
			};
			_grid.OnLeftClick += (evt, element) => Withdraw(stack: 1);
			_grid.OnRightClick += (evt, element) => Withdraw(stack: -1);
			Append(_grid);

			// 分页网格右侧的细滚动条
			_scrollBar = new UIGridScrollBar { Target = _grid };
			Append(_scrollBar);

			_pageButton = new UIButton(ModText.Get("BtnNextDrivePage", "下一页"), 0.72f, false);
			_pageButton.OnLeftClick += (evt, element) =>
			{
				_grid.NextPage();
				Refresh();
			};
			Append(_pageButton);

			_takeAllButton = new UIButton(ModText.Get("BtnTakeAllContents", "全部取出"), 0.72f, false);
			_takeAllButton.OnLeftClick += (evt, element) =>
			{
				if (UIStatus.Now > _confirmUntil)
				{
					_confirmUntil = UIStatus.Now + 2.0;
					_takeAllButton.SetText(ModText.Get("ConfirmTakeAll", "再点一次取出全部"));
					return;
				}

				_confirmUntil = 0;
				_takeAllButton.SetText(ModText.Get("BtnTakeAllContents", "全部取出"));
				TakeAll();
			};
			Append(_takeAllButton);

			// 「清空到地上」和「全部取出」的区别:掉落物带 noGrabDelay,
			// 不会立刻被自己捡回去 —— 给「我要把它们丢在这儿」用。
			_clearToGroundButton = new UIButton(
				ModText.Get("BtnClearToGround", "清空到地上"), 0.72f, false);
			_clearToGroundButton.OnLeftClick += (evt, element) =>
			{
				if (UIStatus.Now > _clearConfirmUntil)
				{
					_clearConfirmUntil = UIStatus.Now + 2.0;
					_clearToGroundButton.SetText(
						ModText.Get("ConfirmClearToGround", "再点一次确认清空到地上"));
					return;
				}

				_clearConfirmUntil = 0;
				_clearToGroundButton.SetText(ModText.Get("BtnClearToGround", "清空到地上"));
				TakeAll(dropOnGround: true);
			};
			Append(_clearToGroundButton);

			// 「导出到箱子」:走网络里导出模式的总线,不用先全塞背包
			_exportToChestButton = new UIButton(
				ModText.Get("BtnExportToChest", "导出到箱子"), 0.72f, false);
			_exportToChestButton.OnLeftClick += (evt, element) => ExportToChest();
			Append(_exportToChestButton);

			// 排序:和终端一样有得选,而且两个窗口共用同一份设置
			_sortButton = new UIButton(SortLabel(), 0.72f, false);
			_sortButton.OnLeftClick += (evt, element) =>
			{
                UIChoiceMenu.Show(this,_sortButton,new[]{"默认","名称","数量","词缀优先"},(index,_) =>
                {
                    SharedSortMode = index;
                    _sortButton.SetText(SortLabel()); Refresh();
                },SharedSortMode);
			};
			Append(_sortButton);
            _directionButton = new UIIconButton(UIIconButton.IconKind.Up);
            _directionButton.OnLeftClick += (_,_) => { UIChoiceMenu.Close(); SharedDescending = !SharedDescending; Refresh(); };
            Append(_directionButton);
		}

		public void Bind(IMEStorage storage, Point16 position)
		{
			_storage = storage;
			_position = position;
			_lastQuery = null;
			_builtVersion = int.MinValue; // 换了个方块,必须重建
			_builtSortMode = -1;
			_builtQuery = null;
			_search?.SetText(string.Empty);

			// 锚点也重算一次(角标要用)
			_anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(position.X, position.Y));
			Refresh();
		}

		/// <summary>由宿主决定放在哪、多大。</summary>
		public void Place(float x, float y, float width, float height)
		{
			// 宿主(驱动器 / 存储单元窗口)会在**自己的** OnInitialize 里直接调这里,
			// 那一刻本面板的 OnInitialize 还没跑过(_header / _search / _grid 全是 null)
			// —— 以前这里直接空引用,异常把宿主窗口的初始化整条链打断,
			// 连带 UISystem.EnsureStates 后面的窗口全都停留在「没初始化」状态。
			if (!_built)
			{
				OnInitialize();
			}

			Left.Set(x, 0f);
			Top.Set(y, 0f);
			Width.Set(width, 0f);
			Height.Set(height, 0f);

			_header.Left.Set(0f, 0f);
			_header.Top.Set(0f, 0f);
			_header.Width.Set(width, 0f);
			_header.Height.Set(20f, 0f);
            _header.DynamicallyScaleDownToWidth = true;

			_search.Left.Set(0f, 0f);
			_search.Top.Set(24f, 0f);
			_search.Width.Set(Math.Max(80f, width - 126f), 0f);
			_search.Height.Set(24f, 0f);

            UIElement[] buttons={_pageButton,_takeAllButton,_clearToGroundButton,_exportToChestButton};
            int columns=Math.Max(1,Math.Min(4,(int)(width/132f)));
            int rows=(buttons.Length+columns-1)/columns;
            float footer=rows*30f;
            float gridHeight=Math.Max(20f,height-56f-footer-4f);
            _grid.Left.Set(0f,0f); _grid.Top.Set(56f,0f);
            _grid.Width.Set(Math.Max(20f,width-14f),0f); _grid.Height.Set(gridHeight,0f);
            _scrollBar.Left.Set(Math.Max(0f,width-10f),0f); _scrollBar.Top.Set(56f,0f); _scrollBar.Height.Set(gridHeight,0f);
            float buttonWidth=(width-(columns-1)*6f)/columns;
            for (int i=0;i<buttons.Length;i++)
            {
                buttons[i].Left.Set((i%columns)*(buttonWidth+6f),0f);
                buttons[i].Top.Set(height-footer+(i/columns)*30f,0f);
                buttons[i].Width.Set(buttonWidth,0f); buttons[i].Height.Set(26f,0f);
            }

			_sortButton.Left.Set(Math.Max(80f, width - 122f), 0f);
			_sortButton.Top.Set(24f, 0f);
			_sortButton.Width.Set(88f, 0f);
            _directionButton.Left.Set(width - 30f,0f); _directionButton.Top.Set(24f,0f);
            _directionButton.Width.Set(30f,0f); _directionButton.Height.Set(24f,0f);
			_sortButton.Height.Set(24f, 0f);

			Recalculate();
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			string query = _search?.Text?.Trim() ?? string.Empty;
			if (query != _lastQuery || _builtDescending != SharedDescending || _builtSortMode != SharedSortMode)
			{
				Refresh();
			}
		}

		public void Refresh()
		{
			if (_storage == null || _grid == null)
			{
				return;
			}

            _directionButton.Icon = SharedDescending ? UIIconButton.IconKind.Down : UIIconButton.IconKind.Up;
            _directionButton.Hint = SharedDescending ? "倒序，点击切换正序" : "正序，点击切换倒序";
            _sortButton.SetText(SortLabel());
			string query = _search?.Text?.Trim() ?? string.Empty;
			_lastQuery = query;

			bool sameAsBuilt = _builtVersion == _storage.Ledger.Version
				&& _builtSortMode == SharedSortMode && _builtDescending == SharedDescending
				&& string.Equals(_builtQuery, query, System.StringComparison.Ordinal);

			if (!sameAsBuilt)
			{
				_items.Clear();

				foreach (KeyValuePair<ItemKey, long> entry in _storage.Ledger.Entries)
				{
					if (entry.Key.Type <= 0 || entry.Value <= 0)
					{
						continue;
					}

					if (query.Length > 0 && !ItemSearch.Matches(entry.Key, query))
					{
						continue;
					}

					_items.Add(entry.Key);
				}

				SortItems();
				_grid.SetKeys(_items, resetPage: false);

				_builtVersion = _storage.Ledger.Version;
				_builtQuery = query;
				_builtSortMode = SharedSortMode; _builtDescending = SharedDescending;
			}

			if (_items.Count == 0)
			{
				_header.SetText(query.Length > 0
					? ModText.Format("LblNoSearchHit", "没有匹配「{0}」的物品", query)
					: ModText.Get("LabelEmptyDrive", "(空)") + " — "
						+ ModText.Get("LabelEmptyDriveHint", "把物品存进来就会显示在这里"));
				return;
			}

			_header.SetText(ModText.Format("DriveContentsHeader",
					"内容 {0}/{1}  共 {2} 种", _grid.Page + 1, _grid.PageCount, _items.Count)
				+ "   ·   左键一叠 / 右键全部");
		}

		private string SortLabel()
		{
			return (SharedSortMode switch
			{
				1 => ModText.Get("SortName", "排序: 名称"),
				2 => ModText.Get("SortCount", "排序: 数量"),
				3 => ModText.Get("SortPrefixed", "排序: 词缀优先"),
				_ => ModText.Get("SortDefault", "排序: 默认")
			});
		}

		private static int Direction(int value) => SharedDescending ? -Math.Sign(value) : value;
        private void SortItems()
		{
			_items.Sort((a, b) =>
			{
				switch (SharedSortMode)
				{
					case 1:
					{
						int byName = string.Compare(UIItemNames.Display(a), UIItemNames.Display(b),
							System.StringComparison.OrdinalIgnoreCase);

						if (byName != 0)
						{
							return Direction(byName);
						}

						break;
					}

					case 2:
					{
						long amountA = _storage?.Ledger.GetAmount(a) ?? 0;
						long amountB = _storage?.Ledger.GetAmount(b) ?? 0;

						if (amountA != amountB)
						{
							return Direction(amountA.CompareTo(amountB));
						}

						break;
					}

					case 3:
					{
						// 带词缀的排前面,方便集中处理
						bool prefixedA = a.Prefix != 0;
						bool prefixedB = b.Prefix != 0;

						if (prefixedA != prefixedB)
						{
							return Direction(prefixedA ? -1 : 1);
						}

						break;
					}
				}

				int byType = a.Type.CompareTo(b.Type);
				return Direction(byType != 0 ? byType : a.Prefix.CompareTo(b.Prefix));
			});
		}

		/// <summary>stack = 1 取一叠,-1 取全部。</summary>
		private void Withdraw(int stack)
		{
			if (_storage == null || _grid == null)
			{
				return;
			}

			ItemKey key = _grid.HoveredKey;
			if (key.IsAir)
			{
				return;
			}

			long have = _storage.Ledger.GetAmount(key);
			if (have <= 0)
			{
				return;
			}

			if (!SavedItemGuard.IsValidType(key.Type))
			{
				return;
			}

			int maxStack = SavedItemGuard.MaxStackOf(key.Type);

			long want = stack < 0 ? have : Math.Min(have, maxStack);
			Take(key, want, maxStack);
		}

		private void TakeAll(bool dropOnGround = false)
		{
			if (_storage == null || _items.Count == 0)
			{
				return;
			}

			var snapshot = new List<ItemKey>(_items);

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				foreach (ItemKey key in snapshot)
				{
					long have = _storage.Ledger.GetAmount(key);
					if (have > 0)
					{
						MENet.Withdraw(_position, key, have, exactStorage: true, dropOnGround: dropOnGround);
					}
				}

				UIStatus.Toast(ModText.Get("MsgTakeAllSent", "已把「全部取出」请求发给服务器"));
				return;
			}

			long total = 0;

			foreach (ItemKey key in snapshot)
			{
				if (!SavedItemGuard.IsValidType(key.Type))
				{
					continue;
				}

				int maxStack = SavedItemGuard.MaxStackOf(key.Type);

				total += Take(key, long.MaxValue, maxStack, dropOnGround);
			}

			if (total > 0)
			{
				UIStatus.Toast(ModText.Format("MsgTakeAll", "已取出 {0} 件物品(共 {1} 种)",
					total, snapshot.Count));
			}
		}

		/// <summary>真正的取出:多人走服务器,单人本地扣账 + 生成掉落物(带词缀)。</summary>
		/// <summary>把整个网络导出到「导出模式的总线」连着的箱子里。</summary>
		private void ExportToChest()
		{
			if (_storage == null)
			{
				return;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.ExportToChest(_position);
				UIStatus.Toast(ModText.Get("MsgExportSent", "已把「导出到箱子」请求发给服务器"));
				return;
			}

			// 单人 / 主机:把本地玩家带进去,让「锁定物品」的判断同样生效
			long moved = Content.Tiles.MEStorageBusEntity.ExportNetworkToChests(
				_position.X, _position.Y, Main.LocalPlayer?.name);

			UIStatus.Toast(moved > 0
				? ModText.Format("MsgExported", "已导出 {0} 件物品到箱子", moved)
				: ModText.Get("MsgExportNothing", "没有可导出的箱子(需要有导出模式的存储总线贴着箱子)"),
				moved > 0 ? new Color(150, 220, 255) : new Color(255, 200, 150));

			Refresh();
			OnChanged?.Invoke();
		}

		private long Take(ItemKey key, long want, int maxStack, bool dropOnGround = false)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.Withdraw(_position, key, want, exactStorage: true, dropOnGround: dropOnGround);
				return want;
			}

			if (NetworkRegistry.IsLocked(_anchor,key.Type)
                && NetworkRegistry.GetLevel(_anchor,Main.LocalPlayer?.name)<NetworkRegistry.LevelManage)
            { UIStatus.Toast("物品已锁定，需要管理权限才能取出"); return 0; }
            long taken = StorageOperations.Withdraw(new List<IMEStorage> { _storage }, key, want, Main.LocalPlayer, dropOnGround);

			UIStatus.Toast(ModText.Get("MsgTookPrefix", "已取出") + " " + taken + " "
				+ ModText.Get("MsgTookSuffix", "个") + " " + UIItemNames.Display(key));

			Refresh();
			OnChanged?.Invoke();
			return taken;
		}
	}
}
