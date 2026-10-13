using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.UI;

/// <summary>One frame and the native alpha/color passes, shared by catalogue, links and portrait.</summary>
internal static class NpcVisuals
{
    internal static Rectangle Frame(Texture2D texture, int type) => texture.Frame(1, Math.Max(1, Main.npcFrameCount[type]));
    internal static string ModName(int type) => NPCLoader.GetNPC(type)?.Mod.DisplayName ?? "Terraria";
    internal static void Draw(SpriteBatch batch, int type, Rectangle box, float maxScale = 1f)
    {
        if (type <= 0 || type >= NPCLoader.NPCCount || box.Width <= 0 || box.Height <= 0) return;
        Main.instance.LoadNPC(type);
        var texture = TextureAssets.Npc[type].Value;
        var frame = Frame(texture, type);
        float scale = Math.Min(maxScale, Math.Min((box.Width - 4f) / frame.Width, (box.Height - 4f) / frame.Height));
        if (scale <= 0) return;
        ContentSamples.NpcsByNetId.TryGetValue(type, out var npc);
        var center = box.Center.ToVector2(); var origin = frame.Size() / 2f;
        batch.Draw(texture, center, frame, npc?.GetAlpha(Color.White) ?? Color.White, 0, origin, scale, SpriteEffects.None, 0);
        if (npc != null && npc.color != default)
            batch.Draw(texture, center, frame, npc.GetColor(Color.White), 0, origin, scale, SpriteEffects.None, 0);
    }
}
