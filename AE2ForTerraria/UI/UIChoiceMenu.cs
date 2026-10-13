using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Transient, scrollable choice list. Opening a second list closes the first one.</summary>
internal sealed class UIChoiceMenu : UIElement
{
    private static UIChoiceMenu _active;
    public static bool IsOpen => _active?.Parent != null;
    private string[] _choices;
    private UIElement _anchor, _host;
    private int _scroll;
    private bool _wasDown;
    private Rectangle _anchorBounds, _hostBounds;
    public static void Close() { _active?.Remove(); _active = null; }
    public static void Show(UIElement host, UIElement anchor, string[] choices, Action<int, string> choose)
    {
        // The trigger belongs to its own popup. Toggle on click (release), never dismiss it on press.
        if (IsOpen && _active._anchor == anchor && _active._host == host) { Close(); return; }
        Close();
        if (choices == null || choices.Length == 0) return;
        var menu = new UIChoiceMenu { _choices = choices, _anchor = anchor, _host = host, _wasDown = Main.mouseLeft };
        menu.OnLeftClick += (e, _) =>
        {
            float y = e.MousePosition.Y - menu.GetDimensions().Y - 4;
            int visible = Math.Max(1, (int)((menu.GetDimensions().Height - 8) / 30));
            if (y < 0 || y >= visible * 30 || !menu.ContainsPoint(e.MousePosition)) return;
            int index = menu._scroll + (int)(y / 30);
            if (index >= 0 && index < choices.Length) { Close(); choose(index, choices[index]); }
        };
        _active = menu; host.Append(menu); menu.Position();
    }
    private void Position()
    {
        _anchorBounds = _anchor.GetDimensions().ToRectangle(); _hostBounds = _host.GetDimensions().ToRectangle();
        var content = _host.GetInnerDimensions(); var anchor = _anchor.GetDimensions();
        float w = Math.Min(270, content.Width - 32);
        float below = anchor.Y + anchor.Height - content.Y + 3;
        float available = Math.Max(0, content.Height - below - 16);
        float height = Math.Min(_choices.Length * 30 + 8, Math.Max(38, available));
        float y = below;
        if (available < 68 && anchor.Y - content.Y > available)
        {
            height = Math.Min(_choices.Length * 30 + 8, Math.Max(38, anchor.Y - content.Y - 19));
            y = anchor.Y - content.Y - height - 3;
        }
        Left.Set(Math.Clamp(anchor.X - content.X, 16, Math.Max(16, content.Width - w - 16)), 0);
        Top.Set(Math.Max(0, y), 0); Width.Set(w, 0); Height.Set(height, 0);
        Recalculate();
    }
    public override void Update(GameTime time)
    {
        base.Update(time);
        if (_anchor.Parent == null || Parent != _host) { Close(); return; }
        if (_anchorBounds != _anchor.GetDimensions().ToRectangle() || _hostBounds != _host.GetDimensions().ToRectangle()) Position();
        if (Main.mouseLeft && !_wasDown && !ContainsPoint(Main.MouseScreen) && !_anchor.ContainsPoint(Main.MouseScreen)) { Close(); return; }
        _wasDown = Main.mouseLeft;
        if (!ContainsPoint(Main.MouseScreen)) return;
        int visible = Math.Max(1, (int)((GetDimensions().Height - 8) / 30));
        int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
        _scroll = Math.Clamp(_scroll - delta * 3, 0, Math.Max(0, _choices.Length - visible));
        Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
        if (Main.LocalPlayer != null) Main.LocalPlayer.mouseInterface = true;
    }
    protected override void DrawSelf(SpriteBatch batch)
    {
        var d = GetDimensions(); UITheme.Fill(batch, d.ToRectangle(), new Color(26, 36, 60)); UITheme.Outline(batch, d.ToRectangle(), UITheme.BorderHot);
        int visible = Math.Max(1, (int)((d.Height - 8) / 30));
        for (int i = _scroll; i < Math.Min(_choices.Length, _scroll + visible); i++)
        {
            var rect = new Rectangle((int)d.X + 4, (int)d.Y + 4 + (i - _scroll) * 30, (int)d.Width - 8, 29);
            if (rect.Contains(Main.MouseScreen.ToPoint())) UITheme.Fill(batch, rect, UITheme.BtnHot);
            string label = _choices[i];
            while (label.Length > 1 && FontAssets.MouseText.Value.MeasureString(label).X * .75f > rect.Width - 12) label = label.Substring(0, label.Length - 1);
            Utils.DrawBorderString(batch, label, new Vector2(rect.X + 6, rect.Y + 7), UITheme.Text, .75f);
        }
        if (_choices.Length > visible) UITheme.DrawTriangleDown(batch, (int)d.X + (int)d.Width - 14, (int)d.Y + (int)d.Height - 12, UITheme.Muted);
    }
}
