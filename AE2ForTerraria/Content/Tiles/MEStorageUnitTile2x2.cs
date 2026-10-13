using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MEStorageUnitTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MEStorageUnitTile>();
    protected override int DropType => ModContent.ItemType<Items.MEStorageUnit>();
    protected override ModTileEntity Entity => ModContent.GetInstance<MEStorageUnitEntity>();
}
