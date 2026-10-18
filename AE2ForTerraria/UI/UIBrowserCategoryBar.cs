using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Representative game sprites with native hover labels; no extra categories.</summary>
internal sealed class UIBrowserCategoryBar : UIElement
{
    private readonly List<CategoryIcon> _main = new(), _children = new();
    public int Category { get; private set; }
    public int Child { get; private set; }
    public bool NpcMode { get; private set; }
    public bool Compact { get; init; }
    public Action Changed;
    private UIElement _hovered;
    public UIBrowserCategoryBar() => SetMode(false);
    private void SelectCategory(int index)
    {
        Category = index; Child = 0; RebuildChildren(); Changed?.Invoke();
    }
    private void SelectChild(int index)
    {
        Child = index;
        for (int i = 0; i < _children.Count; i++) _children[i].Selected = i + 1 == Child;
        Changed?.Invoke();
    }
    public void SetMode(bool npc)
    {
        NpcMode = npc; Category = Child = 0; RemoveAllChildren(); _main.Clear(); _children.Clear();
        string[] labels = npc ? BrowserCategories.NpcKinds : BrowserCategories.Items.Select(c => c.Name).ToArray();
        int[] portraits = { NPCID.Guide, NPCID.BlueSlime, NPCID.EyeofCthulhu, NPCID.Guide, NPCID.Bunny, NPCID.OldMan };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var button = new CategoryIcon(labels[i], i == 0 ? ItemID.CopperShortsword : npc ? 0 : BrowserCategories.Icon(BrowserCategories.Items[i]), npc && i > 0 ? portraits[i] : 0) { Selected = i == 0 };
            button.OnLeftClick += (_, _) => SelectCategory(index);
            _main.Add(button); Append(button);
        }
    }
    private void RebuildChildren()
    {
        foreach (var button in _children) button.Remove(); _children.Clear();
        for (int i = 0; i < _main.Count; i++) _main[i].Selected = Category == i;
        var children = NpcMode ? null : BrowserCategories.Items[Category].Children;
        if (children == null || children.Length == 0) return;
        for (int i = 0; i < children.Length; i++)
        {
            int index = i + 1;
            var child = children[i];
            var button = new CategoryIcon(child.Name, BrowserCategories.Icon(child), 0);
            button.OnLeftClick += (_, _) => SelectChild(index);
            _children.Add(button); Append(button);
        }
    }
    public float Arrange(float width)
    {
        Width.Set(width, 0);
        float height = Wrap(_main, width, 0);
        if (_children.Count > 0) height = Wrap(_children, width, height + 2);
        Height.Set(height, 0); return height;
    }
    private static float Wrap(List<CategoryIcon> buttons, float width, float top)
    {
        const int pitch = 26;
        int columns = Math.Max(1, (int)(width / pitch));
        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].Left.Set(i % columns * pitch, 0); buttons[i].Top.Set(top + i / columns * pitch, 0);
            buttons[i].Width.Set(24, 0); buttons[i].Height.Set(24, 0);
        }
        return top + (buttons.Count + columns - 1) / columns * pitch;
    }
    public bool Matches(int type) => NpcMode ? Category == 0 || BrowserCategories.NpcKind(type) == Category : BrowserCategories.Matches(type, Category, Child);
    protected override void DrawSelf(SpriteBatch batch) => _hovered = Parent?.GetElementAt(Main.MouseScreen);
    private sealed class CategoryIcon : UIElement
    {
        internal int ItemType { get; }
        internal int NpcType { get; }
        internal bool Selected;
        internal string Text { get; }
        private static readonly Item EmptyHover = new();
        internal CategoryIcon(string label, int item, int npc)
        {
            Text = label; ItemType = item; NpcType = npc;
        }
        protected override void DrawSelf(SpriteBatch batch)
        {
            var box = GetDimensions().ToRectangle();
            bool hover = Parent is UIBrowserCategoryBar owner && ReferenceEquals(owner._hovered, this);
            if (Selected || hover) UITheme.Fill(batch, box, Selected ? UITheme.BtnHot : UITheme.Btn);
            UIBrowserDetails.DrawIcon(batch, ItemType, NpcType, box);
            if (Selected) UITheme.Fill(batch, new Rectangle(box.X + 2, box.Bottom - 2, box.Width - 4, 2), UITheme.Select);
            if (hover)
            {
                Main.HoverItem = EmptyHover; Main.hoverItemName = Text;
            }
        }
    }
}
