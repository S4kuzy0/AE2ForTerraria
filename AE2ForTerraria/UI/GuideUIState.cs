using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI;

internal sealed class GuideUIState : MovableWindowState
{
    private static readonly WindowGeometry Geometry = WindowGeometry.Compact();
    private sealed record Page(GuidePage Kind, string Content, string Recipe, long Quantity, int Scroll = 0);
    private Page _page = new(GuidePage.Tutorial, "", "", 1);
    private readonly Stack<Page> _history = new();
    private GuideGoals _goals = new();
    private UIButton _back, _targets, _tutorial, _refresh;
    private UIGuideList _list;
    private ulong _refreshAt;
    private int _recipeRevision = -1;
    internal IReadOnlyList<GuideRow> Rows => _list?.Rows ?? Array.Empty<GuideRow>();

    public override void OnInitialize()
    {
        InitWindow(Geometry, "行动指南");
        _back = Button("返回", Back);
        _targets = Button("收藏下一步", () => Show(GuidePage.Goals));
        _tutorial = Button("终端入门", () => Show(GuidePage.Tutorial));
        _refresh = Button("重新检查", () => Refresh(true));
        _list = new UIGuideList { Navigate = Follow }; Window.Append(_list); RequestLayout();
    }
    private UIButton Button(string label, Action action)
    {
        var button = new UIButton(label, .7f); button.OnLeftClick += (_, _) => action(); Window.Append(button); return button;
    }
    internal void Show(GuidePage kind, int item = 0, string recipe = "", long quantity = 1, bool remember = true)
    {
        if (!remember) _history.Clear();
        if (remember && _list != null)
        {
            if (_history.Count >= BrowserHistory.Capacity)
            {
                var recent = _history.Take(BrowserHistory.Capacity - 1).Reverse().ToArray();
                _history.Clear(); foreach (var entry in recent) _history.Push(entry);
            }
            _history.Push(_page with { Scroll = _list.Scroll });
        }
        _page = new(kind, item > 0 ? BrowserContent.Name(item, kind == GuidePage.Npc) : "", recipe, Math.Max(1, quantity));
        _list.Scroll = 0; Refresh(true);
    }
    private void Back()
    {
        if (_history.Count == 0) { UISystem.Close(); return; }
        _page = _history.Pop(); Refresh(true); _list.Scroll = _page.Scroll;
    }
    internal void Follow(GuideRow row)
    {
        switch (row.Link)
        {
            case GuideLink.Sources: Show(GuidePage.Sources, row.Item, quantity: row.Quantity); break;
            case GuideLink.Recipe: Show(GuidePage.Recipe, row.Item, row.Recipe, row.Quantity); break;
            case GuideLink.Tutorial: Show(GuidePage.Tutorial); break;
            case GuideLink.Storage: Show(GuidePage.Storage); break;
            case GuideLink.Network: UISystem.OpenNetworkOverview(); break;
            case GuideLink.Npc: if (row.Npc > 0) Show(GuidePage.Npc, row.Npc); break;
        }
    }
    private void Refresh(bool force)
    {
        _refreshAt = Main.GameUpdateCount; _recipeRevision = StorageSystem.Recipes?.Revision ?? -1;
        int type = BrowserContent.Resolve(_page.Content, out int resolved, out bool npc) && npc == (_page.Kind == GuidePage.Npc) ? resolved : 0;
        List<GuideRow> rows;
        switch (_page.Kind)
        {
            case GuidePage.Goals: _goals.Update(force); rows = _goals.Rows(); break;
            case GuidePage.Sources: rows = WikiGuidance.Sources(type); break;
            case GuidePage.Npc: rows = WikiGuidance.Npc(type); break;
            case GuidePage.Recipe: rows = WikiGuidance.RecipeHelp(type, _page.Recipe, _page.Quantity); break;
            case GuidePage.Storage: rows = TerminalGuide.Storage(); break;
            default: rows = TerminalGuide.Tutorial(); break;
        }
        _list.SetRows(rows); _back.Enabled = true;
        _targets.Selected = _page.Kind == GuidePage.Goals; _tutorial.Selected = _page.Kind == GuidePage.Tutorial;
    }
    protected override void UpdateWindow(GameTime time)
    {
        base.UpdateWindow(time);
        if (_page.Kind == GuidePage.Goals)
        { if (_goals.Update()) _list.SetRows(_goals.Rows()); }
        else if (_recipeRevision != (StorageSystem.Recipes?.Revision ?? -1)
            || Main.GameUpdateCount - _refreshAt >= (_page.Kind == GuidePage.Sources ? 180u : 60u)) Refresh(false);
    }
    protected override void ApplyLayout()
    {
        base.ApplyLayout(); if (_list == null) return;
        var buttons = new[] { _back, _targets, _tutorial, _refresh };
        float width = (Geo.W - Pad * 2 - 12) / 4;
        for (int i = 0; i < buttons.Length; i++)
        { buttons[i].Left.Set(Pad + i * (width + 4), 0); buttons[i].Top.Set(34, 0); buttons[i].Width.Set(width, 0); buttons[i].Height.Set(24, 0); }
        _list.Left.Set(Pad, 0); _list.Top.Set(64, 0); _list.Width.Set(Geo.W - Pad * 2, 0); _list.Height.Set(Geo.H - 84, 0);
        Window.Recalculate();
    }
}

