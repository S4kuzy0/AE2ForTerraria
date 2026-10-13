using System;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.Systems;

/// <summary>Vanilla quick-stack locks do not cover sorting, restock or the throw key.</summary>
public sealed class InventoryTransactionInputSystem : ModSystem
{
    private delegate bool IgnoreMouseInterfaceHook(Func<bool> original);
    private delegate string GamepadInstructionsHook(Func<Item[], int, int, string> original,
        Item[] inventory, int context, int slot);

    public override void Load()
    {
        if (Main.dedServ) return;
        var getter = typeof(PlayerInput).GetProperty(nameof(PlayerInput.IgnoreMouseInterface))?.GetMethod
            ?? throw new MissingMethodException("PlayerInput.IgnoreMouseInterface");
        MonoModHooks.Add(getter, new IgnoreMouseInterfaceHook(
            original => MENet.InventoryTransactionPending || MENet.CursorPending || AEUiSession.BlocksExternalInput || original()));
        // Gamepad inventory shortcuts can call LeftClick/DropSelectedItem directly,
        // bypassing both DrawInventory's slot lock and the mouse-interface gate.
        // Hook the complete action dispatcher: CanExecuteCommand is a trivial getter
        // that the JIT can inline, so detouring that getter does not protect callers.
        var gamepadDispatcher = typeof(ItemSlot).GetMethod(nameof(ItemSlot.GetGamepadInstructions),
            new[] { typeof(Item[]), typeof(int), typeof(int) })
            ?? throw new MissingMethodException("ItemSlot.GetGamepadInstructions(Item[],int,int)");
        MonoModHooks.Add(gamepadDispatcher, new GamepadInstructionsHook((original, inventory, context, slot)
            => MENet.InventoryTransactionPending || MENet.CursorPending || AEUiSession.BlocksExternalInput ? string.Empty : original(inventory, context, slot)));
    }
}

public sealed class InventoryTransactionItemGuard : GlobalItem
{
    public override bool CanStack(Item destination, Item source)
        => !MENet.IsInventoryTransactionItem(destination) && !MENet.IsInventoryTransactionItem(source);

    // A depleted pending slot becomes empty as soon as its native reply arrives.
    // GetItem may fill an empty slot without CanStack, before the completion packet.
    public override bool CanPickup(Item item, Player player)
        => !(MENet.InventoryTransactionPending && player.whoAmI == Main.myPlayer);
}
