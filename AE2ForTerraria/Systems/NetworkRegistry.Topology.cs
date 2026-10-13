using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

public sealed partial class NetworkRegistry
{
    // Coordinates retain the previous policy association across anchor removal, splits and channel changes.
    private static readonly Dictionary<Point16,string> Membership = new();
    private static readonly Dictionary<string,List<string>> Components=new();
    private static List<string> Policies(Point16 anchor) => Components.TryGetValue(Key(anchor),out var keys) ? keys : new() {Key(anchor)};
    public static void TrackComponent(List<Point16> blocks, Point16 anchor)
    {
        if (Main.netMode==NetmodeID.MultiplayerClient || blocks==null || blocks.Count==0 || anchor.X<0) return;
        string destination=Key(anchor);
        // Assign newly seen blocks from their wired component only. A same-channel remote bridge
        // must never donate its owner's policy to a newly placed local bridge.
        if (blocks.Any(point => !Membership.ContainsKey(point)))
        {
          var unvisited = new HashSet<Point16>(blocks);
          while (unvisited.Count > 0)
          {
            Point16 start = unvisited.First();
            var wired = new List<Point16>();
            var queue = new Queue<Point16>();
            var localOrigins = new SortedSet<string>(StringComparer.Ordinal);
            queue.Enqueue(start); unvisited.Remove(start);
            Point16 localAnchor = start;
            while (queue.Count > 0)
            {
                Point16 point = queue.Dequeue(); wired.Add(point);
                if (point.Y < localAnchor.Y || point.Y == localAnchor.Y && point.X < localAnchor.X) localAnchor = point;
                if (Membership.TryGetValue(point, out string policy)) localOrigins.Add(policy);
                else if (Owners.ContainsKey(Key(point)) || Names.ContainsKey(Key(point))) localOrigins.Add(Key(point));
                foreach (var next in new[] { new Point16(point.X - 1, point.Y), new Point16(point.X + 1, point.Y),
                    new Point16(point.X, point.Y - 1), new Point16(point.X, point.Y + 1) })
                    if (unvisited.Remove(next)) queue.Enqueue(next);
            }
            foreach (Point16 point in wired)
            {
                if (Membership.ContainsKey(point)) continue;
                string ownKey = Key(point);
                // A new block between two already differently-owned wired networks has no unique
                // original owner. It cannot be used as a recovery privilege for either side.
                Membership[point] = Owners.ContainsKey(ownKey) || Names.ContainsKey(ownKey) ? ownKey
                    : localOrigins.Count == 1 ? localOrigins.Min : localOrigins.Count == 0 ? Key(localAnchor) : ownKey;
            }
          }
        }
        var origins=new SortedSet<string>(StringComparer.Ordinal);
        foreach (var point in blocks)
        {
            if (Membership.TryGetValue(point,out string old)) origins.Add(old);
            else if (Owners.ContainsKey(Key(point)) || Names.ContainsKey(Key(point))) origins.Add(Key(point));
        }
        if (origins.Count==0) origins.Add(destination);
        Components[destination]=origins.ToList();
        // Never collapse original policies on merge. Disconnecting restores each side's own owner and ACL.
    }

    /// <summary>Resolve the host's original owner, never the current merged component's owner.</summary>
    public static string OriginalOwner(Point16 position)
    {
        string policy = Membership.TryGetValue(position, out string original) ? original : Key(position);
        return Owners.TryGetValue(policy, out string owner) ? owner : null;
    }

    /// <summary>Only the preserved local policy can authorize disconnecting this one bridge.</summary>
    public static bool CanManageOriginalBlock(Point16 position, string playerName)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || string.IsNullOrWhiteSpace(playerName)
            || !Membership.TryGetValue(position, out string policy) || !Owners.TryGetValue(policy, out string owner)) return false;
        return string.Equals(owner, playerName, StringComparison.Ordinal)
            || Acl.TryGetValue(policy, out var table) && table.TryGetValue(playerName, out int level) && level >= LevelManage;
    }
    public static void InvalidateComponents() => Components.Clear();
    public static void PruneRemovedBlocks()
    {
        if (Main.gameMenu) return;
        foreach (var point in Membership.Keys.ToArray())
            if (!MENetwork.IsMEBlock(point.X,point.Y)) Membership.Remove(point);
    }
    private static void SaveMembership(TagCompound tag)
    {
        var entries=new List<TagCompound>();
        foreach (var pair in Membership)
            if (MENetwork.IsMEBlock(pair.Key.X,pair.Key.Y))
                entries.Add(new TagCompound { ["x"]=(int)pair.Key.X,["y"]=(int)pair.Key.Y,["policy"]=pair.Value });
        tag["networkMembership"]=entries;
    }
    private static void LoadMembership(TagCompound tag)
    {
        Membership.Clear(); Components.Clear();
        if (!tag.ContainsKey("networkMembership")) return;
        foreach (var entry in tag.GetList<TagCompound>("networkMembership"))
            Membership[new Point16(TagCompat.ReadInt(entry,"x"),TagCompat.ReadInt(entry,"y"))]=entry.GetString("policy");
    }
}
