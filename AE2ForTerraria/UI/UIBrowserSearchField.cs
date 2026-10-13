using System;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Terminal text input using Terraria's IME, styled like the catalogue search field.</summary>
internal sealed class UIBrowserSearchField : UIElement
{
    public string Text { get; private set; } = "";
    public Func<bool> Focused;
    public Action EndInput;
    private bool _wasFocused;
    public override void Update(GameTime time)
    {
        base.Update(time);
        bool focused = Focused?.Invoke() == true;
        if (!focused) { if (_wasFocused) ImeInput.Blur(); _wasFocused = false; return; }
        if (!_wasFocused) Main.clrInput();
        _wasFocused = true;
        ImeInput.Focus();
        Text = Main.GetInputText(Text);
        if (Main.inputTextEnter || Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))
        { EndInput?.Invoke(); ImeInput.Blur(); }
    }
    protected override void DrawSelf(SpriteBatch batch)
    {
        var d = GetDimensions(); var rect = d.ToRectangle(); bool focused = Focused?.Invoke() == true;
        UITheme.FillRounded(batch, rect, UITheme.Field, 4);
        UITheme.Outline(batch, rect, focused ? UITheme.Accent : UITheme.Border);
        UITheme.DrawMagnifier(batch, rect.X + 7, rect.Y + (rect.Height - 11) / 2, UITheme.Muted);
        string text = Text.Length == 0 && !focused ? "搜索名称或拼音" : Text + (focused ? "|" : "");
        while (text.Length > 1 && FontAssets.MouseText.Value.MeasureString(text).X * .85f > d.Width - 36) text = text.Substring(1);
        Utils.DrawBorderString(batch, text, new Vector2(d.X + 27, d.Y + 6), Text.Length == 0 ? UITheme.Muted : UITheme.Text, .85f);
    }
    public void DrawCandidates()
    {
        if (Focused?.Invoke() == true) { var d = GetDimensions(); ImeInput.DrawCandidates(new Vector2(d.X + 12, d.Y + d.Height + 24)); }
    }
}
