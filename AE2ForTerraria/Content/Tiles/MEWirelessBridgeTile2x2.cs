using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MEWirelessBridgeTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MEWirelessBridgeTile>();
    protected override int DropType => ModContent.ItemType<Items.MEWirelessBridge>();
    protected override ModTileEntity Entity => ModContent.GetInstance<MEWirelessBridgeEntity>();
}
