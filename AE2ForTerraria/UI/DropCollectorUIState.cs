using System;
using System.Collections.Generic;
using System.Globalization;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Three direct settings above a searchable, non-consuming item filter.</summary>
public sealed class DropCollectorUIState : MovableWindowState
{
    private static readonly WindowGeometry Geometry = WindowGeometry.Compact();
    private UIButton _enableButton, _radiusButton, _modeButton;
    private UIText _list;
    private UIBrowserSearchField _filterSearch;
    private UIItemGrid _filterGrid;
    private UIGridScrollBar _scrollBar;
    private MEDropCollectorEntity _collector;
    private readonly HashSet<int> _listed = new();
    private int _filterRevision = -1;
    private bool _searchFocused, _radiusTyping;
    private string _lastSearch, _radiusBuffer = "";
    public override bool SearchBoxFocused => _searchFocused || _radiusTyping;

    public override void OnInitialize()
    {
        InitWindow(Geometry, ModText.Get("TitleCollector", "ME 掉落收集器"));
        _enableButton = Button("开关", () =>
        {
            if (_collector == null) return;
            _collector.Enabled = !_collector.Enabled; Push();
        });
        _radiusButton = Button("半径", BeginRadiusInput);
        _modeButton = Button("模式", () =>
        {
            if (_collector == null) return;
            _collector.IsBlacklist = !_collector.IsBlacklist; Push();
        });
        _filterSearch = new UIBrowserSearchField { Focused = () => _searchFocused, EndInput = () => _searchFocused = false };
        _filterSearch.OnLeftClick += (_, _) => { _radiusTyping = false; _searchFocused = true; };
        Window.Append(_filterSearch);
        Window.OnLeftMouseDown += (e, _) =>
        {
            if (e.Target != _radiusButton && _radiusTyping) CommitRadius();
            if (e.Target != _filterSearch) _searchFocused = false;
        };
        _list = new UIText("", .72f) { DynamicallyScaleDownToWidth = true };
        Window.Append(_list);
        _filterGrid = new UIItemGrid
        {
            CellSize = 44,
            IsMarked = key => _listed.Contains(key.Type),
            KeyboardEnabled = false,
            InputBlocked = () => UIChoiceMenu.IsOpen
                || UISystem.SecondaryInterface?.CurrentState is { } secondary && !ReferenceEquals(secondary, this)
                || UISystem.OverlayInterface?.CurrentState is { } overlay && !ReferenceEquals(overlay, this)
        };
        _filterGrid.OnLeftClick += (_, _) => ToggleHovered();
        Window.Append(_filterGrid);
        _scrollBar = new UIGridScrollBar { Target = _filterGrid }; Window.Append(_scrollBar);
        RequestLayout(); Refresh();
    }

    private UIButton Button(string text, Action action)
    {
        var button = new UIButton(text, .78f);
        button.OnLeftClick += (_, _) => action(); Window.Append(button); return button;
    }

    private void BeginRadiusInput()
    {
        if (_collector == null) return;
        _searchFocused = false; _radiusTyping = true;
        _radiusBuffer = "";
        Main.clrInput(); ImeInput.Blur(); Refresh();
    }

    internal static bool TryParseRadius(string text, out float radius)
    {
        radius = 0;
        if (!decimal.TryParse(text?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed)
            || parsed <= 0) return false;
        radius = parsed > (decimal)MEDropCollectorEntity.MaxFiniteRadius ? MEDropCollectorEntity.WorldRadius
            : MEDropCollectorEntity.NormalizeRadius((float)parsed);
        return true;
    }

    private void CommitRadius()
    {
        if (!_radiusTyping) return;
        _radiusTyping = false;
        if (_collector != null && TryParseRadius(_radiusBuffer, out float radius))
        { _collector.Radius = radius; Push(); }
        else UIStatus.Toast("请输入有效的正数半径；大于 256 格为无限");
        ImeInput.Blur(); Refresh();
    }

    private void Push()
    {
        if (_collector == null) return;
        _collector.Radius = MEDropCollectorEntity.NormalizeRadius(_collector.Radius);
        _collector.MarkFilterDirty();
        if (Main.netMode == NetmodeID.MultiplayerClient)
            MENet.SendCollectorConfig(_collector.Position, _collector.Enabled, _collector.Radius, _collector.IsBlacklist, _collector.Filter);
        Refresh();
    }

