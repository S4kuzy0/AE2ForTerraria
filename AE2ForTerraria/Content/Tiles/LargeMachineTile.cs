using System.Linq;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace AE2ForTerraria.Content.Tiles;

/// <summary>Shared placement/anchor/lifetime rules, with the original machine's interaction.</summary>
public abstract class LargeMachineTile : ModTile
{
    protected abstract ModTile Legacy { get; }
    protected abstract int DropType { get; }
    protected virtual ModTileEntity Entity => null;
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = false;
        Main.tileBlockLight[Type] = false;
        Main.tileFrameImportant[Type] = true;
        TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
        TileObjectData.newTile.LavaDeath = false;
        TileObjectData.newTile.CoordinateWidth = 16;
        TileObjectData.newTile.CoordinateHeights = new[] { 16, 16 };
        TileObjectData.newTile.CoordinatePadding = 2;
        // Machines can be part of suspended wiring; breaking a supporting cable must not destroy stored data.
        TileObjectData.newTile.AnchorBottom = default;
        TileObjectData.newTile.StyleHorizontal = true;
        if (Entity != null) TileObjectData.newTile.HookPostPlaceMyPlayer = Entity.Generic_HookPostPlaceMyPlayer;
        TileObjectData.addTile(Type);
        RegisterItemDrop(DropType);
        AddMapEntry(new Color(145, 170, 190), Terraria.Localization.Language.GetText("Mods.AE2ForTerraria.Items." + Legacy.Name[..^4] + ".DisplayName"));
    }
    public override bool RightClick(int i, int j)
    { var p = MachineTiles.Origin(i, j); return Legacy.RightClick(p.X, p.Y); }
    public override void MouseOver(int i, int j)
    { var p = MachineTiles.Origin(i, j); Legacy.MouseOver(p.X, p.Y); }
    public override bool CanKillTile(int i, int j, ref bool blockDamaged)
    {
        var p = MachineTiles.Origin(i, j);
        if (TileEntity.ByPosition.TryGetValue(p, out var entity) && entity is MECraftingTerminalEntity terminal
            && terminal.Stations.Any(item => item != null && !item.IsAir))
        {
            if (Main.netMode != NetmodeID.Server) UIStatus.Toast("请先取出工作站，再拆除合成终端");
            return false;
        }
        return Legacy.CanKillTile(p.X, p.Y, ref blockDamaged);
    }
    public override bool CanExplode(int i, int j)
    { bool damaged = false; return CanKillTile(i, j, ref damaged); }
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        // Liquid and other direct destruction paths can skip the normal CanKillTile preflight.
        // Never remove a multi-tile entity from an individual cell's callback.
        bool damaged = false;
        if (!CanKillTile(i, j, ref damaged)) fail = true;
    }
    public override void KillMultiTile(int i, int j, int frameX, int frameY)
    {
        int x = i, y = j; // KillMultiTile already receives the top-left corner.
        // The legacy per-cell KillTile hook is deliberately called only once for the whole machine.
        bool fail = false, effect = false, noItem = false;
        Legacy.KillTile(x, y, ref fail, ref effect, ref noItem);
        if (fail || effect) return;
        if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var entity) && entity is ModTileEntity modEntity)
            modEntity.Kill(x, y);
        MENetwork.InvalidateCache();
    }
}
