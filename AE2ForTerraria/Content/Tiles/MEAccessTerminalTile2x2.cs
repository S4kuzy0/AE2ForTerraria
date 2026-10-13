using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MEAccessTerminalTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MEAccessTerminalTile>();
    protected override int DropType => ModContent.ItemType<Items.MEAccessTerminal>();
}
