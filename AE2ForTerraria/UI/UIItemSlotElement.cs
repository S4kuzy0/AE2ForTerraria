using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 可交互的物品槽。左键整体交换,右键取/放一个。
	/// Getter/Setter 直接指向真实容器(驱动器磁盘槽、玩家背包…)里的 Item 引用。
	/// </summary>
	public sealed class UIItemSlotElement : UIElement
	{
		public const int SlotPixels = 46;

		public Func<Item> Getter;
		public Action<Item> Setter;
		public Func<Item, bool> CanAccept;
		public Func<Item, bool> CanRemove;
		public Action OnChanged;
        public Func<bool, bool> InteractionHandler;

		/// <summary>
		/// Shift+左键的替代动作。返回 true 表示「我处理了」,不再走原来的交换逻辑。
		/// 合成终端用它实现「Shift+左键 = 这一叠直接存进网络」。
		/// </summary>
		public Func<Item, bool> ShiftClickHandler;

		/// <summary>
		/// Shift+右键的替代动作(终端用它做「把背包里这一种全部存进网络」)。
		/// 返回 true 表示已处理。
		/// </summary>
		public Func<Item, bool> ShiftRightClickHandler;

		public static UIItemSlotElement Create(
			Func<Item> getter,
			Action<Item> setter,
			Func<Item, bool> canAccept = null,
			Func<Item, bool> canRemove = null,
			Action onChanged = null)
		{
			var slot = new UIItemSlotElement
			{
				Getter = getter,
				Setter = setter,
				CanAccept = canAccept,
				CanRemove = canRemove,
				OnChanged = onChanged
			};

			slot.Width.Set(SlotPixels, 0f);
			slot.Height.Set(SlotPixels, 0f);
			slot.OnLeftClick += (evt, element) => slot.Interact(false);
			slot.OnRightClick += (evt, element) => slot.Interact(true);
			return slot;
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (ContainsPoint(Main.MouseScreen))
			{
				Main.LocalPlayer.mouseInterface = true;
			}
		}

		private void Interact(bool rightClick)
		{
			if (!Systems.AEUiSession.InputAllowed || Systems.MENet.CursorPending
                || Systems.MENet.InventoryTransactionPending || Main.LocalPlayer.HasLockedInventory()) return;
            Item slotItem = Getter?.Invoke();
			if (slotItem == null)
			{
				return;
			}

			// Shift + 左键:交给宿主处理(快捷搬运)
			if (!rightClick
				&& ShiftClickHandler != null
				&& (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift)
					|| Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift))
				&& ShiftClickHandler(slotItem))
			{
				OnChanged?.Invoke();
				return;
			}

			// Shift + 右键:把「这一种」整批处理(存全部同种)
			if (rightClick
				&& ShiftRightClickHandler != null
				&& (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift)
					|| Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift))
				&& ShiftRightClickHandler(slotItem))
			{
				OnChanged?.Invoke();
				return;
			}

			if (InteractionHandler?.Invoke(rightClick) == true) return;

            Item mouse = Main.mouseItem;

			if (rightClick)
			{
				// 右键:空手取一个;手上拿着则放一个
				if (mouse.type <= 0)
				{
					if (slotItem.type > 0 && CanRemove != null && !CanRemove(slotItem))
					{
						return;
					}

					if (slotItem.type > 0 && slotItem.stack > 0)
					{
						Main.mouseItem = slotItem.Clone();
						Main.mouseItem.stack = 1;
						slotItem.stack--;
						if (slotItem.stack <= 0)
						{
							slotItem.TurnToAir();
						}
					}
				}
				else if (CanAccept == null || CanAccept(mouse))
				{
					if (slotItem.type <= 0)
					{
						Item placed = mouse.Clone();
						placed.stack = 1;
						Setter(placed);
						mouse.stack--;
						if (mouse.stack <= 0)
						{
							Main.mouseItem.TurnToAir();
						}
					}
					else if (Core.ItemKey.FromItem(slotItem).Equals(Core.ItemKey.FromItem(mouse)) && slotItem.stack < slotItem.maxStack)
					{
						slotItem.stack++;
						mouse.stack--;
						if (mouse.stack <= 0)
						{
							Main.mouseItem.TurnToAir();
						}
					}
					else
					{
						return;
					}
				}
				else
				{
					return;
				}
			}
			else
			{
				// 左键:整体交换
				if (mouse.type > 0 && CanAccept != null && !CanAccept(mouse))
				{
					return;
				}

				if (slotItem.type > 0 && CanRemove != null && !CanRemove(slotItem))
				{
					return;
				}

				Item previous = Getter();
				Setter(Main.mouseItem);
				Main.mouseItem = previous;
			}

			OnChanged?.Invoke();
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			var box = new Rectangle((int)dims.X + 1, (int)dims.Y + 1, (int)dims.Width - 3, (int)dims.Height - 3);
			bool hovered = ContainsPoint(Main.MouseScreen);
			Item item = Getter?.Invoke();

			UITheme.FillRounded(spriteBatch, box, hovered ? UITheme.BtnHot : UITheme.Slot, 3);
			UITheme.Outline(spriteBatch, box, UITheme.ItemBorder, 1);

			if (item == null || item.type <= 0)
			{
				return;
			}

            if (hovered && Systems.AEUiSession.InputAllowed)
            {
                Main.HoverItem = item.Clone();
                Main.hoverItemName = item.Name;
            }

			Main.instance.LoadItem(item.type);
			Texture2D texture = TextureAssets.Item[item.type].Value;
			if (texture != null)
			{
				Rectangle frame = Main.itemAnimations[item.type] != null
					? Main.itemAnimations[item.type].GetFrame(texture, 0)
					: texture.Frame(1, 1, 0, 0);

				float scale = 1f;
				int longest = Math.Max(frame.Width, frame.Height);
				float iconSize=Math.Min(32f,Math.Min(box.Width,box.Height)-8f);
                if (longest > iconSize)
				{
					scale = iconSize / longest;
				}

				var center = new Vector2(box.X + box.Width / 2f, box.Y + box.Height / 2f);
				spriteBatch.Draw(texture, center, frame, Color.White, 0f,
					new Vector2(frame.Width / 2f, frame.Height / 2f), scale, SpriteEffects.None, 0f);
			}

			if (item.stack > 1)
			{
				string text = UIFormat.Count(item.stack);
				Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * 0.75f;
				var backing = new Rectangle(
					(int)(box.Right - 3f - size.X - 3f), (int)(box.Bottom - 3f - size.Y) - 2,
					(int)size.X + 5, (int)size.Y + 3);

				UITheme.Fill(spriteBatch, backing, new Color(8, 10, 18) * 0.75f);
				Utils.DrawBorderString(spriteBatch, text,
					new Vector2(box.Right - 5f - size.X, box.Bottom - 4f - size.Y),
					UITheme.Text, 0.75f, 0f, 0f);
			}
		}
	}
}
