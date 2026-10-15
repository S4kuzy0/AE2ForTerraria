using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 窗口底板。**在底板的空位上按住左键就能拖动整个窗口** ——
	/// 和 Recipe Browser 一样,不用非去抓边框那一条。
	///
	/// 只有「点到的东西就是底板本身」才开始拖(点到按钮 / 网格 / 搜索框不会),
	/// 另外可以把状态文字这类"看着像空位"的子元素加进 DragTargets,让它们也能拖。
	/// </summary>
	public sealed class UIWindowPanel : UIPanel
	{
		/// <summary>要拖的窗口(一般就是底板自己)。</summary>
		public UIElement Target;

		/// <summary>拖完 / 松开时回调(用来把布局同步回存档)。</summary>
		public Action OnMoved;

		/// <summary>额外算作空位的子元素(状态文字之类)。</summary>
		public readonly List<UIElement> DragTargets = new List<UIElement>();
		public Func<UIElement, Vector2, bool> AdditionalDragTarget;
		public bool Dragged { get; private set; }

		private bool _dragging;
		private Vector2 _offset;
		private Vector2 _dragStart;

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			var bounds = GetDimensions().ToRectangle();
			UITheme.Fill(spriteBatch, bounds, BackgroundColor);
			UITheme.Outline(spriteBatch, bounds, BorderColor);
		}

		public override void LeftMouseDown(UIMouseEvent evt)
		{
			Dragged = false;
			if (evt.Target == this || (evt.Target != null && DragTargets.Contains(evt.Target))
				|| AdditionalDragTarget?.Invoke(evt.Target, evt.MousePosition) == true)
			{
				_dragging = true;
				_dragStart = evt.MousePosition;
				_offset = new Vector2(
					evt.MousePosition.X - Left.Pixels,
					evt.MousePosition.Y - Top.Pixels);
			}

			base.LeftMouseDown(evt);
		}

		public override void LeftMouseUp(UIMouseEvent evt)
		{
			base.LeftMouseUp(evt);
			bool moved = _dragging;
			_dragging = false;
			if (moved) OnMoved?.Invoke();
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (!_dragging)
			{
				return;
			}

			if (!Main.mouseLeft)
			{
				_dragging = false;
				OnMoved?.Invoke();
				return;
			}

			Dragged |= Vector2.DistanceSquared(_dragStart, Main.MouseScreen) > 9f;
			Target.Left.Set(Main.MouseScreen.X - _offset.X, 0f);
			Target.Top.Set(Main.MouseScreen.Y - _offset.Y, 0f);
			Target.Recalculate();

			// 拖窗口的时候别顺手拿手上的东西
			if (Main.LocalPlayer != null)
			{
				Main.LocalPlayer.mouseInterface = true;
			}
		}
	}

	/// <summary>标题栏:按住拖动整个窗口。</summary>
	public sealed class UIDragBar : UIElement
	{
		public UIElement Target;
		public string Title = string.Empty;
		private bool _dragging;
		private Vector2 _grab;

		public static UIDragBar Create(UIElement target, Action onMoved = null)
		{
			var bar = new UIDragBar { Target = target };

			bar.OnLeftMouseDown += (evt, element) =>
			{
				bar._dragging = true;
				bar._grab = new Vector2(
					Main.MouseScreen.X - target.Left.Pixels,
					Main.MouseScreen.Y - target.Top.Pixels);
			};

			bar.OnLeftMouseUp += (evt, element) =>
			{
				bar._dragging = false;
				onMoved?.Invoke();
			};

			return bar;
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();

			var rect = dims.ToRectangle();
			UITheme.Fill(spriteBatch, rect, UITheme.TitleBar);

			// 底部一条分隔线,把标题栏和内容区分开
			UITheme.Fill(spriteBatch,
				new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), UITheme.Divider);

			// 左侧窗口图标:2×2 的小方块(AE2 的方块意象,不用贴图)
			int iconX = rect.X + 11;
			int iconY = rect.Y + rect.Height / 2 - 5;
			UITheme.Fill(spriteBatch, new Rectangle(iconX, iconY, 4, 4), UITheme.Accent);
			UITheme.Fill(spriteBatch, new Rectangle(iconX + 6, iconY, 4, 4), UITheme.Accent * 0.7f);
			UITheme.Fill(spriteBatch, new Rectangle(iconX, iconY + 6, 4, 4), UITheme.Accent * 0.7f);
			UITheme.Fill(spriteBatch, new Rectangle(iconX + 6, iconY + 6, 4, 4), UITheme.Accent);

			// 右上角三个小圆点,暗示「这里能拖」
			int dotX = rect.Right - 92;
			for (int i = 0; i < 3; i++)
			{
				UITheme.Fill(spriteBatch,
					new Rectangle(dotX - i * 7, rect.Y + rect.Height / 2 - 1, 3, 3),
					UITheme.Muted * 0.8f);
			}

			if (!string.IsNullOrEmpty(Title))
			{
				Utils.DrawBorderString(spriteBatch, Title,
					new Vector2(dims.X + 28f, dims.Y + (dims.Height - 18f) / 2f),
					UITheme.Text, 0.95f);
			}
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (!_dragging)
			{
				return;
			}

			if (!Main.mouseLeft)
			{
				_dragging = false;
				return;
			}

			Target.Left.Set(Main.MouseScreen.X - _grab.X, 0f);
			Target.Top.Set(Main.MouseScreen.Y - _grab.Y, 0f);
			Target.Recalculate();

			// 拖窗口的时候别顺手拿手上的东西
			if (Main.LocalPlayer != null)
			{
				Main.LocalPlayer.mouseInterface = true;
			}
		}
	}

	/// <summary>
	/// 窗口边框的拖拽区。**拖边框就能移动 / 缩放窗口** ——
	/// 上边和左边 = 移动窗口,右边 = 改宽度,下边 = 改高度,右下角 = 两个一起改。
	///
	/// 浏览器那种「没有标题栏」的窗口就靠它移动;有标题栏的窗口多一条路,不影响。
	/// </summary>
	public sealed class UIFrameGrip : UIElement
	{
		public enum GripMode
		{
			Move,
			Width,
			Height,
			Both
		}

		public UIElement Target;
		public Action OnResized;
		public GripMode Mode = GripMode.Move;
		public bool HighlightOnHover = true;
		public float MinPanelWidth = 480f;
		public float MinPanelHeight = 320f;

		private bool _active;
		private Vector2 _grab;

		public static UIFrameGrip Create(UIElement target, GripMode mode, Action onResized = null)
		{
			var grip = new UIFrameGrip { Target = target, Mode = mode, OnResized = onResized };

			grip.OnLeftMouseDown += (evt, element) =>
			{
				grip._active = true;
				grip._grab = new Vector2(
					Main.MouseScreen.X - target.Left.Pixels,
					Main.MouseScreen.Y - target.Top.Pixels);
			};

			grip.OnLeftMouseUp += (evt, element) => grip._active = false;

			return grip;
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (!_active)
			{
				return;
			}

			if (!Main.mouseLeft)
			{
				_active = false;
				OnResized?.Invoke();
				return;
			}

			if (Mode == GripMode.Move)
			{
				Target.Left.Set(Main.MouseScreen.X - _grab.X, 0f);
				Target.Top.Set(Main.MouseScreen.Y - _grab.Y, 0f);
			}
			else
			{
				if (Mode == GripMode.Width || Mode == GripMode.Both)
				{
					Target.Width.Set(Math.Max(MinPanelWidth,
						Main.MouseScreen.X - Target.Left.Pixels), 0f);
				}

				if (Mode == GripMode.Height || Mode == GripMode.Both)
				{
					Target.Height.Set(Math.Max(MinPanelHeight,
						Main.MouseScreen.Y - Target.Top.Pixels), 0f);
				}
			}

			Target.Recalculate();

			// 拖动期间:不像是在点世界里的东西
			if (Main.LocalPlayer != null)
			{
				Main.LocalPlayer.mouseInterface = true;
			}

			OnResized?.Invoke();
		}

		/// <summary>悬停时把这条边框点亮一下 —— 不然玩家不知道边框能拖。</summary>
		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			if (!HighlightOnHover || (!_active && !ContainsPoint(Main.MouseScreen)))
			{
				return;
			}

			CalculatedStyle dims = GetDimensions();

			UITheme.Fill(spriteBatch, dims.ToRectangle(),
				_active ? UITheme.Accent * 0.45f : UITheme.Accent * 0.20f);
		}
	}

	/// <summary>右下角缩放手柄:拖动改变窗口大小。</summary>
	public sealed class UIResizeGrip : UIElement
	{
		public UIElement Target;
		public Action OnResized;
		public float MinPanelWidth = 480f;
		public float MinPanelHeight = 320f;

		private bool _resizing;

		public static UIResizeGrip Create(UIElement target, Action onResized)
		{
			var grip = new UIResizeGrip { Target = target, OnResized = onResized };
			grip.OnLeftMouseDown += (evt, element) => grip._resizing = true;
			grip.OnLeftMouseUp += (evt, element) => grip._resizing = false;
			return grip;
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (!_resizing)
			{
				return;
			}

			if (!Main.mouseLeft)
			{
				_resizing = false;
				OnResized?.Invoke();
				return;
			}

			float width = Math.Max(MinPanelWidth, Main.MouseScreen.X - Target.Left.Pixels);
			float height = Math.Max(MinPanelHeight, Main.MouseScreen.Y - Target.Top.Pixels);
			Target.Width.Set(width, 0f);
			Target.Height.Set(height, 0f);
			Target.Recalculate();
			OnResized?.Invoke();
		}

		/// <summary>画三条斜线当作缩放手柄 —— 以前这个区域是完全空白的。</summary>
		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();

			bool hovered = ContainsPoint(Main.MouseScreen);
			Color color = _resizing
				? UITheme.Text
				: hovered ? UITheme.BorderHot : UITheme.Muted * 0.75f;

			int right = (int)(dims.X + dims.Width) - 3;
			int bottom = (int)(dims.Y + dims.Height) - 3;

			for (int i = 0; i < 3; i++)
			{
				int length = 5 + i * 4;

				for (int step = 0; step < length; step++)
				{
					UITheme.Fill(spriteBatch,
						new Rectangle(right - step, bottom - (length - step), 2, 2), color);
				}
			}
		}
	}
}
