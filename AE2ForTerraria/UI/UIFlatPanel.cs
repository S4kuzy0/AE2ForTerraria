using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.UI.Elements;

namespace AE2ForTerraria.UI;

/// <summary>A small meter must not inherit UIPanel's twelve-pixel texture corners or padding.</summary>
public sealed class UIFlatPanel : UIPanel
{
    public UIFlatPanel() { SetPadding(0); }
    protected override void DrawSelf(SpriteBatch spriteBatch)
        => UITheme.FillRounded(spriteBatch, GetDimensions().ToRectangle(), BackgroundColor, 0);
}
