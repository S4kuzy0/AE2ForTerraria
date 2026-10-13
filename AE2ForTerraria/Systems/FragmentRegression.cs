using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

/// <summary>Opt-in actual packet dispatch/fragment receiver checks. No sockets, world data, or character writes.</summary>
internal static class FragmentRegression
{
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int CompletionNonce = 31415;
    private static FieldInfo Field(string name) => typeof(MENet).GetField(name,StaticFields)
        ?? throw new InvalidOperationException("Fragment regression could not locate " + name);
    private static IDictionary Pending => (IDictionary)Field("PendingSnapshots").GetValue(null);
    private static byte Message(string name) => (byte)Field(name).GetRawConstantValue();
    private static MemoryStream Data(object assembly) => (MemoryStream)assembly.GetType().GetField("Data")!.GetValue(assembly);
    private static int Nonce => (int)Field("_pendingUpgrade").GetValue(null);

    public static void Run(Action<bool,string> check)
    {
        if(!RegressionChecks.IsRunning || !Main.gameMenu)
            throw new InvalidOperationException("Fragment regression requires the opt-in menu-only runner.");
        if(MENet.InventoryTransactionPending || MENet.CursorPending)
            throw new InvalidOperationException("Fragment regression refuses to replace a pending inventory transaction.");
        for(int player=0;player<Main.maxPlayers;player++)
            if(Main.player[player]?.active==true)
                throw new InvalidOperationException("Fragment regression refuses active players.");
        FieldInfo clock=typeof(Main).GetField("_gameUpdateCount",StaticFields)
            ?? throw new InvalidOperationException("Fragment regression could not locate the saved tick counter.");
        object originalClock=clock.GetValue(null);
        int originalMode=Main.netMode,originalPlayer=Main.myPlayer;
        var restore=new List<Action>();
        try
        {
            // Remove originals without disposing them. Reset inside the isolated fixture
            // is then allowed to dispose only streams created by these test packets.
            foreach(FieldInfo field in typeof(MENet).GetFields(StaticFields))
            {
                if(field.IsLiteral) continue;
                object value=field.GetValue(null);
                if(value is IDictionary dictionary)
                {
                    var entries=new List<DictionaryEntry>();
                    foreach(DictionaryEntry entry in dictionary) entries.Add(entry);
                    restore.Add(()=> { dictionary.Clear(); foreach(var entry in entries) dictionary.Add(entry.Key,entry.Value); });
                    dictionary.Clear();
                }
                else if(!field.IsInitOnly) restore.Add(()=>field.SetValue(null,value));
            }
            Main.netMode=NetmodeID.MultiplayerClient; Main.myPlayer=0;
            byte[] payload=CompletionPayload();
            void ResetFixture()
            {
                MENet.Reset(); clock.SetValue(null,(uint)10000);
                // An observable, inert completion marker. No item/slot/player is locked.
                Field("_pendingUpgrade").SetValue(null,CompletionNonce);
            }

            ResetFixture();
            Send(1,payload.Length,0,3,payload[..3]);
            var partial=Data(Pending[1]);
            check(Pending.Count==1 && partial.Length==3 && Nonce==CompletionNonce,
                "fragment prefix is retained without dispatching an incomplete inner packet");
            Send(1,payload.Length,3,payload.Length-3,payload[3..]);
            check(Pending.Count==0 && !partial.CanRead && Nonce==0,
                "complete fragment stream dispatches actual completion handler once and disposes its buffer");

            ResetFixture();
            Send(2,payload.Length,0,3,payload[..3]);
            var original=Data(Pending[2]);
            Send(2,payload.Length,0,3,payload[..3]);
            check(Pending.Count==1 && !original.CanRead && Data(Pending[2]).Length==3 && Nonce==CompletionNonce,
                "duplicate first fragment replaces that assembly without publishing its partial payload");
            Send(2,payload.Length,3,3,payload[3..6]);
            Send(2,payload.Length,3,3,payload[3..6]);
            check(Pending.Count==0 && Nonce==CompletionNonce,
                "duplicate noninitial fragment is rejected before any inner packet is published");

            ResetFixture();
            Send(3,payload.Length,3,3,payload[3..6]);
            check(Pending.Count==0 && Nonce==CompletionNonce,"continuation without an initial fragment is rejected");
            Send(3,payload.Length,0,3,payload[..3]);
            Send(3,payload.Length,6,3,payload[6..]);
            check(Pending.Count==0 && Nonce==CompletionNonce,"out-of-order continuation clears partial data without publishing");

            ResetFixture();
            Send(4,payload.Length,0,3,payload[..3]);
            Send(4,payload.Length+1,3,3,payload[3..6]);
            check(Pending.Count==0 && Nonce==CompletionNonce,"changing a snapshot total rejects the existing assembly");

            ResetFixture();
            Send(5,payload.Length,0,3,payload[..3]);
            var expired=Data(Pending[5]);
            clock.SetValue(null,(uint)10601);
            check(Pending.Count==1 && expired.CanRead && expired.Length==3,
                "recorded limitation: advancing time alone leaves incomplete fragments resident until another protocol action");
            Send(5,payload.Length,3,6,payload[3..]);
            check(Pending.Count==0 && !expired.CanRead && Nonce==CompletionNonce,
                "continuation arriving after expiry is rejected and disposes the stale buffer");

            ResetFixture();
            Send(6,payload.Length,0,3,payload[..3]);
            var truncated=Data(Pending[6]);
            Send(6,payload.Length,3,6,payload[3..5]);
            check(Pending.Count==1 && truncated.Length==3 && Nonce==CompletionNonce,
                "truncated fragment never appends a partial body or dispatches the inner message");
            Send(6,payload.Length,3,6,payload[3..]);
            check(Pending.Count==0 && !truncated.CanRead && Nonce==0,
                "valid continuation can finish an assembly retained after a truncated body");

            ResetFixture();
            int maxTotal=(int)Field("MaxSnapshotBytes").GetRawConstantValue();
            int maxFragment=(int)Field("FragmentBytes").GetRawConstantValue();
            check(maxTotal==16*1024*1024 && maxFragment==30000,"snapshot and fragment byte bounds are explicit constants");
            foreach(var invalid in new[] { (0,0,1),(maxTotal+1,0,1),(9,-1,1),(9,0,0),(9,0,maxFragment+1),(9,8,2) })
            {
                Send(7,invalid.Item1,invalid.Item2,invalid.Item3,new byte[]{1});
                check(Pending.Count==0 && Nonce==CompletionNonce,
                    "invalid fragment bounds reject before allocation: " + invalid);
            }
            var boundedStreams=new List<MemoryStream>();
            for(int id=10;id<18;id++)
            {
                Send(id,maxTotal,0,1,new byte[]{1});
                boundedStreams.Add(Data(Pending[id]));
            }
            check(Pending.Count==8 && boundedStreams.TrueForAll(stream=>stream.Length==1),
                "eight partial snapshots are accepted without preallocating declared total sizes");
            Send(18,maxTotal,0,1,new byte[]{1});
            check(Pending.Count==1 && Pending.Contains(18) && boundedStreams.TrueForAll(stream=>!stream.CanRead),
                "ninth initial fragment enforces pending-count bound by disposing all earlier assemblies");

            ResetFixture();
            Send(20,payload.Length,0,3,payload[..3]);
            Send(21,payload.Length,0,3,payload[..3]);
            var unrelated=Data(Pending[20]);
            Send(21,payload.Length,6,3,payload[6..]);
            check(Pending.Count==0 && !unrelated.CanRead && Nonce==CompletionNonce,
                "recorded limitation: one out-of-order stream discards unrelated pending snapshots too");

            ResetFixture();
            Send(22,payload.Length,0,payload.Length,payload);
            check(Nonce==0 && Pending.Count==0,"single-fragment complete snapshot reaches actual inner dispatcher");
            Field("_pendingUpgrade").SetValue(null,CompletionNonce);
            Send(22,payload.Length,6,3,payload[6..]);
            check(Nonce==CompletionNonce && Pending.Count==0,"late trailing duplicate cannot dispatch an already completed snapshot");
            Send(22,payload.Length,0,payload.Length,payload);
            check(Nonce==0 && Pending.Count==0,
                "recorded limitation: completed snapshot IDs are reusable by a new initial fragment");

            ResetFixture();
            Send(23,payload.Length,0,3,payload[..3]);
            var reset=Data(Pending[23]);
            MENet.Reset();
            check(Pending.Count==0 && !reset.CanRead && !MENet.StorageUpgradePending,
                "network reset releases every incomplete stream and temporary transaction state");
        }
        finally
        {
            // Do this before reinstating originals, so prior session streams stay intact.
            MENet.Reset();
            for(int index=restore.Count-1;index>=0;index--) restore[index]();
            clock.SetValue(null,originalClock);
            Main.netMode=originalMode; Main.myPlayer=originalPlayer;
        }
    }

    private static byte[] CompletionPayload()
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        writer.Write(Message("ServerStorageUpgradeComplete")); writer.Write((short)0); writer.Write((short)0);
        writer.Write(CompletionNonce); return stream.ToArray();
    }
    private static void Send(int id,int total,int offset,int count,byte[] data)
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        writer.Write(Message("ServerFragment")); writer.Write((short)0); writer.Write((short)0);
        writer.Write(id); writer.Write(total); writer.Write(offset); writer.Write(count); writer.Write(data);
        stream.Position=0; using var reader=new BinaryReader(stream);
        MENet.HandlePayload(reader,256);
    }
}
