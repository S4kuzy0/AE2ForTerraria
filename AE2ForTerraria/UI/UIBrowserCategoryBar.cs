using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Wraps at the available width; an absent child category consumes no space.</summary>
internal sealed class UIBrowserCategoryBar : UIElement
{
    private readonly List<UIButton> _main = new(), _children = new();
    public int Category { get; private set; }
    public int Child { get; private set; }
    public bool NpcMode { get; private set; }
    public bool Compact { get; init; }
    public Action Changed;
    public UIBrowserCategoryBar() { SetMode(false); }
    public void SetMode(bool npc)
    {
        NpcMode = npc; Category = Child = 0; RemoveAllChildren(); _main.Clear(); _children.Clear();
        string[] labels = npc ? BrowserCategories.NpcKinds : BrowserCategories.Items.Select(c => c.Name).ToArray();
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var b = new UIButton(labels[i], .85f) { Selected = i == 0 };
            b.OnLeftClick += (_, _) => { Category = index; Child = 0; RebuildChildren(); Changed?.Invoke(); };
            _main.Add(b); Append(b);
        }
    }
    private void RebuildChildren()
    {
        foreach (var b in _children) b.Remove(); _children.Clear();
        for (int i = 0; i < _main.Count; i++) _main[i].Selected = Category == i;
        var children = NpcMode ? null : BrowserCategories.Items[Category].Children;
        if (children == null || children.Length == 0) return;
        var labels = new[] { "全部" }.Concat(children.Select(c => c.Name)).ToArray();
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i; var b = new UIButton(labels[i], .75f) { Selected = i == 0 };
            b.OnLeftClick += (_, _) => { Child = index; for (int j = 0; j < _children.Count; j++) _children[j].Selected = j == Child; Changed?.Invoke(); };
            _children.Add(b); Append(b);
        }
    }
    public float Arrange(float width)
    {
        Width.Set(width, 0); float y = Wrap(_main, width, 0, Compact ? .7f : .85f, Compact ? 24 : 32);
        if (_children.Count > 0) y = Wrap(_children, width, y + (Compact ? 2 : 3), Compact ? .65f : .75f, Compact ? 22 : 29);
        Height.Set(y, 0); return y;
    }
    private float Wrap(List<UIButton> buttons, float width, float top, float scale, float height)
    {
        float x = 0, y = top;
        foreach (var b in buttons)
        {
            float w = Math.Min(width, Math.Max(Compact ? 32 : 46, FontAssets.MouseText.Value.MeasureString(b.Text).X * scale + (Compact ? 12 : 22)));
            if (x > 0 && x + w > width) { x = 0; y += height + (Compact ? 2 : 3); }
            b.SetText(b.Text, scale, false); b.Flat = Compact;
            b.Left.Set(x, 0); b.Top.Set(y, 0); b.Width.Set(w, 0); b.Height.Set(height, 0); x += w + (Compact ? 2 : 4);
        }
        return y + height;
    }
    public bool Matches(int type) => NpcMode ? Category == 0 || BrowserCategories.NpcKind(type) == Category : BrowserCategories.Matches(type, Category, Child);
}
