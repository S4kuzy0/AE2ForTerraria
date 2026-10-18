using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// Both terminals support storage transfers; the crafting terminal also owns recipes, tasks and stations.
	/// Both entry points share catalogue controls without inferring purpose from network contents.
	/// </summary>
	public sealed class TerminalUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = WindowGeometry.Compact();
        private UIPlayerInventoryPanel _playerInventory;
        private UIText _selectionLabel;
        private UIText _craftReadiness;
        private TerminalCraftingAvailability _availability = new();
        private CraftPlan _selectedPlan;
        private int _availabilityRevision;
        private ulong _lastAvailabilityRefresh;
        private ulong _lastCataloguePaint;
        private TerminalViewMode _viewMode;
        private bool IsAccess => _viewMode == TerminalViewMode.Access;
		/// <summary>
		/// 列表按**完整身份**(物品ID + 词缀)存。
		/// 以前只存物品ID,导致「传奇 泰拉刃」和「泰拉刃」在网格里长得一样、
		/// 而且带词缀的那份数量显示成 0、也取不出来。
		/// </summary>
		private readonly List<ItemKey> _filtered = new List<ItemKey>();

		private UIBrowserSearchField _searchBox;
		private UIText _status;
		private UIItemGrid _grid;
		private UICapacityBar _capacity;
        private UIText _capacityCount;
        private bool _itemCapacity;
		private UIBrowserDetails _info;
        private UIBrowserCategoryBar _categories;
        private UIButton _filterButton;
        private UIButton _inventoryButton, _modButton;
        private readonly UIStationGrid _stationGrid = new();
        private bool _stationLayoutDirty, _stationDataDirty;
        private UIButton _catalogueButton, _recipesButton, _tasksButton, _stationsButton;
        private UIIntegratedCraftingPanel _management;
        private int _managementPage;
        private MECraftingCpuEntity _keepEditHost;
        private bool _inventoryVisible, _favoritesOnly;
        private string _modFilter;
        private static readonly string[] RangeChoices = { "已储存", "可合成", "已储存/可合成" };
        private int _range = 2; // Stored, verified craftable, or their union.
        private UIGridScrollBar _scrollBar;
		private UIButton _orderButton;
		private UITextPanel<string> _takeButton;
		private UITextPanel<string> _depositButton;
		private UITextPanel<string> _takeAllButton;
		private double _takeAllConfirmUntil;
		private UITextPanel<string> _quantityButton;
		private UITextPanel<string> _craftableButton;
		private UITextPanel<string> _prefixButton;
		private UITextPanel<string> _keepButton;
		private bool _onlyPrefixed;
		private UIPanel _quantityInput;
		private UIText _quantityText;
		private bool _quantityTyping;
		private string _quantityBuffer = string.Empty;
		private static KeyboardState _prevKeys;
		private UITextPanel<string> _sortButton;
		private UITextPanel<string> _ammoButton;
		private int _sortMode;
		

		public override bool SearchBoxFocused => _searchFocused || _quantityTyping;
        private string _lastQuery = string.Empty;
		private ItemKey _selected = ItemKey.None;
		private bool _searchFocused;
		/// <summary>
		/// 当前网络锚点(算一次缓存住)。
		/// 角标每帧要按物品判断「锁没锁」,不能每格都跑一次 BFS。
		/// </summary>
		private Point16 _lockAnchor = new Point16(-1, -1);
		/// <summary>多选集合(按物品 ID)。Ctrl+左键逐个加入,配合下面的批量按钮。</summary>
		private readonly HashSet<ItemKey> _selection = new HashSet<ItemKey>();
		private UITextPanel<string> _takeSelectedButton;
		private UITextPanel<string> _clearSelectionButton;

		private long _orderQuantity = 1;
		
		private bool _depositAmmoAndCoins;
		private Dictionary<ItemKey, long> _lastContents;

		public override void OnInitialize()
		{
			Geometry.Key = "TerminalUIState.v3";
            InitWindow(Geometry, "", frameless: true);
            Window.BackgroundColor = UITheme.WindowBg;
            _catalogueButton=new UIButton("终端", .75f);
            _catalogueButton.OnLeftClick+=(_,_)=>{_managementPage=0;_info?.Close();_categories.SetMode(false);RebuildList();RequestLayout();};
            _recipesButton=new UIButton("配方", .75f);
            _recipesButton.OnLeftClick+=(_,_)=>
            {
                _managementPage=0;
                if (!_selected.IsAir) Select(_selected);
                else if (_filtered.Count>0) Select(_filtered[0]);
                else UIStatus.Toast("先选择一个物品");
                RequestLayout();
            };
            _tasksButton=new UIButton("任务", .75f);
            _tasksButton.OnLeftClick+=(_,_)=>ShowManagement(false);
            _stationsButton=new UIButton("工作站", .75f);
            _stationsButton.OnLeftClick+=(_,_)=>ShowManagement(true);
            foreach(var button in new[]{_catalogueButton,_recipesButton,_tasksButton,_stationsButton}) Window.Append(button);
            _categories = new UIBrowserCategoryBar();
            _categories.Changed = () => { RequestLayout(); RebuildList(); };
            Window.Append(_categories);
            _inventoryButton = new UIButton("背包");
            _inventoryButton.OnLeftClick += (_, _) => { _inventoryVisible = !_inventoryVisible; _inventoryButton.Selected = _inventoryVisible; RequestLayout(); };
            Window.Append(_inventoryButton);
            _filterButton = new UIButton("筛选", .72f);
            _filterButton.OnLeftClick += (_, _) => UIChoiceMenu.Show(Window, _filterButton, new[] { "全部物品", "已收藏" },
                (index, _) => { _favoritesOnly = index == 1; _filterButton.Selected = _favoritesOnly; RebuildList(); });
            Window.Append(_filterButton);
            _modButton = new UIButton("全部模组");
            _modButton.OnLeftClick += (_, _) => UIChoiceMenu.Show(Window, _modButton,
                new[] { "全部模组", "Terraria" }.Concat(ModLoader.Mods.Where(m => m.Name != "ModLoader").Select(m => m.Name)).ToArray(),
                (index, label) => { _modFilter = index == 0 ? null : label; _modButton.SetText(label); RebuildList(); });
            Window.Append(_modButton);

			_searchBox = new UIBrowserSearchField { Focused = () => _searchFocused && !_quantityTyping, EndInput = () => _searchFocused = false };
            Window.OnLeftMouseDown += (e, _) => _searchFocused = e.Target == _searchBox;
			// tML 的 UITextBox 没公开焦点状态,自己记一个:
			// 点它就当作在输入,点网格就当输入结束。
			_searchBox.OnLeftClick += (evt, element) => _searchFocused = true;
			Window.Append(_searchBox);

			// 多选批量操作:Ctrl+左键逐个选,这里一次性取出来
			_takeSelectedButton = new UIButton(ModText.Get("BtnTakeSelected", "取出选中"), 0.78f, false);
			_takeSelectedButton.OnLeftClick += (evt, element) => WithdrawSelected();
			Window.Append(_takeSelectedButton);

			_clearSelectionButton = new UIButton(ModText.Get("BtnClearSelection", "清空选择"), 0.78f, false);
			_clearSelectionButton.OnLeftClick += (evt, element) =>
			{
				_selection.Clear();
				UpdateSelectionButtons();
			};
			Window.Append(_clearSelectionButton);

			_grid = new UIItemGrid
			{
				CellSize = 44,
                IsFavorite = key => BrowserFavorites.Contains(key.Type),
                InputBlocked = () => _info?.BlocksPointer == true || UIChoiceMenu.IsOpen || UISystem.SecondaryInterface?.CurrentState != null || UISystem.OverlayInterface?.CurrentState != null,
                CountProvider = key => UISystem.GetStoredAmount(key),

				// 「维持库存」的物品在格子上标一个 ∞,不用点开就知道哪些在自动补货
				BadgeProvider = key =>
				{
					string badge = null;

					// 锁定标记
					if (_lockAnchor.X >= 0 && NetworkRegistry.IsLocked(_lockAnchor, key.Type))
					{
						badge = "lock";
					}

					MECraftingCpuEntity cpu = UISystem.ActiveCraftingCpu;

					if (cpu == null)
					{
						return badge;
					}

					foreach (MECraftingCpuEntity.KeepEntry entry in cpu.Keep)
					{
						if (entry.Key.Type == key.Type)
						{
							return (badge ?? string.Empty) + " keep";
						}
					}

					return badge;
				},

				// 选中的格子加绿框(和过滤清单同一套视觉)
				IsMarked = key => _selection.Contains(key)
			};
			_grid.OnLeftClick += (evt, element) =>
			{
				_searchFocused = false;
                if (FavoriteInput.IsDown)
                {
                    if (_grid.HoveredType > 0) { FavoriteInput.Toggle(_grid.HoveredType); RebuildList(false); }
                    return;
                }
                if (!Main.mouseItem.IsAir) { DepositHeldItem(); return; }

				if (_grid.HoveredType <= 0)
				{
					return;
				}

				// Ctrl = 加入 / 移出多选集合(批量操作)
				if (IsAccess && IsCtrlDown())
				{
					if (!_selection.Remove(_grid.HoveredKey))
					{
						_selection.Add(_grid.HoveredKey);
					}

					UpdateSelectionButtons();
					return;
				}

				// Alt is reserved for native-style favorites; Shift still withdraws a stack.
				if (IsShiftDown())
				{
                    _selected = _grid.HoveredKey;
					Withdraw(OverrideStack());
                    return;
				}
                Select(_grid.HoveredKey);
			};
			_grid.OnActivated = key => Select(key);
            _grid.OnUsed = key => _info.Show(key, _orderQuantity, uses: true);
            // Double clicks deliberately have no transfer action. Plain clicks open details.

			Window.Append(_grid);
            _scrollBar = new UIGridScrollBar { Target = _grid }; Window.Append(_scrollBar);

			// 网络容量一行:以前只有容量告警才看得见,现在常驻
			_capacity = new UICapacityBar();
			Window.Append(_capacity);
            _capacityCount=new UIText("",.7f) { DynamicallyScaleDownToWidth=true };

			_sortButton = new UIButton(SortLabel(), 0.78f, false) { Dropdown = true };
			_sortButton.OnLeftClick += (_, _) => ShowSortMenu();
			Window.Append(_sortButton);

			// 「存入全部」的范围开关。
			// 默认只存主背包 —— 弹药栏和钱币栏不该被一键塞进网络。
			_depositAmmoAndCoins = InventoryScope.IncludeAmmoAndCoinsByDefault;
			_ammoButton = new UIButton(AmmoLabel(), 0.78f, false);
			_ammoButton.OnLeftClick += (evt, element) =>
			{
				_depositAmmoAndCoins = !_depositAmmoAndCoins;
				_ammoButton.SetText(AmmoLabel());
			};
			Window.Append(_ammoButton);

            _playerInventory = new UIPlayerInventoryPanel();
            _playerInventory.SetTransferHandlers(DepositSlot, DepositAllOfType);
            _selectionLabel = new UIText("点击物品选择数量", .72f) { DynamicallyScaleDownToWidth = true };

			_info = new UIBrowserDetails(Window, terminal: true);
            _craftReadiness = new UIText("", .72f) { DynamicallyScaleDownToWidth = true };
            _info.Append(_craftReadiness);
            _info.Selected = key =>
            {
                _selected = key; _orderQuantity = _info.Quantity; _managementPage = 0;
                _quantityButton.SetText(QuantityLabel()); UpdateSelectedPlan(); RequestLayout();
            };
            _info.Closed = () => RequestLayout();
            _info.FavoriteChanged = () => RebuildList(false);
            _stationGrid.LayoutChanged = () => _stationLayoutDirty=true;
            _stationGrid.ContentsChanged = () => _stationDataDirty=true;
            _management=new UIIntegratedCraftingPanel((host,key,target)=>
            {
                _orderQuantity=target; _quantityBuffer=target.ToString();_quantityTyping=true;
                Select(key);_keepEditHost=host;UpdateQuantityText();_quantityButton.SetText(QuantityLabel());
            });

			_orderButton = new UIButton(ModText.Get("OrderCraft", "下单合成"), 0.82f, false);
			// 主操作:整块面板里最显眼的那一个
			_orderButton.Primary = true;
			_orderButton.OnLeftClick += (evt, element) => OrderCraft();
			Window.Append(_orderButton);

			_takeButton = new UIButton(ModText.Get("TakeStack", "取出一个堆叠"), 0.82f, false);
			_takeButton.OnLeftClick += (evt, element) => Withdraw();
			Window.Append(_takeButton);

			_depositButton = new UIButton(ModText.Get("DepositAll", "存入全部"), 0.82f, false);
			_depositButton.OnLeftClick += (evt, element) => DepositAll();
			Window.Append(_depositButton);

			_takeAllButton = new UIButton(ModText.Get("TakeAll", "取出全部"), 0.82f, false);
			_takeAllButton.OnLeftClick += (evt, element) =>
			{
				// 「取出全部」也是不可逆的,和清空队列一样要二次确认
				if (UIStatus.Now > _takeAllConfirmUntil)
				{
					_takeAllConfirmUntil = UIStatus.Now + 2.0;
					_takeAllButton.SetText(ModText.Get("ConfirmTakeAll", "再点一次取出全部"));
					return;
				}

				_takeAllConfirmUntil = 0;
				_takeAllButton.SetText(ModText.Get("TakeAll", "取出全部"));
				WithdrawAll();
			};
			Window.Append(_takeAllButton);

			_quantityButton = new UIButton(QuantityLabel(), 0.82f, false);
			_quantityButton.OnLeftClick += (evt, element) => CycleQuantity();
			Window.Append(_quantityButton);

			// 任意数量输入:点一下就进入输入态,直接敲数字
			_quantityInput = new UIPanel();
			_quantityInput.BackgroundColor = new Color(24, 30, 50);
			_quantityInput.OnLeftClick += (evt, element) =>
			{
				_quantityTyping = true;
				_quantityBuffer = _orderQuantity.ToString();
				UpdateQuantityText();
			};
			Window.Append(_quantityInput);

			_quantityText = new UIText("输入数量", 0.78f, false);
			_quantityInput.Append(_quantityText);
			_quantityText.Left.Set(6f, 0f);
			_quantityText.Top.Set(4f, 0f);

			_craftableButton = new UIButton("已储存", 0.82f, false) { Dropdown = true };
			_craftableButton.OnLeftClick += (_, _) => ShowRangeMenu();
			Window.Append(_craftableButton);

			// 词缀过滤:只看带词缀的装备,方便集中重铸 / 整理
			_prefixButton = new UIButton(PrefixLabel(), 0.82f, false);
			_prefixButton.OnLeftClick += (evt, element) =>
			{
				_onlyPrefixed = !_onlyPrefixed;
				_prefixButton.SetText(PrefixLabel());
				RebuildList();
			};
			Window.Append(_prefixButton);

			// 选中物品 + 数量设置或更新目标；任务页负责移除。
			_keepButton = new UIButton(ModText.Get("BtnKeepStock", "维持库存"), 0.82f, false);
			_keepButton.OnLeftClick += (evt, element) => ToggleKeepStock();
			Window.Append(_keepButton);

			_status = new UIText(string.Empty, 0.72f, false);
			Window.Append(_status);

            foreach (var e in new UIElement[] { _orderButton, _takeButton, _takeAllButton, _quantityButton, _quantityInput, _keepButton }) { e.Remove(); _info.Append(e); }
            _info.BottomReserve = 68;
            RequestLayout();
        }

        private static void Place(UIElement e, float x, float y, float w, float h)
        { e.Left.Set(x, 0); e.Top.Set(y, 0); e.Width.Set(w, 0); e.Height.Set(h, 0); }
        private void ShowManagement(bool stations)
        {
            if (IsAccess) return;
            _info.Close();_searchFocused=false;_quantityTyping=false;
            _managementPage=stations ? 2 : 1;_management.Show(stations);RequestLayout();
        }
        private bool CanConfigure(int level)
        {
            if(Main.netMode==NetmodeID.MultiplayerClient) return true;
            var p=UISystem.ActiveTerminalPos;
            if(p.X<0) return true; // Legacy no-terminal fixture/UI remains supported.
            var anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(p.X,p.Y));
            if(NetworkRegistry.GetOwner(anchor)==null) NetworkRegistry.TryClaim(anchor,Main.LocalPlayer?.name);
            if(NetworkRegistry.GetLevel(anchor,Main.LocalPlayer?.name)>=level) return true;
            UIStatus.Toast("当前网络权限不足");return false;
        }
        private void Visible(UIElement element,bool visible)
        {
            if (visible) { if(element.Parent==null) Window.Append(element); }
            else element.Remove();
        }
        protected override void ApplyLayout()
        {
            base.ApplyLayout();
            if (_info == null || _keepButton == null) return;
            float w = Geo.W, h = Geo.H, inner = w - Pad * 2;
            bool catalogue = _managementPage == 0 && !_info.IsOpen;
            _catalogueButton.SetText("终端");
            Place(_catalogueButton, Pad, Pad, 80, 22);
            Visible(_recipesButton, false); Visible(_stationsButton, false);
            Visible(_tasksButton, !IsAccess);
            Place(_tasksButton, Pad + 84, Pad, 60, 22);
            Visible(_inventoryButton, false);
            Place(_filterButton, w - Pad - 122, Pad, 42, 22);
            Place(_searchBox, Pad, 36, inner - 128, 24);
            Place(_modButton, w - Pad - 122, 36, 122, 24);
            Place(_categories, Pad, 64, inner, 22);
            float top = 64 + _categories.Arrange(inner) + 5;
            float controlW = (inner - 18) / 4;
            if (IsAccess)
            {
                Place(_takeSelectedButton, Pad, top, controlW, 26);
                Place(_clearSelectionButton, Pad + controlW + 6, top, controlW, 26);
                Place(_prefixButton, Pad + 2 * (controlW + 6), top, controlW, 26);
                Place(_sortButton, Pad + 3 * (controlW + 6), top, controlW, 26);
            }
            else
            {
                Place(_craftableButton, Pad, top, inner - 224, 26);
                Place(_prefixButton, w - Pad - 218, top, 94, 26);
                Place(_sortButton, w - Pad - 118, top, 118, 26);
            }
            top += 32;
            float stationHeight = !IsAccess && catalogue ? _stationGrid.RequiredHeight : 0;
            Visible(_stationGrid, stationHeight > 0);
            if (stationHeight > 0) _stationGrid.SetBounds(Pad, h - Pad - 50 - stationHeight, inner);
            float gridBottom = h - Pad - 54 - stationHeight;
            float gridHeight = Math.Max(43, gridBottom - top);
            Place(_grid, Pad, top, inner - 12, gridHeight);
            Place(_scrollBar, w - Pad - 9, top, 9, Math.Min(gridHeight, Math.Max(12, h - 28 - top)));
            Place(_depositButton, Pad, h - Pad - 46, 100, 24);
            Place(_ammoButton, Pad + 106, h - Pad - 46, inner - 106, 24);
            float capacityWidth = inner * .45f;
            Place(_capacity, Pad, h - Pad - 18, capacityWidth, 14);
            Place(_capacityCount, Pad, h - Pad - 18, capacityWidth, 18);
            Place(_status, Pad + capacityWidth + 8, h - Pad - 18, inner - capacityWidth - 20, 18);
            foreach (var element in new UIElement[] { _searchBox, _modButton, _categories, _sortButton, _grid, _scrollBar,
                _status, _depositButton, _ammoButton, _prefixButton }) Visible(element, catalogue);
            foreach (var element in new UIElement[] { _takeSelectedButton, _clearSelectionButton }) Visible(element, catalogue && IsAccess);
            Visible(_craftableButton, catalogue && !IsAccess);
            Visible(_capacity, catalogue && !_itemCapacity);
            Visible(_capacityCount, catalogue && _itemCapacity);
            Visible(_playerInventory, false); Visible(_selectionLabel, false);
            _catalogueButton.Selected = catalogue; _tasksButton.Selected = _managementPage == 1;
            float drawerH = h - Pad - 36;
            _info.BottomReserve = IsAccess ? 38 : 92;
            _info.SetBounds(Pad, 36, inner, drawerH);
            Place(_craftReadiness, 10, drawerH - 86, inner - 20, 18);
            if (_info.IsOpen) { _info.Remove(); Window.Append(_info); }
            foreach (var action in new UIElement[] { _quantityButton, _quantityInput, _orderButton, _keepButton, _takeButton, _takeAllButton, _craftReadiness }) action.Remove();
            if (!_selected.IsAir && _info.IsOpen)
            {
                UIElement[] storage = { _quantityButton, _quantityInput, _takeButton, _takeAllButton };
                float actionW = (inner - 38) / 4;
                for (int i = 0; i < storage.Length; i++)
                {
                    _info.Append(storage[i]);
                    Place(storage[i], 10 + i * (actionW + 6), drawerH - 34, actionW, 26);
                }
                if (!IsAccess)
                {
                    _info.Append(_craftReadiness); _info.Append(_orderButton); _info.Append(_keepButton);
                    Place(_orderButton, 10, drawerH - 64, (inner - 26) / 2, 26);
                    Place(_keepButton, 16 + (inner - 26) / 2, drawerH - 64, (inner - 26) / 2, 26);
                }
            }
            _quantityInput.SetPadding(0); _quantityText.Width.Set(-12, 1); _quantityText.DynamicallyScaleDownToWidth = true;
            if (_management != null)
            {
                Visible(_management, !IsAccess && _managementPage != 0);
                _management.SetBounds(Pad, 36, inner, h - Pad - 36);
            }
            KeepResizeGripInFront();
            Window.Recalculate();
        }
        public void ShowDetails(int type, bool uses = false) => _info.Show(ItemKey.Crafted(type), _orderQuantity, uses);

        public bool CloseDetails() { if (!_info.IsOpen) return false; _info.Close(); return true; }

		/// <summary>每次打开终端:重算锁定锚点并重建列表(角标要立刻正确)。</summary>
		public override void OnActivate()
		{
			base.OnActivate();
            ResetForOpen();
        }

        public void ResetForOpen()
        {
            if (_info == null) return;
            _viewMode = UISystem.ActiveTerminalViewMode;
            _inventoryVisible = false; _searchFocused = _quantityTyping = false;
            _selection.Clear(); _selected = ItemKey.None; _selectedPlan = null;
            UITextCache.Set(_selectionLabel, "点击物品选择数量");
            _range = IsAccess ? 0 : 2; _craftableButton.SetText(RangeLabel());
            _availability = TerminalCraftingCacheSystem.Current; _availability.Refresh();
            _lastAvailabilityRefresh = Main.GameUpdateCount;
            _availabilityRevision = _availability.Revision;
            _managementPage=0;
            _info?.Close();
			RefreshLockAnchor();
			RebuildList();
			RequestLayout();
		}

		protected override void UpdateWindow(GameTime gameTime)
		{
            if(_stationLayoutDirty) {_stationLayoutDirty=false;RequestLayout();}
            if(_stationDataDirty) {_stationDataDirty=false;_availability.Refresh();RebuildList(false);UpdateSelectedPlan();}
			HandleQuantityTyping();
			UpdateTakeLabel();
			UpdateCapacity();
            _orderButton.Enabled=!IsAccess && UISystem.TerminalCanUse && _selectedPlan?.FullySatisfied == true;
            ((UIButton)_takeButton).Enabled=!_selected.IsAir && UISystem.GetStoredAmount(_selected) > 0 && UISystem.TerminalCanUse;
            ((UIButton)_takeAllButton).Enabled=!_selected.IsAir && UISystem.GetStoredAmount(_selected) > 0 && UISystem.TerminalCanUse;
            ((UIButton)_depositButton).Enabled=UISystem.TerminalCanUse;
            ((UIButton)_keepButton).Enabled=UISystem.TerminalCanManage;
            if (!UISystem.TerminalReadable) UITextCache.Set(_status,UISystem.TerminalAccessStatus);

			// 搜索框正在输入时把网格的方向键让给文本框(光标移动),
			// 否则方向键会被两边同时吃掉。
			_grid.KeyboardEnabled = !_searchFocused && !_quantityTyping;

			// 多人:服务器每隔几十 tick 会补一份库存汇总过来,
			// 内容变了就重刷列表(保留当前页,不然合成的时候列表会一直跳回第一页)。
			if (!ReferenceEquals(_lastContents, UISystem.ActiveNetworkContents))
			{
				_lastContents = UISystem.ActiveNetworkContents;
				_availability.Refresh();
				RebuildList(false);
                if (_info.IsOpen && !_selected.IsAir) _info.Show(_selected, _orderQuantity, remember: false, preserveTab: true);
			}
            if (!IsAccess)
            {
                if (Main.GameUpdateCount - _lastAvailabilityRefresh >= 30)
                {
                    _lastAvailabilityRefresh = Main.GameUpdateCount;
                    _availability.Refresh(); UpdateSelectedPlan();
                }

                if (_availabilityRevision != _availability.Revision && Main.GameUpdateCount - _lastCataloguePaint >= 6)
                {
                    _lastCataloguePaint = Main.GameUpdateCount;
                    _availabilityRevision = _availability.Revision;
                    RebuildList(false);
                }
            }

			string query = _searchBox?.Text?.Trim() ?? string.Empty;
			if (query != _lastQuery)
			{
				_lastQuery = query;
				RebuildList();
			}

		}

		/// <summary>
		/// 网络容量:把网络里每个存储方块的字节数加起来。
		/// 有方块不限容量(MaxBytes = long.MaxValue)时只显示已用量,不画进度条。
		/// </summary>
		private void UpdateCapacity()
		{
			if (_capacity == null)
			{
				return;
			}

			long used = 0;
			long max = 0;
			bool unlimited = false;
            decimal totalItems=0;

			List<Core.IMEStorage> network = UISystem.ActiveNetwork;

			if (network != null)
			{
				foreach (Core.IMEStorage storage in network)
				{
					Core.StorageLedger ledger = storage?.Ledger;
					if (ledger == null)
					{
						continue;
					}

					used = StorageAmounts.Add(used,ledger.UsedBytes);
                    totalItems+=ledger.StoredTotalAmount;

					if (ledger.MaxBytes <= 0 || ledger.MaxBytes >= long.MaxValue / 4)
					{
						unlimited = true;
						continue;
					}

					max = StorageAmounts.Add(max,ledger.MaxBytes);
				}
			}

			_capacity.Used = used;
			_capacity.Max = unlimited ? 0 : max;
			_capacity.Label = ModText.Get("LabelNetworkCapacity", "网络容量");
            if(unlimited) UITextCache.Set(_capacityCount,"已储存 "+totalItems.ToString("N0")+" 件");
            if(_itemCapacity!=unlimited) {_itemCapacity=unlimited;RequestLayout();}
		}

		/// <summary>
		/// 取出按钮的文案跟着数量走 —— 数量 > 1 时它取的就是那个数量,
		/// 不然玩家会以为按钮永远只取一个堆叠。
		/// </summary>
		private void UpdateTakeLabel()
		{
			if (_takeButton == null)
			{
				return;
			}

			if (_orderQuantity > 1)
			{
				_takeButton.SetText(ModText.Format("TakeAmount", "取出 x{0}", _orderQuantity));
			}
			else
			{
				_takeButton.SetText(ModText.Get("TakeStack", "取出一个堆叠"));
			}
		}

		private void RebuildList(bool resetPage = true)
		{
			_filtered.Clear();
			Dictionary<ItemKey, long> contents = UISystem.ActiveNetworkContents;

			if (contents != null && (IsAccess || _range == 0 || _range == 2))
			{
				foreach (KeyValuePair<ItemKey, long> entry in contents)
				{
					if (entry.Value <= 0 || entry.Key.Type <= 0 || !MatchesCatalogue(entry.Key.Type))
					{
						continue;
					}

					if (_lastQuery.Length > 0 && !ItemSearch.Matches(entry.Key, _lastQuery))
					{
						continue;
					}

					// 只看带词缀的
					if (_onlyPrefixed && entry.Key.Prefix == 0)
					{
						continue;
					}

					// 按完整身份加进去:同一种物品的不同词缀是两份,不去重
					_filtered.Add(entry.Key);
				}
			}

			// Add canonical crafting outputs without merging distinct stored identities.
			if (!IsAccess && _range != 0 && !_onlyPrefixed && StorageSystem.Recipes != null)
			{
				var known = new HashSet<ItemKey>();
                var priority = new List<int>();
				foreach (ItemKey key in _filtered)
				{
					known.Add(key);
				}

				foreach (int type in StorageSystem.Recipes.OutputTypes)
				{
					if (known.Contains(ItemKey.Crafted(type)) || !MatchesCatalogue(type))
					{
						continue;
					}

					if (StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(type)).Count == 0)
					{
						continue;
					}
					if (_lastQuery.Length > 0 && !ItemSearch.Matches(ItemKey.Crafted(type), _lastQuery))
					{
						continue;
					}

                    // Recipe existence only puts an item into "all recipes". A
                    // positive craftable result requires a checked complete plan.
                    if(_lastQuery.Length>0) priority.Add(type);
                    bool ready = _availability.CanUse && _availability.Catalogue.IsCraftable(type);
                    if (!ready) continue;

					_filtered.Add(ItemKey.Crafted(type));
				}
                if(priority.Count>0) _availability.Catalogue.Prioritize(priority);
			}

			// 排序:收藏永远在最前,然后按所选模式
			_filtered.Sort((a, b) =>
			{
				bool favA = BrowserFavorites.Contains(a.Type);
				bool favB = BrowserFavorites.Contains(b.Type);
				if (favA != favB)
				{
					return favA ? -1 : 1;
				}

				if (_sortMode == 1)
				{
					int byName = string.Compare(UIItemNames.Display(a), UIItemNames.Display(b),
						StringComparison.OrdinalIgnoreCase);
					if (byName != 0)
					{
						return byName;
					}

					return CompareKeys(a, b);
				}

				if (_sortMode == 2)
				{
					long amountA = UISystem.GetStoredAmount(a);
					long amountB = UISystem.GetStoredAmount(b);
					return amountA != amountB ? amountB.CompareTo(amountA) : CompareKeys(a, b);
				}

				return CompareKeys(a, b);
			});

			_grid.SetKeys(_filtered, resetPage);
			UpdateStatus();
		}

		public override void OnDeactivate() { base.OnDeactivate(); _info?.Close(); ImeInput.Blur(); UIChoiceMenu.Close(); _searchFocused = _quantityTyping = false; }
        private static int CompareKeys(ItemKey a, ItemKey b)
		{
			int byType = a.Type.CompareTo(b.Type);
			return byType != 0 ? byType : a.Prefix.CompareTo(b.Prefix);
		}

		private void UpdateStatus()
		{
			if (_status == null)
			{
				return;
			}

			string network = UISystem.ActiveNetwork == null
				? ModText.Get("LblStatusNoDrive", "未连接驱动器")
				: UISystem.ActiveNetwork.Count + " " + ModText.Get("LblStatusDrives", "个驱动器");
            if (!UISystem.TerminalReadable)
            {
                UITextCache.Set(_status, UISystem.TerminalAccessStatus); return;
            }

			// 容量告警:快满 / 已满时状态行变色
			long used = 0;
			long max = 0;

			if (UISystem.ActiveNetwork != null)
			{
				foreach (IMEStorage unit in UISystem.ActiveNetwork)
				{
					used = StorageAmounts.Add(used,unit.Ledger.UsedBytes);

					if (unit.Ledger.MaxBytes != long.MaxValue)
					{
						max = StorageAmounts.Add(max,unit.Ledger.MaxBytes);
					}
				}
			}

			float ratio = max > 0 && !_itemCapacity ? Math.Clamp((float)used / max, 0f, 1f) : 0f;
			string capacity = max > 0 && !_itemCapacity
				? ModText.Get("LabelCapacity", "容量") + " " + UIFormat.Count(used) + "/"
					+ UIFormat.Count(max) + "  "
				: string.Empty;

			_status.TextColor = ratio >= 0.95f
				? new Color(255, 130, 120)
				: ratio >= 0.8f ? new Color(255, 215, 130) : Color.White;

			UITextCache.Set(_status, capacity + network + "   |   "
				+ ModText.Get("LblStatusTotalPrefix", "共") + " " + _filtered.Count + " "
				+ ModText.Get("LblStatusKinds", "种物品"));
		}

		private string SortLabel()
		{
			return _sortMode == 0
				? ModText.Get("SortDefault", "排序: 默认")
				: _sortMode == 1
					? ModText.Get("SortName", "排序: 名称")
					: ModText.Get("SortCount", "排序: 数量");
		}

		private string QuantityLabel()
		{
			return ModText.Get("LabelQuantity", "数量") + " x" + _orderQuantity;
		}

		private string AmmoLabel()
		{
			return _depositAmmoAndCoins
				? ModText.Get("DepositWithAmmo", "存入: 含弹药/钱币")
				: ModText.Get("DepositMainOnly", "存入: 仅主背包");
		}

		private string PrefixLabel()
		{
			return _onlyPrefixed
				? ModText.Get("OnlyPrefixedOn", "仅带词缀")
				: ModText.Get("OnlyPrefixedOff", "全部物品");
		}

		/// <summary>
		/// 「维持库存」:对当前选中的物品设置一个目标数量,网络里少于它就自动补货。
		/// 已经设过的话再点一次就是取消。
		/// </summary>
		private void ToggleKeepStock()
		{
            if (IsAccess) return;
            if (!CanConfigure(NetworkRegistry.LevelManage)) return;
			if (_selected.IsAir)
			{
				Main.NewText(ModText.Get("MsgPickItem", "请先点选一个目标物品"), 255, 200, 150);
				return;
			}

			MECraftingCpuEntity cpu = _keepEditHost ?? UISystem.ActiveCraftingCpu;
			if (cpu == null)
			{
				UIStatus.Toast("需要连接合成终端",
					new Color(255, 200, 150));
				return;
			}

			ItemKey key = ItemKey.Crafted(_selected.Type); // 补货一律补无词缀的版本
			long target = 0;

			foreach (MECraftingCpuEntity.KeepEntry entry in cpu.Keep)
			{
				if (entry.Key.Equals(key))
				{
					target = entry.Target;
					break;
				}
			}

			long newTarget = Math.Max(1,_orderQuantity);

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.CpuKeep(cpu.Position, key, newTarget);
			}
			else if (!cpu.SetKeep(key, newTarget))
			{
				UIStatus.Toast("物品或数量无效", new Color(255, 200, 150));
				return;
			}

			UIStatus.Toast(newTarget > 0
				? ModText.Format("MsgKeepAdded", "已设为保持库存:{0} x{1}",
					UIItemNames.Display(key), newTarget)
				: ModText.Format("MsgKeepRemoved", "已取消保持库存:{0}", UIItemNames.Display(key)));

			if (newTarget > 0)
			{
				Content.Achievements.AchievementTriggers.Trigger<Content.Achievements.KeepStockAchievement>();
			}
		}

		private void Select(ItemKey key)
		{
            _keepEditHost=null;
			_selected = key;
			_info.Show(key, _orderQuantity);
		}

        private void UpdateSelectedPlan()
        {
            if (IsAccess || _selected.IsAir || !_info.IsOpen) { _selectedPlan = null; return; }
            _selectedPlan = _availability.Plan(_selected, _orderQuantity);
            string reason = _selectedPlan.FullySatisfied ? "合成树材料与配方条件已满足"
                : _selectedPlan.BlockedReasons.Count > 0 ? _selectedPlan.BlockedReasons[0]
                : _selectedPlan.Limited ? "规划尚未完成，暂不计为可合成"
                : _selectedPlan.Missing.Count > 0 ? "缺少：" + _selectedPlan.Missing[0].ItemName + " ×" + _selectedPlan.Missing[0].Amount
                : "暂无可用合成路线";
            UITextCache.Set(_craftReadiness, reason);
            _craftReadiness.TextColor = _selectedPlan.FullySatisfied ? UITheme.Ok : UITheme.Warn;
        }

		private void OrderCraft()
		{
            if (IsAccess) return;
            if (!CanConfigure(NetworkRegistry.LevelUse)) return;
			if (_selected.IsAir)
			{
				Main.NewText(ModText.Get("MsgPickItem", "请先点选一个目标物品"), 255, 200, 150);
				return;
			}
            // The command always rechecks its own inputs; its correctness does not
            // depend on a rendered details panel or a previously enabled button.
            _selectedPlan = _availability.Plan(_selected, _orderQuantity);
            if (_selectedPlan?.FullySatisfied != true)
            {
                UIStatus.Toast(_selectedPlan?.BlockedReasons.Count > 0 ? _selectedPlan.BlockedReasons[0]
                    : _selectedPlan?.Missing.Count > 0 ? "缺少：" + _selectedPlan.Missing[0].ItemName
                    : "合成树尚未满足，请查看缺料和工作站条件");
                return;
            }

			// 配方只认「无词缀」的版本 —— 带词缀的物品是成品,不是可合成目标。
			// 这里明确告诉玩家下单会造出无词缀的那一份,免得以为是 bug。
			if (_selected.Prefix != 0)
			{
				UIStatus.Toast(ModText.Get("MsgOrderDropsPrefix",
					"配方只能造出无词缀的版本,词缀要靠重铸"));
			}

			int orderType = _selected.Type;
			string name = UIItemNames.Display(_selected);

			// 多人:下单也交给服务器
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.Order(UISystem.ActiveTerminalPos, ItemKey.Crafted(orderType), _orderQuantity);
				UIStatus.Toast(ModText.Get("MsgOrderSent", "已把下单请求发给服务器:")
					+ name + " x" + _orderQuantity);
				return;
			}

			// 有独立合成 CPU 就排进队列(能排队、有进度),否则退回装配室的单一目标
			if (UISystem.ActiveCraftingCpu != null)
			{
                if (!TerminalCraftingExecutor.Submit(UISystem.ActiveCraftingCpu, ItemKey.Crafted(orderType), _orderQuantity, Main.LocalPlayer?.name, out string result))
				{
                    UIStatus.Toast(result, new Color(255, 180, 180));
					return;
				}

                UIStatus.Toast(result);
                AfterNetworkChange();
				return;
			}

			if (UISystem.ActiveAssembler == null)
			{
				Main.NewText("需要连接合成终端", 255, 160, 160);
				return;
			}

			if (_orderQuantity > 1) { UIStatus.Toast("需要连接合成终端"); return; }
            UISystem.ActiveAssembler.Target = ItemKey.Crafted(orderType);
			UISystem.ActiveAssembler.LastStatus = ModText.Get("MsgOrdered", "已下单:") + name;
			UIStatus.Toast(ModText.Get("StatusNoCpuCraft",
				"已下单(没有合成 CPU,装配室一次只做一个)") + name);
		}

		private void CycleQuantity()
		{
			_orderQuantity = _orderQuantity switch
			{
				1 => 10,
				10 => 50,
				50 => 100,
				100 => 500,
				_ => 1
			};

			_quantityButton.SetText(QuantityLabel());
            if (!IsAccess && !_selected.IsAir) _info.Show(_selected, _orderQuantity, remember: false, preserveTab: true);
			UpdateQuantityText();
			UpdateTakeLabel();
		}

		/// <summary>任意数量输入:自己读键盘的数字键 / 退格 / 回车。</summary>
		private void HandleQuantityTyping()
		{
			KeyboardState now = Keyboard.GetState();

			if (!_quantityTyping)
			{
				_prevKeys = now;
				return;
			}

			foreach (Keys key in now.GetPressedKeys())
			{
				if (_prevKeys.IsKeyDown(key))
				{
					continue;
				}

				if (key >= Keys.D0 && key <= Keys.D9)
				{
					if (_quantityBuffer.Length < 19)
					{
						_quantityBuffer += (char)('0' + (key - Keys.D0));
					}
				}
				else if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
				{
					if (_quantityBuffer.Length < 19)
					{
						_quantityBuffer += (char)('0' + (key - Keys.NumPad0));
					}
				}
				else if (key == Keys.Back)
				{
					if (_quantityBuffer.Length > 0)
					{
						_quantityBuffer = _quantityBuffer.Substring(0, _quantityBuffer.Length - 1);
					}
				}
				else if (key == Keys.Enter || key == Keys.Escape)
				{
					_quantityTyping = false;
					CommitQuantity();
					continue;
				}
			}

			_prevKeys = now;
			UpdateQuantityText();
		}

		private void CommitQuantity()
		{
			if (long.TryParse(_quantityBuffer, out long parsed) && parsed > 0)
			{
				// 上限给个合理值,免得一次规划太大卡住
				_orderQuantity = parsed;
			}
            else UIStatus.Toast("请输入有效的正整数数量");

			_quantityButton.SetText(QuantityLabel());
            if (!IsAccess && !_selected.IsAir) _info.Show(_selected, _orderQuantity, remember: false, preserveTab: true);
			UpdateQuantityText();
		}

		private void UpdateQuantityText()
		{
			if (_quantityText == null)
			{
				return;
			}

			_quantityText.SetText(_quantityTyping
				? (_quantityBuffer.Length == 0
					? ModText.Get("LabelQuantityTyping", "输入数量") + "|"
					: _quantityBuffer + "|")
				: "输入数量");
		}

        private bool MatchesCatalogue(int type) => _categories.Matches(type) && (!_favoritesOnly || BrowserFavorites.Contains(type))
            && (_modFilter == null || (ItemLoader.GetItem(type)?.Mod.Name ?? "Terraria") == _modFilter);
        private void ShowRangeMenu()
        {
            if (IsAccess) return;
            UIChoiceMenu.Show(Window, _craftableButton, RangeChoices, (index, _) =>
            {
                if (_range == index) return;
                _range = index; _craftableButton.SetText(RangeLabel()); RebuildList();
            }, selectedIndex: _range);
        }

        private string RangeLabel() => RangeChoices[_range];

        private void ShowSortMenu()
        {
            UIChoiceMenu.Show(Window, _sortButton, new[] { "默认", "名称", "数量" }, (index, _) =>
            {
                if (_sortMode == index) return;
                _sortMode = index; _sortButton.SetText(SortLabel()); RebuildList();
            }, selectedIndex: _sortMode);
        }

		/// <summary>把网络里这个物品全部取出来。</summary>
		private void WithdrawAll() { Withdraw(long.MaxValue); }

		private void Withdraw(long? amountOverride = null)
        {
            if (!UISystem.TerminalCanUse) return;
            if (_selected.IsAir || !CanTakeLocally(_selected)) return;
            long wanted = amountOverride ?? (_orderQuantity > 1 ? _orderQuantity : MaxStackOf(_selected.Type));
            if (Main.netMode == NetmodeID.MultiplayerClient) { MENet.Withdraw(UISystem.ActiveTerminalPos,_selected,wanted); return; }
            long taken = StorageOperations.Withdraw(UISystem.ActiveNetwork,_selected,wanted,Main.LocalPlayer);
            AfterNetworkChange();
            UIStatus.Toast("已取出 " + taken + " 件；未取出的余量仍在网络中");
        }

		private static int MaxStackOf(int type)
		{
			return SavedItemGuard.MaxStackOf(type);
		}

		/// <summary>
		/// 本机(单人 / 主机)能不能取这件东西。
		/// 多人下这条由服务端统一拦,但单人里没有服务端往返,所以要在这里也判一次 ——
		/// 否则「锁定」在自己是客机的存档里形同虚设。
		/// </summary>
		private static bool CanTakeLocally(ItemKey key)
		{
			if (key.IsAir || UISystem.ActiveNetwork == null || UISystem.ActiveNetwork.Count == 0)
			{
				return true;
			}

			Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(
				UISystem.ActiveTerminalPos.X, UISystem.ActiveTerminalPos.Y));

			// 没有所有者 / 没有终端坐标的场合不拦
			if (string.IsNullOrEmpty(NetworkRegistry.GetOwner(anchor)))
			{
				return true;
			}

			if (!NetworkRegistry.IsLocked(anchor, key.Type))
			{
				return true;
			}

			string me = Main.LocalPlayer?.name;
			if (!string.IsNullOrEmpty(me)
				&& NetworkRegistry.GetLevel(anchor, me) >= NetworkRegistry.LevelManage)
			{
				return true;
			}

			UIStatus.Toast(ModText.Get("MsgItemLocked", "这件物品被锁定了,只有所有者 / 管理者能取"),
				new Color(255, 170, 150));
			return false;
		}

		private static bool IsShiftDown()
		{
			return Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		}

		private static bool IsCtrlDown()
		{
			return Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);
		}

		private static bool IsAltDown()
		{
			return Main.keyState.IsKeyDown(Keys.LeftAlt) || Main.keyState.IsKeyDown(Keys.RightAlt);
		}

		/// <summary>批量:把选中的每一种各取一个堆叠出来。</summary>
		private void WithdrawSelected()
        {
            if (!UISystem.TerminalCanUse) return;
            long total = 0;
            foreach (ItemKey key in new List<ItemKey>(_selection))
            {
                if (!CanTakeLocally(key)) continue;
                long wanted = Math.Min(UISystem.GetStoredAmount(key), MaxStackOf(key.Type));
                if (Main.netMode == NetmodeID.MultiplayerClient) MENet.Withdraw(UISystem.ActiveTerminalPos,key,wanted);
                else total += StorageOperations.Withdraw(UISystem.ActiveNetwork,key,wanted,Main.LocalPlayer);
            }
            AfterNetworkChange();
            UIStatus.Toast(Main.netMode == NetmodeID.MultiplayerClient ? "批量取出请求已发送" : "已按所选词缀取出 " + total + " 件");
        }

        private void UpdateSelectionButtons()
		{
			if (_takeSelectedButton == null)
			{
				return;
			}

			_takeSelectedButton.SetText(_selection.Count == 0
				? ModText.Get("BtnTakeSelected", "取出选中")
				: ModText.Get("BtnTakeSelected", "取出选中") + " (" + _selection.Count + ")");
		}

		/// <summary>Shift+左键在网格上 = 取一整叠。</summary>
		private long OverrideStack()
		{
			return MaxStackOf(_selected.Type);
		}

		/// <summary>Shift+左键在背包格上 = 把这一格直接存进网络。</summary>
		/// <summary>Shift+右键在背包格上 = 把背包里**这一种**全部存进网络。</summary>
		private bool DepositAllOfType(Item item, int slotIndex)
		{
            if (!UISystem.TerminalCanUse) return true;
			if (item == null || item.type <= 0)
			{
				return true;
			}

			// 多人:服务端执行(它扫的是玩家背包,和这里一致)
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.DepositScope(UISystem.ActiveTerminalPos, slotIndex, ItemKey.FromItem(item), true, _depositAmmoAndCoins);
				UIStatus.Toast(ModText.Get("MsgDepositSent", "已把存入请求发给服务器"));
				return true;
			}

			if (UISystem.ActiveNetwork == null || UISystem.ActiveNetwork.Count == 0)
			{
				Main.NewText(ModText.Get("MsgNoDrive", "没有连接的磁盘驱动器"), 255, 180, 180);
				return true;
			}

			int type = item.type;
			long moved = 0;
			Player player = Main.LocalPlayer;
			int slots = InventoryScope.DepositSlotCount(player, _depositAmmoAndCoins);

			for (int i = 0; i < slots; i++)
			{
				Item slot = player.inventory[i];

				if (slot == null || slot.type != type || slot.stack <= 0 || slot.favorited)
				{
					continue;
				}

				moved += MENetwork.Deposit(UISystem.ActiveNetwork, slot);
			}

			if (moved > 0)
			{
				AfterNetworkChange();
				UIStatus.Toast(ModText.Get("MsgStored", "已存入") + " " + moved + " "
					+ ModText.Get("MsgStoredTail", "件物品"));
			}

            else UIStatus.Toast("未存入：" + StorageOperations.DepositRemainderReason(UISystem.ActiveNetwork, player,
                _depositAmmoAndCoins, onlyType: type));

			return true;
		}

        internal bool DepositInventorySlot(int slotIndex) => DepositSlot(Main.LocalPlayer.inventory[slotIndex], slotIndex);
        private void DepositHeldItem()
        {
            if (!UISystem.TerminalCanUse || MENet.CursorPending || MENet.InventoryTransactionPending) return;
            if (Main.netMode == NetmodeID.MultiplayerClient) { MENet.DepositCursor(UISystem.ActiveTerminalPos); return; }
            var preview = StorageOperations.PreviewDeposit(UISystem.ActiveNetwork, Main.mouseItem);
            if (preview.Accepted <= 0) { UIStatus.Toast(preview.Message); return; }
            long moved=MENetwork.Deposit(UISystem.ActiveNetwork,Main.mouseItem);
            Main.LocalPlayer.inventory[58]=Main.mouseItem.Clone();
            if (moved>0) { AfterNetworkChange(); UIStatus.Toast("已存入 "+moved+" 件" + (moved < preview.Requested ? "；其余留在手中。" + preview.Reason : "")); }
            else UIStatus.Toast("存储空间不足或过滤规则拒绝此物品");
        }
        private bool DepositSlot(Item item, int slotIndex)
		{
            if (!UISystem.TerminalCanUse)
            {
                UIStatus.Toast(string.IsNullOrEmpty(UISystem.TerminalAccessStatus)
                    ? "需要网络使用权限才能存入物品" : UISystem.TerminalAccessStatus);
                return true;
            }
			if (item == null || item.type <= 0 || item.stack <= 0 || item.favorited)
			{
				return true;
			}

			if (UISystem.ActiveNetwork == null || UISystem.ActiveNetwork.Count == 0)
			{
				// 本地没有网络实体(远程终端)时交给服务器
				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.DepositScope(UISystem.ActiveTerminalPos, slotIndex, ItemKey.FromItem(item), false, false);
					return true;
				}

				Main.NewText(ModText.Get("MsgNoDrive", "没有连接的磁盘驱动器"), 255, 180, 180);
				return true;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.DepositScope(UISystem.ActiveTerminalPos, slotIndex, ItemKey.FromItem(item), false, false);
				return true;
			}

            var preview = StorageOperations.PreviewDeposit(UISystem.ActiveNetwork, item);
            if (preview.Accepted <= 0) { UIStatus.Toast(preview.Message); return true; }
			long moved = MENetwork.Deposit(UISystem.ActiveNetwork, item);
			if (moved > 0)
			{
				UIStatus.Toast(ModText.Get("MsgStored", "已存入") + " " + moved + " "
					+ ModText.Get("MsgStoredTail", "件物品"));
				AfterNetworkChange();
			}

            else UIStatus.Toast("未存入：请检查存储容量或过滤规则，物品保留在背包");

			return true;
		}

		/// <summary>取出时把词缀一起还原 —— 以前只按物品ID取,词缀就丢了。</summary>
		private static void SpawnWithPrefix(ItemKey key, int stack)
		{
			if (stack <= 0)
			{
				return;
			}

			int index = Item.NewItem(new EntitySource_Misc("AE2ForTerraria"),
				(int)Main.LocalPlayer.Center.X, (int)Main.LocalPlayer.Center.Y, 1, 1, key.Type, stack);

			if (key.Prefix > 0 && index >= 0 && index < Main.maxItems && Main.item[index] != null)
			{
				UIItemNames.ApplyPrefix(Main.item[index], key.Prefix);
				// 词缀会改数值,同步给其它客户端
				if (Main.netMode == NetmodeID.Server)
				{
					NetMessage.SendData(MessageID.SyncItem, -1, -1, null, index);
				}
			}
		}

		private void DepositAll()
        {
            if (!UISystem.TerminalCanUse) return;
            if (Main.netMode == NetmodeID.MultiplayerClient) { MENet.DepositAll(UISystem.ActiveTerminalPos,_depositAmmoAndCoins); return; }
            long moved = StorageOperations.DepositInventory(UISystem.ActiveNetwork,Main.LocalPlayer,_depositAmmoAndCoins);
            AfterNetworkChange();
            UIStatus.Toast(moved > 0 ? "已存入 " + moved + " 件；收藏与未存入的物品保留在背包"
                : "未存入：" + StorageOperations.DepositRemainderReason(UISystem.ActiveNetwork, Main.LocalPlayer, _depositAmmoAndCoins));
        }

        private void AfterNetworkChange()
		{
			UISystem.RefreshNetwork();
			RefreshLockAnchor();
			RebuildList();
            _availability.Refresh(); UpdateSelectedPlan();
            if (_info.IsOpen && !_selected.IsAir)
                _info.Show(_selected, _orderQuantity, remember: false, preserveTab: true);
		}

		/// <summary>把当前网络的锚点缓下来,角标判断用(避免每格一次 BFS)。</summary>
		private void RefreshLockAnchor()
		{
			if (UISystem.ActiveNetwork == null || UISystem.ActiveNetwork.Count == 0
				|| UISystem.ActiveTerminalPos.X < 0)
			{
				_lockAnchor = new Point16(-1, -1);
				return;
			}

			_lockAnchor = MENetwork.CanonicalAnchor(MENetwork.Collect(
				UISystem.ActiveTerminalPos.X, UISystem.ActiveTerminalPos.Y));
		}

		/// <summary>
		/// 悬停提示:物品名 + 网络存量 + 配方数量。
		/// 以前只能从右侧面板看,而且得先点选。
		/// </summary>
		protected override void DrawHoverTooltip(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
		{
            if (!Main.mouseItem.IsAir && _grid.Parent != null && _grid.ContainsPoint(Main.MouseScreen))
            {
                string message = Main.netMode == NetmodeID.MultiplayerClient
                    ? "存入时由服务器核验容量；未能存入的物品保留在手中"
                    : StorageOperations.PreviewDeposit(UISystem.ActiveNetwork, Main.mouseItem).Message;
                UITooltip.Draw(spriteBatch, "存入网络", message); return;
            }
			_info?.DrawTooltip(spriteBatch);
            _searchBox?.DrawCandidates();
            _grid?.DrawTooltip(spriteBatch, key =>
			{
				if (StorageSystem.Recipes == null)
				{
					return null;
				}

				int recipes = StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(key.Type)).Count;
                if (!IsAccess) return _availability.Catalogue.IsCraftable(key.Type)
                    ? "完整合成树已满足 · 左键查看配方；Shift＋左键取出一叠" : "左键查看配方与缺料；Shift＋左键取出一叠";
				return recipes > 0
					? ModText.Get("LblRecipeCount", "配方数量") + ": " + recipes
					: ModText.Get("LblNoRecipeShort", "无配方(只能靠掉落/商店)");
			});
		}
	}
}
