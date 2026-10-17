using System;
using System.Linq;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 一个窗口的位置和大小。做成静态实例,这样关掉再打开还能保持你拖好的样子。
	/// </summary>
	public sealed class WindowGeometry
	{
		public float X = -1f;
		public float Y = -1f;
		public float W;
		public float H;
		public float MinW;
		public float MinH;

		/// <summary>设计时的默认尺寸,「重置本窗口布局」要回到这里。</summary>
		public float DefaultW;
		public float DefaultH;

		/// <summary>持久化用的键(默认取窗口类名)。</summary>
		public string Key;

        // Recipe Browser's default body size, in Terraria UI coordinates.
        public static WindowGeometry Compact() => new(475f, 350f, 415f, 300f);

		public WindowGeometry(float width, float height, float minWidth = 400f, float minHeight = 240f)
		{
			W = width;
			H = height;
			MinW = minWidth;
			MinH = minHeight;
			DefaultW = width;
			DefaultH = height;
		}

		public void ResetPosition()
		{
			X = -1f;
			Y = -1f;
		}
	}

	/// <summary>
	/// 可拖动、可缩放的窗口基类:自带标题栏(拖动)、右上角关闭按钮、右下角缩放手柄。
	/// 子类只管往 Window 里塞控件,并在 ApplyLayout() 里按 Geo.W / Geo.H 摆放。
	/// </summary>
	public abstract class MovableWindowState : UIState
	{
		protected const float TitleHeight = 26f;

		/// <summary>
		/// 内容离窗口边缘的留白。**必须比底板那圈边框(12px)大一点** ——
		/// 不然按钮会压在边框上,看起来像跑到窗口外面去了。
		/// </summary>
		protected const float Pad = 8f;

		/// <summary>边框拖拽区厚度(拖这里 = 移动 / 缩放)。</summary>
		protected const float FrameGrip = 10f;

		protected UIWindowPanel Window { get; private set; }

		protected WindowGeometry Geo { get; private set; }

		/// <summary>窗口几何。子页面定位 / 分离时读它。</summary>
		public WindowGeometry Layout => Geo;

        internal Rectangle InteractionBounds => Window?.GetDimensions().ToRectangle() ?? Rectangle.Empty;
        internal static Vector2 ViewportSize => PlayerInput.OriginalScreenSize / Main.UIScale;

		/// <summary>true = 没有标题栏(全靠拖边框移动 / 缩放)。</summary>
		public bool Frameless { get; private set; }

		private float _lastScreenWidth, _lastScreenHeight;
        private bool _updatingChildren, _layoutPending;
        private Action _afterChildren;
        private UIDragBar _dragBar;
		private UIResizeGrip _grip;
		private UIIconButton _closeButton;
		private UITextPanel<string> _resetButton;
		private UIFrameGrip _frameTop;
		private UIFrameGrip _frameLeft;
		private UIFrameGrip _frameRight;
		private UIFrameGrip _frameBottom;
		private UIFrameGrip _frameCorner;

		/// <summary>无标题栏窗口里「重置 / 关闭」那一行的上边距与高度(和内容第一行对齐)。</summary>
		private float _chromeTop = 5f;
		private float _chromeHeight = 22f;

		/// <summary>带搜索框的窗口可以覆写成实际焦点状态。</summary>
		public virtual bool SearchBoxFocused => false;
		protected virtual bool CornerResizeOnly => true;
		protected virtual bool HighlightFrame => false;
		protected virtual float ResetButtonWidth => 42f;
		protected virtual float ChromeGap => 4f;
		protected virtual float FramelessChromeHeight => 22f;

        // A grip updates inside Window.Children enumeration. Layouts may reparent
        // controls, so flush them only after the complete child update has returned.
        protected void RequestLayout()
        {
            if (_updatingChildren) _layoutPending = true;
            else ApplyLayout();
        }
        protected void KeepResizeGripInFront()
        {
            if (_grip == null || Window.Children.LastOrDefault() == _grip) return;
            _grip.Remove(); Window.Append(_grip); _grip.Recalculate();
        }
        internal void AfterChildUpdate(Action change)
        {
            if (_updatingChildren) _afterChildren += change;
            else change();
        }

		/// <summary>
		/// 把一个子元素也当成"窗口空位":点它按住左键同样能拖动窗口。
		/// 状态文字这类不接收点击的元素适合加进来。
		/// </summary>
		protected void AddDragTarget(UIElement element)
		{
			if (element != null && Window != null && !Window.DragTargets.Contains(element))
			{
				Window.DragTargets.Add(element);
			}
		}

		/// <summary>
		/// 建窗口。frameless = true 时不画标题栏 —— 那种窗口靠**拖边框**移动 / 缩放,
		/// 标题栏那一条 32px 就省给内容了。
		/// </summary>
		protected void InitWindow(WindowGeometry geometry, string title, bool frameless = false)
		{
			Geo = geometry;
			Frameless = frameless;

			// 没有标题栏的窗口:重置 / 关闭要摆进内容区,并且和内容第一行同高
			// (以前固定在 y=5,和旁边那排按钮不在一条线上,看着歪)
			_chromeTop = frameless ? Pad : 5f;
			_chromeHeight = frameless ? FramelessChromeHeight : 22f;

			// 先把上次存档里的位置 / 大小读回来(没有记录就用设计默认值)
			if (string.IsNullOrEmpty(Geo.Key))
			{
				Geo.Key = GetType().Name;
			}
            if (!Geo.Key.EndsWith(".compact1", StringComparison.Ordinal)) Geo.Key += ".compact1";

			WindowLayout.Apply(Geo.Key, Geo);

			Window = new UIWindowPanel();
            Window.SetPadding(0f);
			Window.Target = Window;
			Window.OnMoved = RequestLayout;
			Window.HAlign = 0f;
			Window.VAlign = 0f;
			Window.BackgroundColor = UITheme.WindowBg * 0.97f;
			Window.BorderColor = UITheme.WindowBorder;
			Append(Window);

			// 先按 Geo 把元素的几何摆好,再交给 ApplyLayout。
			// 这一步不能省:ApplyLayout 开头会 SyncGeometry() 把元素上的值读回 Geo,
			// 而元素刚建出来时 Left/Top/Width/Height 都是 0 —— 于是 -1 的居中哨兵
			// 会被 0 覆盖(判定 Geo.X < 0 不成立),窗口就跑到左上角、还缩到最小尺寸。
			FitToScreen();

			// 边框四条拖拽区(最先添加,这样后面的按钮 / 内容能盖在它们上面)。
			// 拖动上边 / 左边 = 移窗口,右边 = 改宽,下边 = 改高,右下角 = 一起改。
			_frameTop = UIFrameGrip.Create(Window, UIFrameGrip.GripMode.Move, RequestLayout);
			_frameLeft = UIFrameGrip.Create(Window, UIFrameGrip.GripMode.Move, RequestLayout);
			_frameRight = UIFrameGrip.Create(Window, CornerResizeOnly ? UIFrameGrip.GripMode.Move : UIFrameGrip.GripMode.Width, RequestLayout);
			_frameBottom = UIFrameGrip.Create(Window, CornerResizeOnly ? UIFrameGrip.GripMode.Move : UIFrameGrip.GripMode.Height, RequestLayout);
			_frameCorner = UIFrameGrip.Create(Window, UIFrameGrip.GripMode.Both, RequestLayout);
			foreach (var frame in new[] { _frameTop, _frameLeft, _frameRight, _frameBottom, _frameCorner })
				frame.HighlightOnHover = HighlightFrame;

			_frameTop.MinPanelWidth = geometry.MinW;
			_frameLeft.MinPanelWidth = geometry.MinW;
			_frameRight.MinPanelWidth = geometry.MinW;
			_frameRight.MinPanelHeight = geometry.MinH;
			_frameBottom.MinPanelWidth = geometry.MinW;
			_frameBottom.MinPanelHeight = geometry.MinH;
			_frameCorner.MinPanelWidth = geometry.MinW;
			_frameCorner.MinPanelHeight = geometry.MinH;

			Window.Append(_frameTop);
			Window.Append(_frameLeft);
			Window.Append(_frameRight);
			Window.Append(_frameBottom);
			if (!CornerResizeOnly) Window.Append(_frameCorner);

			// 有标题栏的窗口照旧:标题栏也能拖
			if (!frameless)
			{
				_dragBar = UIDragBar.Create(Window, RequestLayout);
				_dragBar.Title = title;
				Window.Append(_dragBar);
			}

			// 关闭:叉是代码画的(不依赖字体里有没有 ×);悬停变红
			_closeButton = new UIIconButton(UIIconButton.IconKind.Cross) { Danger = true };
			_closeButton.OnLeftClick += (evt, element) => UISystem.Close();
			Window.Append(_closeButton);

			// 「重置本窗口布局」——以前拖到奇怪位置只能靠边界 clamp 自救
			_resetButton = new UIButton(ModText.Get("ResetLayoutShort", "重置"), 0.78f);
			_resetButton.OnLeftClick += (evt, element) =>
			{
				WindowLayout.Reset(Geo.Key, Geo);
				FitToScreen();
				ApplyLayout();
			};
			Window.Append(_resetButton);

			_grip = UIResizeGrip.Create(Window, RequestLayout);
			_grip.MinPanelWidth = geometry.MinW;
			_grip.MinPanelHeight = geometry.MinH;
			_grip.Width.Set(20f, 0f);
			_grip.Height.Set(20f, 0f);
			Window.Append(_grip);
		}

		/// <summary>
		/// 把 -1 的居中哨兵解成屏幕中心,并把窗口收进当前屏幕范围内。
		/// 每次窗口被打开(OnActivate)都会走一遍,所以换了分辨率或界面缩放之后
		/// 也不会跑到屏幕外面去。
		/// </summary>
		public void FitToScreen()
		{
			if (Window == null || Geo == null || Main.screenWidth <= 0 || Main.screenHeight <= 0)
			{
				return;
			}

			// 窗口永远不超过屏幕(留一点边距);但不能小于设计的最小尺寸。
			float maxW = Math.Max(Geo.MinW, ViewportSize.X - 24f);
			float maxH = Math.Max(Geo.MinH, ViewportSize.Y - 24f);
			Geo.W = Math.Min(Geo.W, maxW);
			Geo.H = Math.Min(Geo.H, maxH);

			if (Geo.X < 0f || Geo.Y < 0f)
			{
				// 居中
				Geo.X = Math.Max(0f, (ViewportSize.X - Geo.W) / 2f);
				Geo.Y = Math.Max(0f, (ViewportSize.Y - Geo.H) / 2f);
			}
			else
			{
				// 玩家拖过位置就保留,但别让窗口跑到屏幕外面
				Geo.X = Math.Clamp(Geo.X, 0f, Math.Max(0f, ViewportSize.X - Geo.W));
				Geo.Y = Math.Clamp(Geo.Y, 0f, Math.Max(0f, ViewportSize.Y - Geo.H));
			}

			Window.Left.Set(Geo.X, 0f);
			Window.Top.Set(Geo.Y, 0f);
			Window.Width.Set(Geo.W, 0f);
			Window.Height.Set(Geo.H, 0f);
			Window.Recalculate();
		}

		public override void OnActivate()
		{
			base.OnActivate();
			FitToScreen();
			ApplyLayout();
		}

		/// <summary>
		/// 窗口关掉时把几何写回存档 —— 这样拖过 / 拉过的布局下次进来还在。
		/// </summary>
		public override void OnDeactivate()
		{
			base.OnDeactivate();
			SyncGeometry();
			WindowLayout.Store(Geo.Key, Geo);
			WindowLayout.Save();
		}

		/// <summary>
		/// 画完所有子控件之后再画悬停提示,保证它压在最上面。
		/// (子控件自己画的话,会被后添加的控件盖住。)
		/// </summary>
		public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
		{
			base.Draw(spriteBatch);
			if (AEUiSession.InputAllowed) DrawHoverTooltip(spriteBatch);
		}

		/// <summary>子类覆写它来画「鼠标悬停时的物品名 / 存量」。</summary>
		protected virtual void DrawHoverTooltip(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
		{
		}

		/// <summary>把面板当前几何写回 Geo(拖动之后同步)。</summary>
		protected void SyncGeometry()
		{
			if (Window == null || Geo == null)
			{
				return;
			}

			Geo.W = Math.Max(Geo.MinW, Window.Width.Pixels);
			Geo.H = Math.Max(Geo.MinH, Window.Height.Pixels);
			Geo.X = Window.Left.Pixels;
			Geo.Y = Window.Top.Pixels;
		}

		/// <summary>子类覆写时先调用 base.ApplyLayout()。</summary>
		protected virtual void ApplyLayout()
		{
			if (Window == null || Geo == null)
			{
				return;
			}

			// 先把窗口的实际几何读回来 —— 缩放刚改过 Width/Height,
			// 如果这里直接用旧的 Geo,就会把刚拖出来的大小又改回去。
			SyncGeometry();

			Geo.W = Math.Max(Geo.MinW, Geo.W);
			Geo.H = Math.Max(Geo.MinH, Geo.H);

			if (Geo.X < 0f || Geo.Y < 0f)
			{
				Geo.X = Math.Max(0f, (ViewportSize.X - Geo.W) / 2f);
				Geo.Y = Math.Max(0f, (ViewportSize.Y - Geo.H) / 2f);
			}
			else
			{
				// 拖过头也别把窗口扔出屏幕:至少留 80px 宽 / 40px 高在里面,还能抓回来
				Geo.X = Math.Clamp(Geo.X, 80f - Geo.W, Math.Max(0f, ViewportSize.X - 80f));
				Geo.Y = Math.Clamp(Geo.Y, 0f, Math.Max(0f, ViewportSize.Y - 40f));
			}

			Window.Left.Set(Geo.X, 0f);
			Window.Top.Set(Geo.Y, 0f);
			Window.Width.Set(Geo.W, 0f);
			Window.Height.Set(Geo.H, 0f);

			if (_dragBar != null)
			{
				_dragBar.Left.Set(0f, 0f);
				_dragBar.Top.Set(0f, 0f);
				_dragBar.Width.Set(Geo.W, 0f);
				_dragBar.Height.Set(TitleHeight, 0f);
			}

			// 边框拖拽区(窗口最外一圈)
			float innerH = Math.Max(0f, Geo.H - FrameGrip * 2f);
			float innerW = Math.Max(0f, Geo.W - FrameGrip * 2f);

			_frameTop.Left.Set(0f, 0f);
			_frameTop.Top.Set(0f, 0f);
			_frameTop.Width.Set(Geo.W, 0f);
			_frameTop.Height.Set(FrameGrip, 0f);

			_frameLeft.Left.Set(0f, 0f);
			_frameLeft.Top.Set(FrameGrip, 0f);
			_frameLeft.Width.Set(FrameGrip, 0f);
			_frameLeft.Height.Set(innerH, 0f);

			_frameRight.Left.Set(Geo.W - FrameGrip, 0f);
			_frameRight.Top.Set(FrameGrip, 0f);
			_frameRight.Width.Set(FrameGrip, 0f);
			_frameRight.Height.Set(innerH, 0f);

			_frameBottom.Left.Set(FrameGrip, 0f);
			_frameBottom.Top.Set(Geo.H - FrameGrip, 0f);
			_frameBottom.Width.Set(innerW, 0f);
			_frameBottom.Height.Set(FrameGrip, 0f);

			_frameCorner.Left.Set(Geo.W - 22f, 0f);
			_frameCorner.Top.Set(Geo.H - 22f, 0f);
			_frameCorner.Width.Set(22f, 0f);
			_frameCorner.Height.Set(22f, 0f);

			// 「重置 / 关闭」:既要压在窗口内部,又要**离开那圈 12px 的边框**
			// (压到边框上看起来就像跑到框外面去了)
			float rightInset = Frameless ? Pad : 8f;
			const float closeWidth = 26f;
			float resetWidth = ResetButtonWidth;
			float chromeGap = ChromeGap;

			_closeButton.Left.Set(Geo.W - rightInset - closeWidth, 0f);
			_closeButton.Top.Set(_chromeTop, 0f);
			_closeButton.Width.Set(closeWidth, 0f);
			_closeButton.Height.Set(_chromeHeight, 0f);

			_resetButton.Left.Set(
				Geo.W - rightInset - closeWidth - chromeGap - resetWidth, 0f);
			_resetButton.Top.Set(_chromeTop, 0f);
			_resetButton.Width.Set(resetWidth, 0f);
			_resetButton.Height.Set(_chromeHeight, 0f);

			_grip.Left.Set(Geo.W - 22f, 0f);
			_grip.Top.Set(Geo.H - 22f, 0f);

			Window.Recalculate();
			SyncGeometry();
		}

		public sealed override void Update(GameTime gameTime)
		{
            _updatingChildren = true;
            try { base.Update(gameTime); }
            finally { _updatingChildren = false; }
            var changes = _afterChildren; _afterChildren = null; changes?.Invoke();
            if (_layoutPending) { _layoutPending = false; ApplyLayout(); }
            float w=ViewportSize.X,h=ViewportSize.Y;
            if (w!=_lastScreenWidth || h!=_lastScreenHeight) { _lastScreenWidth=w; _lastScreenHeight=h; FitToScreen(); ApplyLayout(); }
            SyncGeometry();
			UpdateWindow(gameTime);
		}

		protected virtual void UpdateWindow(GameTime gameTime)
		{
		}
	}
}
