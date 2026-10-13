using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;

namespace AE2ForTerraria.Systems;

/// <summary>Prepare a slot and cursor together, validate the resulting capacity, then commit once.</summary>
internal static class ContainerSlotOperations
{
    public static bool TryChange(Item[] slots, int index, ref Item cursor, bool right,
        MEDiskDriveEntity drive, out string reason)
    {
        reason = null;
        if (slots == null || index < 0 || index >= slots.Length || cursor == null) return false;
        Item old = slots[index] ?? new Item();
        if (!cursor.IsAir && !(drive != null ? MEDiskDriveEntity.IsDisk(cursor) : cursor.createTile >= 0)) return false;
        Item next = old.Clone(), held = cursor.Clone();
        if (!right) { next = cursor.Clone(); held = old.Clone(); }
        else if (cursor.IsAir && !old.IsAir) { held = old.Clone(); held.stack = 1; if (--next.stack <= 0) next.TurnToAir(); }
        else if (!cursor.IsAir && old.IsAir) { next = cursor.Clone(); next.stack = 1; if (--held.stack <= 0) held.TurnToAir(); }
        else if (!cursor.IsAir && ItemKey.FromItem(cursor).Equals(ItemKey.FromItem(old)) && old.stack < old.maxStack)
        { next.stack++; if (--held.stack <= 0) held.TurnToAir(); }
        else return false;
        if (drive != null && !drive.CanReplaceDisk(index, next, out reason)) return false;
        slots[index] = next;
        cursor = held;
        drive?.RefreshCapacity();
        return true;
    }
}