/// <summary>Wrapped action rows in a clipped viewport; scrollbar stops above the resize handle.</summary>
internal sealed class UIGuideList : UIElement
{
    internal IReadOnlyList<GuideRow> Rows { get; private set; } = Array.Empty<GuideRow>();
    internal Action<GuideRow> Navigate;
    internal int Scroll;
    private readonly List<(GuideRow Row, string[] Lines, int Top, int Height)> _layout = new();
    private readonly List<(Rectangle Bounds, GuideRow Row)> _hits = new();
    private float _width = -1;
    private int _height;
    private readonly Canvas _canvas;
    internal UIGuideList()
    {
        OverflowHidden = true;
        _canvas = new Canvas(this) { IgnoresMouseInteraction = true };
        _canvas.Width.Set(0, 1); _canvas.Height.Set(0, 1); Append(_canvas);
        OnLeftClick += (evt, _) =>
        {
            if (!ContainsPoint(evt.MousePosition)) return;
            foreach (var hit in _hits)
                if (hit.Bounds.Contains(evt.MousePosition.ToPoint())) { Navigate?.Invoke(hit.Row); return; }
        };
    }
    internal void SetRows(IReadOnlyList<GuideRow> rows)
    {
        if (Rows.SequenceEqual(rows)) return;
        Rows = rows; _width = -1; _hits.Clear();
    }
    private void ArrangeRows()
    {
        float width = GetDimensions().Width;
        if (_width == width) return;
        _width = width; _layout.Clear(); _height = 0;
        foreach (var row in Rows)
        {
            float available = Math.Max(24, width - 52);
            var lines = new List<string>();
            foreach (string paragraph in row.Text.Split('\n'))
            {
                string remaining = paragraph;
                if (remaining.Length == 0) lines.Add("");
                while (remaining.Length > 0)
                {
                    int count = remaining.Length;
                    while (count > 1 && FontAssets.MouseText.Value.MeasureString(remaining[..count]).X * .64f > available) count--;
                    lines.Add(remaining[..count]); remaining = remaining[count..];
                }
            }
            int height = Math.Max(27, lines.Count * 18 + 10);
            _layout.Add((row, lines.ToArray(), _height, height)); _height += height + (row.Heading ? 4 : 1);
        }
    }
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime); ArrangeRows();
        if (ContainsPoint(Main.MouseScreen))
        {
            Main.LocalPlayer.mouseInterface = true;
            int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
            Scroll -= wheel * 54; Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
        }
        Scroll = Math.Clamp(Scroll, 0, Math.Max(0, _height - (int)GetDimensions().Height));
    }
    private sealed class Canvas(UIGuideList owner) : UIElement
    {
        protected override void DrawSelf(SpriteBatch batch)
        {
            owner.ArrangeRows(); var area = owner.GetDimensions().ToRectangle(); owner._hits.Clear();
            owner.Scroll = Math.Clamp(owner.Scroll, 0, Math.Max(0, owner._height - area.Height));
            UITheme.Fill(batch, area, UITheme.Panel);
            foreach (var entry in owner._layout)
            {
                int y = area.Y + entry.Top - owner.Scroll;
                if (y >= area.Bottom || y + entry.Height <= area.Top) continue;
                var bounds = new Rectangle(area.X + 2, y, area.Width - 9, entry.Height);
                bool hover = owner.ContainsPoint(Main.MouseScreen) && bounds.Contains(Main.MouseScreen.ToPoint());
                if (entry.Row.Heading || entry.Row.Link != GuideLink.None)
                    UITheme.Fill(batch, bounds, hover && entry.Row.Link != GuideLink.None ? UITheme.BtnHot : UITheme.PanelAlt);
                if (entry.Row.Item > 0 || entry.Row.Npc > 0)
                    UIBrowserDetails.DrawIcon(batch, entry.Row.Item, entry.Row.Npc, new Rectangle(bounds.X + 2, y + 2, 23, 23));
                else if (entry.Row.Satisfied.HasValue)
                    UITheme.Fill(batch, new Rectangle(bounds.X + 9, y + 10, 6, 6), entry.Row.Satisfied.Value ? UITheme.Ok : UITheme.Warn);
                if ((entry.Row.Item > 0 || entry.Row.Npc > 0) && entry.Row.Satisfied.HasValue)
                    UITheme.Fill(batch, new Rectangle(bounds.X + 20, y + 20, 5, 5), entry.Row.Satisfied.Value ? UITheme.Ok : UITheme.Warn);
                Color color = entry.Row.Satisfied == false ? UITheme.Warn : entry.Row.Satisfied == true ? UITheme.Ok : entry.Row.Heading ? UITheme.Info : UITheme.Text;
                for (int i = 0; i < entry.Lines.Length; i++)
                    Utils.DrawBorderString(batch, entry.Lines[i], new Vector2(bounds.X + 28, y + 5 + i * 18), color, .64f);
                if (entry.Row.Link != GuideLink.None)
                {
                    owner._hits.Add((Rectangle.Intersect(bounds, area), entry.Row));
                    if (hover) Main.hoverItemName = "点击继续查看；返回可回到上一页";
                }
            }
            if (owner._height > area.Height)
            {
                int thumb = Math.Max(12, area.Height * area.Height / owner._height);
                int y = area.Y + (area.Height - thumb) * owner.Scroll / Math.Max(1, owner._height - area.Height);
                UITheme.Fill(batch, new Rectangle(area.Right - 4, y, 3, thumb), UITheme.Accent);
            }
        }
    }
}
