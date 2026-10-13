using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MECraftingCpuTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MECraftingCpuTile>();
    protected override int DropType => ModContent.ItemType<Items.MECraftingCpu>();
    protected override ModTileEntity Entity => ModContent.GetInstance<MECraftingCpuEntity>();
}
