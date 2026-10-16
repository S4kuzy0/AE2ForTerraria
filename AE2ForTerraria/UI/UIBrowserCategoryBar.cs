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
    private readonly UIButton _categoryChoice, _childChoice;
    public int Category { get; private set; }
    public int Child { get; private set; }
    public bool NpcMode { get; private set; }
    public bool Compact { get; init; }
    public Action Changed;
    public UIBrowserCategoryBar()
    {
        _categoryChoice = new UIButton("分类：全部", .72f);
        _childChoice = new UIButton("全部", .72f);
        _categoryChoice.OnLeftClick += (_, _) => UIChoiceMenu.Show(Parent, _categoryChoice,
            _main.Select(b => b.Text).ToArray(), (index, _) => SelectCategory(index));
        _childChoice.OnLeftClick += (_, _) => UIChoiceMenu.Show(Parent, _childChoice,
            _children.Select(b => b.Text).ToArray(), (index, _) => SelectChild(index));
        SetMode(false);
    }
    private void SelectCategory(int index)
    {
        Category = index; Child = 0; RebuildChildren(); Changed?.Invoke();
    }
    private void SelectChild(int index)
    {
        Child = index;
        for (int j = 0; j < _children.Count; j++) _children[j].Selected = j == Child;
        Changed?.Invoke();
    }
    public void SetMode(bool npc)
    {
        NpcMode = npc; Category = Child = 0; RemoveAllChildren(); _main.Clear(); _children.Clear();
        string[] labels = npc ? BrowserCategories.NpcKinds : BrowserCategories.Items.Select(c => c.Name).ToArray();
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var b = new UIButton(labels[i], .85f) { Selected = i == 0 };
            b.OnLeftClick += (_, _) => SelectCategory(index);
            _main.Add(b); Append(b);
        }
        Append(_categoryChoice); Append(_childChoice);
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
            b.OnLeftClick += (_, _) => SelectChild(index);
            _children.Add(b); Append(b);
        }
    }
    public float Arrange(float width)
    {
        bool narrow = width < 560;
        foreach (var button in _main.Concat(_children)) button.IgnoresMouseInteraction = narrow;
        _categoryChoice.IgnoresMouseInteraction = _childChoice.IgnoresMouseInteraction = !narrow;
        _categoryChoice.Left.Set(narrow ? 0 : -10000, 0);
        _childChoice.Left.Set(narrow && _children.Count > 0 ? width * .52f : -10000, 0);
        if (narrow)
        {
            foreach (var button in _main.Concat(_children)) button.Left.Set(-10000, 0);
            _categoryChoice.SetText("分类：" + _main[Category].Text);
            _categoryChoice.Width.Set(_children.Count > 0 ? width * .52f - 4 : width, 0);
            _categoryChoice.Height.Set(22, 0);
            _childChoice.SetText(_children.Count > 0 ? _children[Child].Text : "全部");
            _childChoice.Width.Set(width * .48f, 0); _childChoice.Height.Set(22, 0);
            Width.Set(width, 0); Height.Set(22, 0); return 22;
        }
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
