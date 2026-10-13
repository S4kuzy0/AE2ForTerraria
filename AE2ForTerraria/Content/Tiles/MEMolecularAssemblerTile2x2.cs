using Terraria.ModLoader;
namespace AE2ForTerraria.Content.Tiles;
public sealed class MEMolecularAssemblerTile2x2 : LargeMachineTile
{
    protected override ModTile Legacy => ModContent.GetInstance<MEMolecularAssemblerTile>();
    protected override int DropType => ModContent.ItemType<Items.MEMolecularAssembler>();
    protected override ModTileEntity Entity => ModContent.GetInstance<MEMolecularAssemblerEntity>();
}