    private void ToggleHovered()
    {
        _searchFocused = false;
        if (_collector == null || _filterGrid.HoveredType <= 0 || _filterGrid.InputBlocked?.Invoke() == true) return;
        int type = _filterGrid.HoveredType;
        _collector.Filter ??= new();
        if (_listed.Contains(type)) _collector.Filter.RemoveAll(id => id == type);
        else _collector.Filter.Add(type);
        Push();
    }

    private void RebuildCandidates()
    {
        string query = _filterSearch?.Text.Trim() ?? "";
        _lastSearch = query;
        var candidates = new List<int>();
        for (int type = 1; type < ItemLoader.ItemCount; type++)
            if (!ItemID.Sets.Deprecated[type] && !string.IsNullOrWhiteSpace(Lang.GetItemNameValue(type))
                && (query.Length == 0 || ItemSearch.Matches(ItemKey.Crafted(type), query))) candidates.Add(type);
        _filterGrid.SetItems(candidates);
    }

    public void SetCollector(MEDropCollectorEntity collector)
    {
        _collector = collector; _filterRevision = -1; _radiusTyping = _searchFocused = false;
        RebuildCandidates(); Refresh();
    }

    protected override void UpdateWindow(GameTime time)
    {
        if (_radiusTyping)
        {
            string input = Main.GetInputText(_radiusBuffer);
            if (input.Length <= 28) _radiusBuffer = input;
            if (Main.inputTextEnter) CommitRadius();
            else if (Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))
            { _radiusTyping = false; ImeInput.Blur(); }
        }
        if ((_filterSearch?.Text.Trim() ?? "") != _lastSearch) RebuildCandidates();
        Refresh();
    }

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        float width = Geo.W - Pad * 2, enabled = width * .23f, radius = width * .33f;
        Place(_enableButton, Pad, 32, enabled - 4, 24);
        Place(_radiusButton, Pad + enabled, 32, radius - 4, 24);
        Place(_modeButton, Pad + enabled + radius, 32, width - enabled - radius, 24);
        Place(_filterSearch, Pad, 64, width, 28);
        Place(_list, Pad, 98, width, 20);
        float bottom = Geo.H - 22;
        Place(_filterGrid, Pad, 122, width - 14, Math.Max(40, bottom - 122));
        Place(_scrollBar, Geo.W - Pad - 10, 122, 10, Math.Max(40, bottom - 122));
        Window.Recalculate();
    }

    private static void Place(UIElement element, float x, float y, float width, float height)
    {
        element.Left.Set(x, 0); element.Top.Set(y, 0); element.Width.Set(width, 0); element.Height.Set(height, 0);
    }

    private void Refresh()
    {
        if (_collector == null || _enableButton == null) return;
        if (_filterRevision != _collector.FilterRevision)
        {
            _listed.Clear();
            if (_collector.Filter != null) foreach (int id in _collector.Filter) _listed.Add(id);
            _filterRevision = _collector.FilterRevision;
        }
        SetLabel(_enableButton, "开关：" + (_collector.Enabled ? "开" : "关"));
        _enableButton.Selected = _collector.Enabled;
        SetLabel(_radiusButton, _radiusTyping ? "半径：" + (_radiusBuffer.Length == 0 ? "输入" : _radiusBuffer) + "|"
            : "半径：" + (_collector.WorldRange ? "无限" : _collector.Radius.ToString("0.##", CultureInfo.InvariantCulture) + " 格"));
        _radiusButton.Selected = _radiusTyping;
        SetLabel(_modeButton, "模式：" + (_collector.IsBlacklist ? "黑名单" : "白名单"));
        UITextCache.Set(_list, "清单 " + _listed.Count + " 项 · 点物品加入/移出 · "
            + (_collector.IsBlacklist ? "名单内不收集" : "仅收集名单内"));
    }

    private static void SetLabel(UIButton button, string text) { if (button.Text != text) button.SetText(text); }
    public override void OnDeactivate()
    {
        _radiusTyping = _searchFocused = false; ImeInput.Blur(); base.OnDeactivate();
    }
    protected override void DrawHoverTooltip(Microsoft.Xna.Framework.Graphics.SpriteBatch batch)
    {
        _filterSearch.DrawCandidates(); _filterGrid.DrawTooltip(batch);
        if (_radiusButton.ContainsPoint(Main.MouseScreen)) UITooltip.Draw(batch, "收集半径", "点击输入；大于 256 格为无限，覆盖全世界掉落物");
    }
}
