using System;
using System.Collections.Generic;
using System.IO;
using Terraria;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    private const byte ServerFragment = (MessageTag << ProtocolShift) | 36;
    private const int FragmentBytes=30000, MaxSnapshotBytes=16*1024*1024;
    private static int _snapshotSequence;
    private sealed class Assembly
    {
        public MemoryStream Data;
        public int Total;
        public ulong Started;
    }
    private static readonly Dictionary<int,Assembly> PendingSnapshots=new();
    private static void SendAuthorizedPayload(byte[] bytes,Terraria.DataStructures.Point16 position,int toClient,int ignoreClient)
    {
        var anchor=AnchorOf(position);
        if(anchor.X<0) return;
        for(int client=0;client<Main.maxPlayers;client++)
        {
            if((toClient>=0 && client!=toClient) || client==ignoreClient || !Main.player[client].active) continue;
            if(NetworkRegistry.GetLevel(anchor,Main.player[client].name)<NetworkRegistry.LevelReadOnly) continue;
            SendPayload(bytes,client,ignoreClient);
        }
    }
    private static void ClearFragments()
    {
        foreach (var pending in PendingSnapshots.Values) pending.Data.Dispose();
        PendingSnapshots.Clear();
    }
    private static void SendPayload(byte[] bytes, int toClient, int ignoreClient)
    {
        if (bytes.Length>MaxSnapshotBytes) { Mod.Logger.Error("ME snapshot exceeded maximum size: "+bytes.Length); return; }
        if (bytes.Length<=FragmentBytes)
        { var single=CreatePacket(); single.Write(bytes); single.Send(toClient,ignoreClient); return; }
        int id=unchecked(++_snapshotSequence);
        for (int offset=0;offset<bytes.Length;offset+=FragmentBytes)
        {
            int count=Math.Min(FragmentBytes,bytes.Length-offset);
            var packet=CreatePacket(); packet.Write(ServerFragment); packet.Write((short)0); packet.Write((short)0);
            packet.Write(id); packet.Write(bytes.Length); packet.Write(offset); packet.Write(count);
            packet.Write(bytes,offset,count); packet.Send(toClient,ignoreClient);
        }
    }
    private static void ReceiveFragment(BinaryReader reader)
    {
        int id=reader.ReadInt32(),total=reader.ReadInt32(),offset=reader.ReadInt32(),count=reader.ReadInt32();
        if (total<=0 || total>MaxSnapshotBytes || offset<0 || count<=0 || count>FragmentBytes || (long)offset+count>total)
            throw new InvalidDataException("Invalid snapshot fragment");
        if (offset==0)
        {
            if (PendingSnapshots.Count>=8) ClearFragments();
            if (PendingSnapshots.Remove(id,out var old)) old.Data.Dispose();
            PendingSnapshots[id]=new Assembly { Data=new MemoryStream(),Total=total,Started=Main.GameUpdateCount };
        }
        if (!PendingSnapshots.TryGetValue(id,out var pending) || pending.Total!=total || pending.Data.Length!=offset
            || Main.GameUpdateCount-pending.Started>600) { ClearFragments(); throw new InvalidDataException("Out of order snapshot"); }
        byte[] bytes=reader.ReadBytes(count);
        if (bytes.Length!=count) throw new EndOfStreamException();
        pending.Data.Write(bytes);
        if (pending.Data.Length==total)
        {
            PendingSnapshots.Remove(id); pending.Data.Position=0;
            using (pending.Data)
            using (var complete=new BinaryReader(pending.Data)) HandlePayload(complete,Main.myPlayer);
        }
    }
}
