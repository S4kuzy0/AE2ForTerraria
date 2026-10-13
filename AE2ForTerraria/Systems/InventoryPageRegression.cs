using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

internal static class InventoryPageRegression
{
    public static void Run(Action<bool,string> check)
    {
        using var world = new RegressionWorld();
        var drive = world.Place(new MEDiskDriveEntity(), ModContent.TileType<MEDiskDriveTile>(), 20, 20);
        var wood = ItemKey.Crafted(ItemID.Wood);
        drive.Ledger.Restore(wood, 7);
        Main.netMode = NetmodeID.MultiplayerClient;
        var pageMessage = (byte)typeof(MENet).GetField("ServerInventoryPage", BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
        byte[] Page(long id, int offset, int total, int count, bool longData = false, ushort version = MENet.ProtocolVersion)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write((byte)0); writer.Write(version); writer.Write(pageMessage); writer.Write(drive.Position.X); writer.Write(drive.Position.Y);
            writer.Write(id); writer.Write(false); writer.Write(offset); writer.Write(total); writer.Write(0); writer.Write(0);
            writer.Write(long.MaxValue); writer.Write(long.MaxValue);
            writer.Write((long)int.MaxValue);
            for (int i = 0; i < 4; i++) writer.Write((short)-1);
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                // ItemID constants are Int16; the protocol stores ItemKey.Type as Int32.
                writer.Write((int)ItemID.Wood); writer.Write(0);
                // Opaque identities exercise the wire format; no synthetic item is created or saved.
                writer.Write((longData ? new string('x', 280) : "wire-") + (offset+i));
                writer.Write(1L);
            }
            return stream.ToArray();
        }
        void Dispatch(byte[] payload) { using var reader = new BinaryReader(new MemoryStream(payload)); MENet.Handle(reader, 256); }
        try
        {
            MENet.ClearClientInventoryPages();
            Dispatch(Page(1,0,2,1,version:3));
            check(drive.Ledger.GetAmount(wood)==7,"old wire version is rejected without mutating inventory");
            Dispatch(Page(2,0,2,1));
            var pendingPages = (IDictionary)typeof(MENet).GetField("InventoryPages", BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            var staged = pendingPages[(drive.Position, false)];
            check(staged != null && (int)staged.GetType().GetField("Received").GetValue(staged) == 1
                && drive.Ledger.GetAmount(wood)==7,"first page is staged while the previous ledger remains visible");
            Dispatch(Page(2,1,2,1)[..^1]);
            check(drive.Ledger.GetAmount(wood)==7,"truncated final page cannot publish a partial ledger");
            Dispatch(Page(2,1,2,1));
            check(drive.Ledger.StoredTotalAmount==2 && drive.Ledger.MaxTypes==long.MaxValue
                && drive.Ledger.MaxAmountPerType==int.MaxValue,
                "complete inventory pages atomically publish identity and per-identity capacity metadata");
            Dispatch(Page(3,0,4,1)); Dispatch(Page(3,2,4,1));
            check(drive.Ledger.StoredTotalAmount==2,"out-of-order inventory page preserves previous complete ledger");
            const int total = 65537;
            long transferred = 0;
            for (int offset=0; offset<total; offset+=128)
            {
                var bytes=Page(4,offset,total,Math.Min(128,total-offset),true); transferred+=bytes.Length; Dispatch(bytes);
                if (offset==0) check(drive.Ledger.StoredTotalAmount==2,"large snapshot remains invisible until final page");
            }
            check(transferred>16*1024*1024 && drive.Ledger.Entries.Count==total && drive.Ledger.StoredTotalAmount==total,
                "snapshot larger than 16 MiB and 65536 identities completes across bounded pages");
            var state=new TerminalReadState(); state.Begin(new(20,20)); int epoch=state.Epoch;
            state.Reset(); state.Begin(new(20,20));
            check(state.Epoch!=epoch && !state.Apply(new(20,20),epoch,NetworkRegistry.LevelManage),
                "reopening the same terminal rejects snapshots from its previous read session");
            check(state.Apply(new(20,20),state.Epoch,0) && state.Denied && !state.Readable,
                "permission denial clears readable state and cannot be overwritten by a stale snapshot");
        }
        finally { MENet.ClearClientInventoryPages(); }
    }
}
