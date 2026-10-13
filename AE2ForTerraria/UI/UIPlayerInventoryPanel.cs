using System;
using System.Collections.Generic;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>The real fifty player inventory slots, usable while the vanilla inventory is shielded.</summary>
public sealed class UIPlayerInventoryPanel : UIElement
{
    public const int SlotCount = 50;
    private const int Columns = 10;
    private readonly List<UIItemSlotElement> _slots = new();
    private readonly UIText _label = new("我的背包", .78f) { DynamicallyScaleDownToWidth = true };
    private Func<Item, int, bool> _shiftLeft, _shiftRight;

    public UIPlayerInventoryPanel()
    {
        Append(_label);
        for (int index = 0; index < SlotCount; index++)
        {
            int slotIndex = index;
            bool localChange = false;
            UIItemSlotElement slot = UIItemSlotElement.Create(
                () => Main.LocalPlayer.inventory[slotIndex],
                item => Main.LocalPlayer.inventory[slotIndex] = item,
                onChanged: () =>
                {
                    // Shift transfers already send an ordered server intent. Sending
                    // the old slot again after that intent would restore deposited items.
                    if (!localChange) return;
                    localChange = false;
                    SyncPlayerSlot(slotIndex);
                });
            slot.InteractionHandler = _ =>
            {
                localChange = CanInteract(Main.LocalPlayer);
                return !localChange;
            };
            slot.ShiftClickHandler = item =>
            {
                localChange = false;
                return !CanInteract(Main.LocalPlayer) || (_shiftLeft?.Invoke(item, slotIndex) ?? false);
            };
            slot.ShiftRightClickHandler = item =>
            {
                localChange = false;
                return !CanInteract(Main.LocalPlayer) || (_shiftRight?.Invoke(item, slotIndex) ?? false);
            };
            Append(slot);
            _slots.Add(slot);
        }
    }

    public void SetTransferHandlers(Func<Item, int, bool> shiftLeft, Func<Item, int, bool> shiftRight)
    { _shiftLeft = shiftLeft; _shiftRight = shiftRight; }

    public void SetBounds(float x, float y, float width, float height)
    {
        Place(this, x, y, width, height);
        Place(_label, 0, 0, width, 22);
        float step = Math.Max(8, Math.Min(50, Math.Min(width / Columns, (height - 26) / 5)));
        for (int index = 0; index < _slots.Count; index++)
            Place(_slots[index], (index % Columns) * step, 26 + (index / Columns) * step, step - 3, step - 3);
        Recalculate();
    }

    internal static bool CanInteract(Player player) => player != null && player.active
        && !MENet.CursorPending && !MENet.InventoryTransactionPending && !player.HasLockedInventory();

    private static void SyncPlayerSlot(int slot)
    {
        Player player = Main.LocalPlayer;
        player.inventory[58] = Main.mouseItem;
        if (Main.netMode != NetmodeID.MultiplayerClient) return;
        NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot);
        NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, 58);
    }

    private static void Place(UIElement element, float x, float y, float width, float height)
    {
        element.Left.Set(x, 0); element.Top.Set(y, 0);
        element.Width.Set(width, 0); element.Height.Set(height, 0);
    }
}
