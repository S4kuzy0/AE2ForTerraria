using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.UI;

internal static class FavoriteInput
{
    internal static bool IsDown => Main.keyState.IsKeyDown(Keys.LeftAlt) || Main.keyState.IsKeyDown(Keys.RightAlt);
    internal static void Cursor() { if (IsDown) Main.cursorOverride = CursorOverrideID.FavoriteStar; }
    internal static void Toggle(int type, bool npc = false, int recipe = -1, long quantity = 1)
    {
        if (type <= 0) return;
        if (BrowserFavorites.Toggle(type, npc, recipe, quantity))
            Systems.UIStatus.Toast(BrowserFavorites.Contains(type, npc) ? "已收藏合成树" : "已取消收藏");
    }
    internal static void Mark(SpriteBatch batch, Rectangle box)
    {
        // A gold star and corner accents distinguish personal favorites from rarity colors.
        UITheme.DrawStar(batch, box.Right - 6, box.Top + 6, true, Main.OurFavoriteColor, 4f);
        UITheme.Fill(batch, new Rectangle(box.X, box.Y, 7, 1), Main.OurFavoriteColor);
        UITheme.Fill(batch, new Rectangle(box.X, box.Y, 1, 7), Main.OurFavoriteColor);
    }
}
