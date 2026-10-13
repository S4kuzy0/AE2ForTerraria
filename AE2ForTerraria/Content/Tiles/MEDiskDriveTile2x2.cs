using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MEDiskDriveTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MEDiskDriveTile>();
    protected override int DropType => ModContent.ItemType<Items.MEDiskDrive>();
    protected override ModTileEntity Entity => ModContent.GetInstance<MEDiskDriveEntity>();
}
