using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MECraftingTerminalTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MECraftingTerminalTile>();
    protected override int DropType => ModContent.ItemType<Items.MECraftingTerminal>();
    protected override ModTileEntity Entity => ModContent.GetInstance<MECraftingTerminalEntity>();
}
