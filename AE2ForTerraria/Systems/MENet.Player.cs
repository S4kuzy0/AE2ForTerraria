using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using AE2ForTerraria.Content.Tiles;

namespace AE2ForTerraria.Systems;
public static partial class MENet
{
    private const byte ClientPlayerState=(MessageTag<<ProtocolShift)|31;
    public static void SendPlayerState(MEPlayer state)
    {
        if (!IsClient || state.Player.whoAmI!=Main.myPlayer) return;
        var packet=CreatePacket(); packet.Write(ClientPlayerState); packet.Write((short)-1); packet.Write((short)-1);
        packet.Write(state.MagnetEnabled); packet.Write(state.BoundTerminals.Count);
        foreach (Point16 pos in state.BoundTerminals) { packet.Write(pos.X); packet.Write(pos.Y); }
        packet.Send();
    }
    private static void ReceivePlayerState(BinaryReader reader,int who)
    {
        bool magnet=reader.ReadBoolean(); int count=reader.ReadInt32();
        if (count<0 || count>MEPlayer.MaxBindings) throw new InvalidDataException("Invalid player bindings");
        // SyncPlayer may arrive before vanilla activates the joining player.
        // Decode this bounded payload even when the sender cannot yet apply it;
        // otherwise tML correctly reports unread packet bytes as a read underflow.
        var requested = new Point16[count];
        for (int i=0;i<count;i++)
            requested[i] = new Point16(reader.ReadInt16(), reader.ReadInt16());
        if (who<0 || who>=Main.maxPlayers || Main.player[who]?.active != true) return;
        Player player = Main.player[who];
        var valid=new System.Collections.Generic.List<Point16>(count);
        foreach (Point16 requestedPosition in requested)
        {
            Point16 pos = MachineTiles.Origin(requestedPosition.X, requestedPosition.Y);
            if (MENetwork.IsMEBlock(pos.X,pos.Y) && MachineTiles.CanonicalType(Main.tile[pos.X,pos.Y].TileType)==ModContent.TileType<MEAccessTerminalTile>()
                && NetworkRegistry.GetLevel(AnchorOf(pos),player.name)>=NetworkRegistry.LevelReadOnly && !valid.Contains(pos)) valid.Add(pos);
        }
        var state=player.GetModPlayer<MEPlayer>();
        state.MagnetEnabled=magnet; state.BoundTerminals.Clear(); state.BoundTerminals.AddRange(valid);
    }
}
