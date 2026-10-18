using AE2ForTerraria.Core;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 物品格子列表(JEI 风格)。分页显示,支持悬停高亮。
	/// </summary>
	public sealed class UIItemGrid : UIElement
	{
		/// <summary>
		/// 格子边长。34 是照着 Recipe Browser 的密度来的(它的格子是 42×0.75 ≈ 31.5):
		/// 浏览器默认只有 475×350,格子太大一屏就放不下几个东西。
		/// </summary>
		public const int SlotSize = 34;
        public int CellSize = SlotSize;
        public bool NpcLabels;
        private int CellWidth => NpcMode && NpcLabels ? 104 : CellSize;
        private int CellHeight => NpcMode && NpcLabels ? 78 : CellSize;
        public Func<bool> InputBlocked;
        public Func<ItemKey, bool> IsFavorite;
        public int Revision { get; private set; }

		private readonly List<int> _items = new List<int>();
		private readonly List<ItemKey> _keys = new List<ItemKey>();

		/// <summary>
		/// true = 这一页是「物品ID + 词缀」的键列表(终端 / 驱动器),
		/// false = 只是物品ID(浏览器 / 怪物页)。
		/// </summary>
		public bool KeyMode { get; private set; }

		public int Page { get; private set; }

		/// <summary>
		/// 当前滚动到第几行(0 = 最上面一行)。网格是**连续滚动**的,
		/// 不是一页一页跳 —— 滚轮动一下只走几行,和 Recipe Browser 一样。
		/// </summary>
		private int _rowOffset;

		/// <summary>一格滚轮走几行。</summary>
		private const int RowsPerWheelNotch = 3;

		public int HoveredType { get; private set; } = -1;

		/// <summary>当前悬停格子的完整身份(带词缀)。</summary>
		public ItemKey HoveredKey { get; private set; } = ItemKey.None;

		/// <summary>
		/// 键盘光标所在的格子(`-1` = 没有)。
		/// 鼠标划过也会把光标带过去,所以混合操作不会「两个高亮」。
		/// </summary>
		public int CursorIndex { get; private set; } = -1;

		/// <summary>
		/// 键盘导航是否生效。宿主在「搜索框正在输入」时把它关掉,
		/// 否则方向键会被两边同时吃掉。
		/// </summary>
		public bool KeyboardEnabled = true;
        public bool RecipeShortcutRequiresHover;

		/// <summary>回车激活(参数是完整身份)。名字故意不叫 OnActivate —— 那是 UIElement 自己的激活回调。</summary>
		public Action<ItemKey> OnActivated;
        public Action<ItemKey> OnUsed;

		/// <summary>双击事件供显式绑定的调用方使用；终端不绑定取物。</summary>
		public Action<ItemKey> OnDoubleClicked;

		private ItemKey _lastClickKey = ItemKey.None;
		private double _lastClickTime = -10.0;

		private Microsoft.Xna.Framework.Input.KeyboardState _prevKeys;

		public override void OnInitialize()
		{
			base.OnInitialize();
            OverflowHidden = true;
            var slots = new SlotCanvas(this) { IgnoresMouseInteraction = true };
            slots.Width.Set(0, 1); slots.Height.Set(0, 1); Append(slots);

			OnLeftClick += (evt, element) =>
			{
                if (FavoriteInput.IsDown) { _lastClickKey = ItemKey.None; _lastClickTime = -10; return; }
				ItemKey key = ActiveKey;

				if (key.IsAir)
				{
					return;
				}

				double now = Systems.UIStatus.Now;
				bool doubleClick = key.Equals(_lastClickKey) && now - _lastClickTime <= 0.35;

				_lastClickKey = doubleClick ? ItemKey.None : key; // 三连击不重复触发
				_lastClickTime = now;

				if (doubleClick)
				{
					OnDoubleClicked?.Invoke(key);
				}
			};
		}

		/// <summary>当前光标格子(没光标就用鼠标悬停的那个)。</summary>
		public ItemKey ActiveKey
		{
			get
			{
				if (CursorIndex >= 0 && CursorIndex < EntryCount)
				{
					return KeyAt(CursorIndex);
				}

				return HoveredKey;
			}
		}

		/// <summary>可选的存量显示(合成终端里用)。按完整身份取数量。</summary>
		public Func<ItemKey, long> CountProvider;

		/// <summary>
		/// 可选的「已选中」标记(过滤清单里用)。返回 true 的格子会加深底色并显示角标,
		/// 这样点一下就能看出勾没勾上。
		/// </summary>
		public Func<ItemKey, bool> IsMarked;

		/// <summary>
		/// 格子右上角的小角标(比如终端里「维持库存」的 ∞)。
		/// 返回 null / 空串就不画。
		/// </summary>
		public Func<ItemKey, string> BadgeProvider;

		/// <summary>true = 画怪物图标,false = 画物品图标。</summary>
		public bool NpcMode;

		public int Columns { get; private set; } = 1;

		public int VisibleRows { get; private set; } = 1;

		public int TotalRows { get; private set; } = 1;

		public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)Math.Max(1, VisibleRows)));

		/// <summary>当前列表里的条目数。</summary>
		public int EntryCount => KeyMode ? _keys.Count : _items.Count;

		private int TypeAt(int index)
		{
			return KeyMode ? _keys[index].Type : _items[index];
		}

		private ItemKey KeyAt(int index)
		{
			return KeyMode ? _keys[index] : ItemKey.Crafted(_items[index]);
		}

		/// <summary>还能往下滚多少行(内容比一屏矮时是 0)。</summary>
		public int MaxRowOffset => Math.Max(0, TotalRows - VisibleRows);

		/// <summary>滚动位置 0..1(滚动条用)。</summary>
		public float ScrollRatio => MaxRowOffset <= 0
			? 0f
			: _rowOffset / (float)MaxRowOffset;

		/// <summary>一屏占内容的比例(滚动条滑块长度用)。</summary>
		public float VisibleRatio => TotalRows <= 0
			? 1f
			: MathHelper.Clamp(VisibleRows / (float)TotalRows, 0.05f, 1f);

		/// <summary>按比例滚动(拖动滚动条)。</summary>
		public void ScrollToRatio(float ratio)
		{
			_rowOffset = (int)Math.Round(MathHelper.Clamp(ratio, 0f, 1f) * MaxRowOffset);
			ClampScroll();
		}

		/// <summary>按行滚动(正数 = 往下翻)。</summary>
		public void ScrollRows(int rows)
		{
			if (rows == 0)
			{
				return;
			}

			_rowOffset += rows;
			ClampScroll();
		}

		private void ClampScroll()
		{
			_rowOffset = Math.Clamp(_rowOffset, 0, MaxRowOffset);
			Page = VisibleRows <= 0 ? 0 : Math.Min(PageCount - 1, _rowOffset / VisibleRows);
		}

		/// <summary>
		/// 鼠标悬停时在光标旁画出「物品名 / 怪物名 + 数量」。
		/// 由窗口在 Draw 的最后调用,保证压在所有元素上面。
		/// </summary>
		public void DrawTooltip(SpriteBatch spriteBatch, System.Func<ItemKey, string> extraLine = null)
		{
			if (HoveredType <= 0 || InputBlocked?.Invoke() == true || !ContainsPoint(Main.MouseScreen))
			{
				return;
			}

			if (!NpcMode) { BrowserHover.Item(HoveredKey); return; }

            string title = NpcMode
				? Systems.NpcIndex.GetName(HoveredType)
				: UIItemNames.Display(HoveredKey.IsAir ? ItemKey.Crafted(HoveredType) : HoveredKey);

			if (string.IsNullOrWhiteSpace(title))
			{
				title = "#" + HoveredType;
			}

			string sub = NpcVisuals.ModName(HoveredType);

			// 出处模组:35 个模组的环境里,同名物品靠这个区分
			if (!NpcMode)
			{
				string modName = UIItemNames.ModNameOf(HoveredType);
				if (!string.IsNullOrEmpty(modName))
				{
					sub = Systems.ModText.Get("TipFromMod", "来自") + ": " + modName;
				}
			}

			if (!NpcMode && CountProvider != null)
			{
				long count = CountProvider(HoveredKey.IsAir ? ItemKey.Crafted(HoveredType) : HoveredKey);
				string stored = Systems.ModText.Get("TipStored", "网络存量") + ": " + UIFormat.Count(count);
				sub = string.IsNullOrEmpty(sub) ? stored : stored + "   " + sub;
			}

			if (extraLine != null)
			{
				string extra = extraLine(HoveredKey.IsAir ? ItemKey.Crafted(HoveredType) : HoveredKey);
				if (!string.IsNullOrWhiteSpace(extra))
				{
					sub = string.IsNullOrEmpty(sub) ? extra : sub + "   " + extra;
				}
			}

			UITooltip.Draw(spriteBatch, title, sub);
		}

		/// <summary>
		/// 换一批内容。resetPage = false 时保留当前页 ——
		/// 库存被后台刷新时不该把玩家正在看的页面跳回第一页。
		/// </summary>
		public void SetItems(List<int> items, bool resetPage = true)
		{
			Revision++;
            KeyMode = false;
			_keys.Clear();
			_items.Clear();

			if (items != null)
			{
				_items.AddRange(items);
			}

			if (resetPage)
			{
				_rowOffset = 0;
				Page = 0;
				CursorIndex = -1;
			}

			RecalcLayout();
		}

		/// <summary>
		/// 换一批「物品ID + 词缀」的键。
		/// 同一个物品的不同词缀是**两份**东西(和 AE2 的 NBT 一样),
		/// 所以这里不去重,靠 tooltip 把词缀显示出来区分。
		/// </summary>
		public void SetKeys(List<ItemKey> keys, bool resetPage = true)
		{
			Revision++;
            KeyMode = true;
			_items.Clear();
			_keys.Clear();

			if (keys != null)
			{
				_keys.AddRange(keys);
			}

			if (resetPage)
			{
				_rowOffset = 0;
				Page = 0;
			}

			RecalcLayout();
		}

		public void NextPage()
		{
			ScrollRows(Math.Max(1, VisibleRows));
		}

		public void PrevPage()
		{
			ScrollRows(-Math.Max(1, VisibleRows));
		}

		/// <summary>直接跳到第几页。</summary>
		public void SetPage(int page)
		{
			_rowOffset = Math.Clamp(page, 0, PageCount - 1) * Math.Max(1, VisibleRows);
			ClampScroll();
		}

		private void RecalcLayout()
		{
			CalculatedStyle dims = GetDimensions();
			Columns = Math.Max(1, (int)(dims.Width / CellWidth));
			VisibleRows = Math.Max(1, (int)(dims.Height / CellHeight));
			TotalRows = Math.Max(1, (int)Math.Ceiling(EntryCount / (double)Columns));

			ClampScroll();
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);
			HoveredType = -1;
            HoveredKey = ItemKey.None;
			RecalcLayout();

			HandleKeyboard();

			if (InputBlocked?.Invoke() == true || !ContainsPoint(Main.MouseScreen))
			{
				return;
			}

			// 悬停时不要把点击透传给世界
			Main.LocalPlayer.mouseInterface = true;

			// 滚轮滚动:往下滚 = 往后翻(和 Recipe Browser、网页一样)。
			// 以前这里写反了 —— 往下滚反而往回走。
			int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
			if (wheel != 0)
			{
				ScrollRows(-wheel * RowsPerWheelNotch);

				// 吃掉这一帧的滚轮,免得同一个滚轮又被别的界面元素处理一次
				Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;

				// 也别让游戏本体拿这个滚轮去切物品栏
				Terraria.GameInput.PlayerInput.LockVanillaMouseScroll("AE2ForTerraria");
				RecalcLayout();
			}

			int index = EntryIndexAt(Main.MouseScreen);
			if (index >= 0 && index < EntryCount)
			{
				HoveredType = TypeAt(index);
				HoveredKey = KeyAt(index);
                if (IsFavorite != null) FavoriteInput.Cursor();

				// 鼠标划过也把光标带过去,键盘接管时不会跳回旧位置
				CursorIndex = index;
			}
		}

		/// <summary>Hit-test current layout rather than last frame's hover state (also used for blank-area dragging).</summary>
		public bool HasEntryAt(Vector2 point) => EntryIndexAt(point) >= 0;
		private int EntryIndexAt(Vector2 point)
		{
			if (!ContainsPoint(point)) return -1;
			RecalcLayout();
			var dims = GetDimensions();
			int col = (int)((point.X - dims.X) / CellWidth), row = (int)((point.Y - dims.Y) / CellHeight);
			if (col < 0 || col >= Columns || row < 0 || row >= Math.Ceiling(dims.Height / CellHeight)) return -1;
			int index = (_rowOffset + row) * Columns + col;
			return index >= 0 && index < EntryCount ? index : -1;
		}

		/// <summary>
		/// 方向键移动光标、Enter 打开详情、PageUp/PageDown 翻页。
		/// 以前整个网格只能用鼠标点。
		/// </summary>
		private void HandleKeyboard()
		{
			var now = RecipeShortcutRequiresHover ? Main.keyState : Microsoft.Xna.Framework.Input.Keyboard.GetState();

			if (!KeyboardEnabled || EntryCount == 0 || InputBlocked?.Invoke() == true)
			{
				_prevKeys = now;
				return;
			}

			bool pressed(
				Microsoft.Xna.Framework.Input.Keys key) =>
				now.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

			if (pressed(Microsoft.Xna.Framework.Input.Keys.PageUp))
			{
				PrevPage();
				MoveCursorToTopRow();
				_prevKeys = now;
				return;
			}

			if (pressed(Microsoft.Xna.Framework.Input.Keys.PageDown))
			{
				NextPage();
				MoveCursorToTopRow();
				_prevKeys = now;
				return;
			}

			if (CursorIndex < 0)
			{
				CursorIndex = _rowOffset * Columns;
			}

			int columns = Math.Max(1, Columns);
			int before = CursorIndex;

			if (pressed(Microsoft.Xna.Framework.Input.Keys.Left))
			{
				CursorIndex--;
			}
			else if (pressed(Microsoft.Xna.Framework.Input.Keys.Right))
			{
				CursorIndex++;
			}
			else if (pressed(Microsoft.Xna.Framework.Input.Keys.Up))
			{
				CursorIndex -= columns;
			}
			else if (pressed(Microsoft.Xna.Framework.Input.Keys.Down))
			{
				CursorIndex += columns;
			}
			else if (pressed(Microsoft.Xna.Framework.Input.Keys.Home))
			{
				CursorIndex = 0;
			}
			else if (pressed(Microsoft.Xna.Framework.Input.Keys.End))
			{
				CursorIndex = EntryCount - 1;
			}
			else if (pressed(Microsoft.Xna.Framework.Input.Keys.U))
            {
                OnUsed?.Invoke(ActiveKey); _prevKeys = now; return;
            }
            else if (pressed(Microsoft.Xna.Framework.Input.Keys.R))
			{
                int index = EntryIndexAt(Main.MouseScreen);
                if (!RecipeShortcutRequiresHover) OnActivated?.Invoke(ActiveKey);
                else if (index >= 0) OnActivated?.Invoke(KeyAt(index));
                _prevKeys = now;
                return;
			}
            else if (pressed(Microsoft.Xna.Framework.Input.Keys.Enter))
			{
				OnActivated?.Invoke(ActiveKey);
				_prevKeys = now;
				return;
			}

			if (CursorIndex != before)
			{
				ClampCursor();
			}

			_prevKeys = now;
		}

		/// <summary>光标越界时自动翻页(走到一页末尾按 → 就翻到下一页)。</summary>
		private void ClampCursor()
		{
			CursorIndex = Math.Clamp(CursorIndex, 0, Math.Max(0, EntryCount - 1));
			EnsureCursorVisible();
		}

		/// <summary>把光标放到当前屏幕的第一行(PgUp/PgDn 之后用)。</summary>
		private void MoveCursorToTopRow()
		{
			CursorIndex = _rowOffset * Columns;
			ClampCursor();
		}

		/// <summary>光标那一行必须在屏幕里,不在就滚过去(键盘选格时自动跟随)。</summary>
		private void EnsureCursorVisible()
		{
			if (CursorIndex < 0 || Columns <= 0)
			{
				return;
			}

			int row = CursorIndex / Columns;

			if (row < _rowOffset)
			{
				_rowOffset = row;
			}
			else if (row >= _rowOffset + VisibleRows)
			{
				_rowOffset = row - VisibleRows + 1;
			}

			ClampScroll();
		}


		private void DrawSlots(SpriteBatch spriteBatch)
		{
            RecalcLayout();
			CalculatedStyle dims = GetDimensions();
			int perPage = Math.Max(1, (int)Math.Ceiling(dims.Height / CellHeight) * Columns);
			int start = _rowOffset * Columns;

			for (int i = 0; i < perPage; i++)
			{
				int index = start + i;
				if (index >= EntryCount)
				{
					break;
				}

				int col = i % Columns;
				int row = i / Columns;
				var position = new Vector2(dims.X + col * CellWidth, dims.Y + row * CellHeight);
				DrawSlot(spriteBatch, position, TypeAt(index), KeyAt(index));
			}
		}

        // UIElement clips children using the native UI transform and restores SpriteBatch state.
        private sealed class SlotCanvas(UIItemGrid owner) : UIElement
        {
            protected override void DrawSelf(SpriteBatch batch) => owner.DrawSlots(batch);
        }

		private void DrawSlot(SpriteBatch spriteBatch, Vector2 position, int itemType, ItemKey key)
		{
			var box = new Rectangle((int)position.X + 2, (int)position.Y + 2, CellWidth - 5, CellHeight - 5);
			bool hovered = key.Equals(HoveredKey);
            if (hovered && IsFavorite != null) FavoriteInput.Cursor();

			bool marked = IsMarked != null && IsMarked(key);
			bool cursored = key.Equals(ActiveKey) && CursorIndex >= 0 && !hovered;
			Color fill = marked || cursored ? UITheme.Btn : hovered ? UITheme.BtnHot : UITheme.Slot;
			if (NpcMode)
			{
				// Native inventory backgrounds give the compact bestiary the same
				// rounded cells as Recipe Browser, without copying its assets or code.
				var background = marked || cursored || hovered ? TextureAssets.InventoryBack15.Value : TextureAssets.InventoryBack9.Value;
				spriteBatch.Draw(background, box, Color.White);
			}
			else
			{
				UITheme.Fill(spriteBatch, box, fill);
				UITheme.Outline(spriteBatch, box, UITheme.ItemBorder, 1);
			}

			if (marked)
			{
				UITheme.Fill(spriteBatch, new Rectangle(box.X + 2, box.Y + 2, 4, 4), UITheme.Select);
			}

			// 角标:全部用画的形状(字体里没有 emoji)
			string badge = BadgeProvider?.Invoke(key);
			if (!string.IsNullOrEmpty(badge))
			{
				if (badge.Contains("lock"))
				{
					UITheme.DrawLock(spriteBatch, box.X + 3, box.Y + 4);
				}

				if (badge.Contains("keep"))
				{
					UITheme.DrawKeep(spriteBatch, box.Right - 10, box.Y + 4);
				}
			}

			Texture2D texture;
			Rectangle frame;

			if (NpcMode)
			{
				Main.instance.LoadNPC(itemType);
				texture = TextureAssets.Npc[itemType].Value;
				int frames = Main.npcFrameCount != null && itemType < Main.npcFrameCount.Length
					? Math.Max(1, Main.npcFrameCount[itemType])
					: 1;
				frame = texture != null ? texture.Frame(1, frames, 0, 0) : Rectangle.Empty;
			}
			else
			{
				Main.instance.LoadItem(itemType);
                texture = TextureAssets.Item[itemType].Value;
				frame = texture == null
					? Rectangle.Empty
					: Main.itemAnimations[itemType] != null
						? Main.itemAnimations[itemType].GetFrame(texture, 0)
						: texture.Frame(1, 1, 0, 0);
			}

			if (texture == null)
			{
				return;
			}

			float scale = 1f;
			int longest = Math.Max(frame.Width, frame.Height);
			int iconLimit = NpcMode && NpcLabels ? 45 : CellSize - 8;
			if (longest > iconLimit)
			{
				scale = iconLimit / (float)longest;
			}

			var center = new Vector2(box.X + box.Width / 2f, box.Y + (NpcMode && NpcLabels ? 27 : box.Height / 2f));
			if (NpcMode) NpcVisuals.Draw(spriteBatch, itemType, new Rectangle((int)(center.X - iconLimit / 2f), (int)(center.Y - iconLimit / 2f), iconLimit, iconLimit));
            else spriteBatch.Draw(texture, center, frame, Color.White, 0f,
                new Vector2(frame.Width / 2f, frame.Height / 2f), scale, SpriteEffects.None, 0f);

			if (NpcMode && NpcLabels)
            {
                string name = Systems.NpcIndex.GetName(itemType);
                while (name.Length > 1 && FontAssets.MouseText.Value.MeasureString(name).X * .65f > box.Width - 8) name = name.Substring(0, name.Length - 1);
                Utils.DrawBorderString(spriteBatch, name, new Vector2(box.Center.X, box.Bottom - 18), UITheme.Text, .65f, .5f);
            }
            if (CountProvider != null)
			{
				long count = CountProvider(key);
				if (count > 0)
				{
					// 数量缩写 + 半透明底衬,五位数也不会糊成一团
					string text = UIFormat.Count(count);
					Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * 0.55f;

					var backing = new Rectangle(
						(int)(box.Right - 3f - size.X - 3f), (int)(box.Bottom - 3f - size.Y - 1f),
						(int)size.X + 5, (int)size.Y + 2);

					UITheme.Fill(spriteBatch, backing, new Color(8, 10, 18) * 0.55f);
					Utils.DrawBorderString(spriteBatch, text,
						new Vector2(box.Right - 5f - size.X, box.Bottom - 4f - size.Y),
						new Color(235, 240, 250), 0.55f, 0f, 0f);
				}
			}
            if (IsFavorite?.Invoke(key) == true) FavoriteInput.Mark(spriteBatch, box);
		}
	}
}
