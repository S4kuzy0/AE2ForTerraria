using System;
using System.Linq;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.Systems;

/// <summary>Menu-only interactions against a temporary player's real inventory, without file or socket I/O.</summary>
internal static class EmbeddedInventoryRegression
{
    public static void Run(Action<bool, string> check)
    {
        if (!Main.gameMenu || Main.dedServ || MENet.CursorPending || MENet.InventoryTransactionPending)
            throw new InvalidOperationException("Embedded inventory checks require an idle client menu.");
        int index = Main.myPlayer, mode = Main.netMode;
        Player previousPlayer = Main.player[index];
        Item previousMouse = Main.mouseItem;
        KeyboardState previousKeys = Main.keyState;
        try
        {
            var player = new Player { whoAmI = index, active = true };
            Main.player[index] = player;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.mouseItem = new Item();
            Main.keyState = default;
            player.inventory[0] = new Item(ItemID.Wood, 10);
            player.inventory[49] = new Item(ModContent.ItemType<MEDisk1K>());
            var panel = new UIPlayerInventoryPanel();
            panel.SetBounds(20, 20, 500, 140);
            var slots = panel.Children.OfType<UIItemSlotElement>().ToArray();
            check(slots.Length == 50 && slots.Select((slot, i) => ReferenceEquals(slot.Getter(), player.inventory[i])).All(ok => ok),
                "embedded inventory binds all fifty real player slots, including the final row");
            var bounds = panel.GetDimensions().ToRectangle();
            check(slots.All(slot => bounds.Contains(slot.GetDimensions().ToRectangle())),
                "embedded inventory stays within a compact height without clipping the final row");

            void Click(int slot, bool right = false)
            {
                var target = slots[slot];
                var evt = new UIMouseEvent(target, target.GetDimensions().Center());
                if (right) target.RightClick(evt); else target.LeftClick(evt);
            }
            Click(49);
            check(player.inventory[49].IsAir && Main.mouseItem.type == ModContent.ItemType<MEDisk1K>()
                && ReferenceEquals(player.inventory[58], Main.mouseItem),
                "embedded disk pickup updates the real final bag slot and cursor slot together");
            Click(0);
            check(player.inventory[0].type == ModContent.ItemType<MEDisk1K>() && Main.mouseItem.type == ItemID.Wood
                && Main.mouseItem.stack == 10, "embedded left click swaps disk and material without losing either");
            Click(1, true);
            check(player.inventory[1].type == ItemID.Wood && player.inventory[1].stack == 1 && Main.mouseItem.stack == 9,
                "embedded right click puts exactly one item into an empty slot");
            Click(49);
            Click(49, true);
            check(Main.mouseItem.type == ItemID.Wood && Main.mouseItem.stack == 1 && player.inventory[49].stack == 8,
                "embedded right click picks exactly one item from a stack");

            MENet.CursorPending = true;
            Click(0);
            check(player.inventory[0].type == ModContent.ItemType<MEDisk1K>() && Main.mouseItem.type == ItemID.Wood,
                "pending authoritative machine reply blocks embedded inventory swaps");
            MENet.CursorPending = false;
            player.inventoryChestStack[49] = true;
            check(!UIPlayerInventoryPanel.CanInteract(player), "native inventory lock also blocks embedded inventory");
            player.inventoryChestStack[49] = false;

            Main.mouseItem = new Item();
            Main.netMode = NetmodeID.MultiplayerClient;
            var request = MENet.BeginInventoryDeposit(player, false, onlySlot: 49);
            check(request != null && !UIPlayerInventoryPanel.CanInteract(player),
                "pending server deposit blocks bag interaction before an acknowledgement");
            Click(49);
            check(player.inventory[49].stack == 8 && Main.mouseItem.IsAir,
                "clicking a pending deposit does not withdraw the same items locally");
            MENet.ResetInventoryDeposits();
            Main.netMode = NetmodeID.SinglePlayer;

            Main.mouseItem = new Item(ItemID.StoneBlock);
            Click(49, true); // Rejected incompatible placement must not leak into the next shift action.
            Main.mouseItem = new Item();
            int transferred = -1;
            panel.SetTransferHandlers((item, slot) => { transferred = slot; return true; }, null);
            var oldCursorSlot = player.inventory[58];
            Main.keyState = new KeyboardState(Keys.LeftShift);
            Click(49);
            check(transferred == 49 && ReferenceEquals(oldCursorSlot, player.inventory[58]),
                "shift transfer after a rejected placement uses the real slot without resending stale post-intent items");
        }
        finally
        {
            MENet.CursorPending = false;
            MENet.ResetInventoryDeposits();
            Main.player[index] = previousPlayer;
            Main.mouseItem = previousMouse;
            Main.keyState = previousKeys;
            Main.netMode = mode;
        }
    }
}
