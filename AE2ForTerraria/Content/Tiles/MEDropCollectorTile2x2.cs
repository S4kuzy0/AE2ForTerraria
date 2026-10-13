using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MEDropCollectorTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MEDropCollectorTile>();
    protected override int DropType => ModContent.ItemType<Items.MEDropCollector>();
    protected override ModTileEntity Entity => ModContent.GetInstance<MEDropCollectorEntity>();
}
