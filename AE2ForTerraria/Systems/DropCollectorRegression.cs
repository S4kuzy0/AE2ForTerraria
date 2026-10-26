using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

internal static class DropCollectorRegression
{
    internal static void Run(Action<bool,string> check)
    {
        using var world = new RegressionWorld();
        var collector = world.Place(new MEDropCollectorEntity(), ModContent.TileType<MEDropCollectorTile>(), 40, 40);
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 41, 40);
        var oldItems = Main.item; int oldMode = Main.netMode;
        var drops = Enumerable.Range(0, oldItems.Length).Select(_ => new Item()).ToArray();
        try
        {
            Main.item = drops; Main.netMode = NetmodeID.SinglePlayer;
            var wood = ItemKey.Crafted(ItemID.Wood); var stone = ItemKey.Crafted(ItemID.StoneBlock);
            void Drop(int slot, int type, int stack, float distance, int delay=0)
            { drops[slot]=new Item(type,stack) {active=true,position=new Vector2(40*16+8+distance,40*16+8),noGrabDelay=delay}; }
            void Scan() { for(int i=0;i<15;i++) collector.Update(); }
            collector.Radius=256; Drop(0,ItemID.Wood,7,256*16+128); Scan();
            check(drops[0].active && storage.Ledger.GetAmount(wood)==0,
                "radius 256 remains finite and leaves drops beyond its actual distance untouched");
            collector.Radius=257; Scan();
            check(!drops[0].active && storage.Ledger.GetAmount(wood)==7,
                "radius above 256 collects actual far-away world drops into its connected storage");
            collector.Filter=new(){ItemID.StoneBlock};collector.IsBlacklist=true;collector.MarkFilterDirty();
            Drop(1,ItemID.StoneBlock,9,100000);Drop(2,ItemID.Wood,3,100000,60); Scan();
            check(drops[1].active && drops[1].stack==9 && drops[2].active && storage.Ledger.GetAmount(stone)==0,
                "world-range collector retains blacklist and native pickup-delay protections");
            collector.IsBlacklist=false;collector.MarkFilterDirty();Scan();
            check(!drops[1].active && storage.Ledger.GetAmount(stone)==9 && drops[2].active,
                "world-range whitelist admits only its real listed types");
            collector.IsBlacklist=true;collector.Filter.Clear();collector.MarkFilterDirty();
            storage.Ledger.MaxAmountPerType=8;Drop(3,ItemID.Wood,5,100000);Scan();
            check(storage.Ledger.GetAmount(wood)==8 && drops[3].active && drops[3].stack==4,
                "world-range partial collection leaves an exact live remainder at storage capacity");
            collector.Enabled=false;Drop(4,ItemID.StoneBlock,2,100000);Scan();
            check(drops[4].active && drops[4].stack==2,"disabled world-range collector does not touch any drop");
            collector.Radius=float.MaxValue;var tag=new TagCompound();collector.SaveData(tag);
            var restored=new MEDropCollectorEntity();restored.LoadData(tag);
            check(restored.WorldRange && restored.Radius==257 && !restored.Enabled,
                "unlimited range is saved as a finite canonical value and survives old-field loading");
            tag["radius"]=float.NaN;restored.LoadData(tag);
            check(restored.Radius==24 && !restored.WorldRange,"invalid saved radius cannot become unintended global collection");
            foreach(var pair in new[]{("256",256f),("257",257f),("1000000000",257f),("256.5",257f),("24.5",24.5f)})
                check(DropCollectorUIState.TryParseRadius(pair.Item1,out float radius) && radius==pair.Item2,
                    "typed collector radius normalizes "+pair.Item1+" consistently");
            foreach(string input in new[]{"","NaN","Infinity","-1","0","abc","1e99"})
                check(!DropCollectorUIState.TryParseRadius(input,out _),"invalid radius input is rejected: "+input);
            Main.netMode=NetmodeID.MultiplayerClient;
            collector.Enabled=true;collector.Radius=24;collector.IsBlacklist=true;collector.Filter=new(){ItemID.Wood};collector.MarkFilterDirty();
            byte[] Snapshot(float radius,params int[] ids)
            {
                using var buffer=new MemoryStream();using var writer=new BinaryWriter(buffer);
                writer.Write(MENet.ServerSyncCollector);writer.Write(collector.Position.X);writer.Write(collector.Position.Y);
                writer.Write(false);writer.Write(radius);writer.Write(false);
                MENet.WriteFilter(writer,ids);return buffer.ToArray();
            }
            void Receive(byte[] bytes) {using var reader=new BinaryReader(new MemoryStream(bytes));MENet.HandlePayload(reader,256);}
            var complete=Snapshot(100000,ItemID.StoneBlock,ItemID.StoneBlock,0,int.MaxValue);
            Receive(complete[..^1]);
            check(collector.Enabled && collector.Radius==24 && collector.IsBlacklist && collector.Filter.SequenceEqual(new[]{(int)ItemID.Wood}),
                "truncated real collector snapshot cannot partially change any previously published setting");
            Receive(Snapshot(float.NaN,ItemID.StoneBlock));
            Receive(complete.Concat(new byte[]{7}).ToArray());
            check(collector.Enabled && collector.Radius==24 && collector.Filter[0]==ItemID.Wood,
                "nonfinite and trailing real collector snapshots preserve the previous settings together");
            Receive(complete);
            check(!collector.Enabled && collector.WorldRange && !collector.IsBlacklist && collector.Filter.SequenceEqual(new[]{(int)ItemID.StoneBlock}),
                "complete collector snapshot publishes normalized global radius and a valid deduplicated filter atomically");
        }
        finally {Main.item=oldItems;Main.netMode=oldMode;}
    }
}
