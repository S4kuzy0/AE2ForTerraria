using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    private const byte ServerInventoryPage = (MessageTag << ProtocolShift) | 49;
    private const int PageEntryLimit = 128, PageByteTarget = 4 * 1024 * 1024;
    private static long _inventorySequence;
    private sealed class InventoryAssembly
    {
        public long Id, MaxTypes, MaxBytes, MaxAmountPerType;
        public int Total, Received, Epoch, Level;
        public Point16 Cpu, Assembler;
        public ulong Updated;
        public readonly Dictionary<ItemKey, long> Entries = new();
    }
    private static readonly Dictionary<(Point16, bool), InventoryAssembly> InventoryPages = new();
    internal static void ClearClientInventoryPages() => InventoryPages.Clear();
    private static void ClearInventoryPages() => InventoryPages.Clear();

    private static void SendInventoryPages(Point16 position, IReadOnlyDictionary<ItemKey, long> entries,
        bool aggregate, long maxTypes, long maxBytes, long maxAmountPerType, int who, int ignore, int epoch = 0, int level = 0)
    {
        long id = ++_inventorySequence;
        var cpu = aggregate ? MENetwork.FindCraftingCpu(position.X, position.Y) : null;
        var assembler = aggregate ? MENetwork.FindAssembler(position.X, position.Y) : null;
        int offset = 0, size = 0;
        var page = new List<KeyValuePair<ItemKey, long>>(PageEntryLimit);
        void SendPage()
        {
            using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
            writer.Write(ServerInventoryPage); writer.Write(position.X); writer.Write(position.Y);
            writer.Write(id); writer.Write(aggregate); writer.Write(offset); writer.Write(entries.Count);
            writer.Write(epoch); writer.Write(level); writer.Write(maxTypes); writer.Write(maxBytes);
            writer.Write(maxAmountPerType);
            writer.Write(cpu?.Position.X ?? (short)-1); writer.Write(cpu?.Position.Y ?? (short)-1);
            writer.Write(assembler?.Position.X ?? (short)-1); writer.Write(assembler?.Position.Y ?? (short)-1);
            writer.Write(page.Count);
            foreach (var entry in page) { WriteKey(writer, entry.Key); writer.Write(entry.Value); }
            SendAuthorizedPayload(buffer.ToArray(), position, who, ignore);
            offset += page.Count; page.Clear(); size = 0;
        }
        foreach (var entry in entries)
        {
            int entrySize = Encoding.UTF8.GetByteCount(entry.Key.Data ?? "") + 32;
            if (page.Count > 0 && (page.Count >= PageEntryLimit || (long)size + entrySize > PageByteTarget)) SendPage();
            page.Add(entry); size += entrySize;
        }
        if (page.Count > 0 || entries.Count == 0) SendPage();
    }

    private static void ReceiveInventoryPage(Point16 position, BinaryReader reader)
    {
        long id = reader.ReadInt64(); bool aggregate = reader.ReadBoolean();
        int offset = reader.ReadInt32(), total = reader.ReadInt32(), epoch = reader.ReadInt32(), level = reader.ReadInt32();
        long maxTypes = reader.ReadInt64(), maxBytes = reader.ReadInt64(), maxAmountPerType = reader.ReadInt64();
        var cpu = new Point16(reader.ReadInt16(), reader.ReadInt16());
        var assembler = new Point16(reader.ReadInt16(), reader.ReadInt16());
        int count = ReadCount(reader);
        if (id <= 0 || offset < 0 || total < 0 || count > PageEntryLimit || (long)offset + count > total
            || (count == 0 && total != 0) || maxTypes < 0 || maxBytes < 0 || maxAmountPerType < 0)
            throw new InvalidDataException("Invalid inventory page bounds");
        var key = (position, aggregate);
        if (aggregate && epoch > 0 && !UISystem.AcceptTerminalInventorySnapshot(position, epoch)) return;
        if (offset == 0)
        {
            if (InventoryPages.TryGetValue(key, out var previous) && previous.Id > id) return;
            if (InventoryPages.Count >= 64) InventoryPages.Clear();
            InventoryPages[key] = new InventoryAssembly { Id = id, Total = total, Epoch = epoch, Level = level,
                MaxTypes = maxTypes, MaxBytes = maxBytes, MaxAmountPerType = maxAmountPerType, Cpu = cpu, Assembler = assembler };
        }
        if (!InventoryPages.TryGetValue(key, out var pending) || pending.Id != id) return;
        if (pending.Received != offset || pending.Total != total || pending.Epoch != epoch || pending.Level != level
            || pending.MaxTypes != maxTypes || pending.MaxBytes != maxBytes || pending.MaxAmountPerType != maxAmountPerType)
        { InventoryPages.Remove(key); throw new InvalidDataException("Inventory snapshot changed during transfer"); }
        var decoded = new List<KeyValuePair<ItemKey, long>>(count);
        for (int i = 0; i < count; i++)
        {
            var item = ReadKey(reader); long amount = reader.ReadInt64();
            if (item.IsAir || amount <= 0 || pending.Entries.ContainsKey(item))
            { InventoryPages.Remove(key); throw new InvalidDataException("Invalid inventory entry"); }
            decoded.Add(new(item, amount));
        }
        foreach (var entry in decoded)
            if (!pending.Entries.TryAdd(entry.Key, entry.Value))
            { InventoryPages.Remove(key); throw new InvalidDataException("Duplicate inventory identity"); }
        pending.Received += count; pending.Updated = Main.GameUpdateCount;
        if (pending.Received != total) return;
        InventoryPages.Remove(key);
        // Publication is atomic: partial pages never clear or partially replace a visible inventory.
        if (aggregate)
        {
            if (epoch > 0) UISystem.ApplyTerminalAccess(position, epoch, level, pending.Entries, cpu, assembler);
            else if (!UISystem.HasTerminalReadSession) UISystem.SetRemoteContents(position, pending.Entries);
        }
        else if (TileEntity.ByPosition.TryGetValue(position, out var entity) && entity is IMEStorage storage)
        {
            var decodedLedger = new StorageLedger(storage.Ledger.BytesPerType, storage.Ledger.AmountPerByte, maxTypes)
                { MaxBytes = maxBytes, MaxTypes = maxTypes, MaxAmountPerType = maxAmountPerType };
            foreach (var entry in pending.Entries)
                decodedLedger.Restore(entry.Key, entry.Value);
            storage.Ledger.ReplaceContents(decodedLedger); storage.Ledger.MaxTypes = maxTypes; storage.Ledger.MaxBytes = maxBytes;
            storage.Ledger.MaxAmountPerType = maxAmountPerType;
            UISystem.RefreshNetwork();
        }
    }
    internal static void ExpirePartialSnapshots()
    {
        foreach (var key in new List<(Point16, bool)>(InventoryPages.Keys))
            if (Main.GameUpdateCount - InventoryPages[key].Updated > 600) InventoryPages.Remove(key);
        foreach (int id in new List<int>(PendingSnapshots.Keys))
            if (Main.GameUpdateCount - PendingSnapshots[id].Started > 600)
            { PendingSnapshots[id].Data.Dispose(); PendingSnapshots.Remove(id); }
    }
}
