using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using AE2ForTerraria.Systems;

namespace AE2ForTerraria.UI;

/// <summary>Compact station inventory on the recipe page; operations retain server authority.</summary>
internal sealed class UIStationGrid : UIElement
{
    private readonly UIList _rows = new() { ListPadding = 3 };
    private readonly UIScrollbar _scroll = new();
    private readonly UIText _title = new("工作站", .72f);
    private int _signature = int.MinValue;
    private float _width;
    private float _height=62;
    internal Action LayoutChanged;
    internal Action ContentsChanged;
    internal float RequiredHeight => _height;
    internal const float GridHeight = 104;

    public UIStationGrid()
    {
        _rows.SetScrollbar(_scroll);
        Append(_title); Append(_rows); Append(_scroll);
    }

    public void SetBounds(float x, float y, float width)
    {
        Left.Set(x, 0); Top.Set(y, 0); Width.Set(width, 0); Height.Set(_height, 0);
        if (_width != width) { _width = width; _signature = int.MinValue; }
        _title.Width.Set(width, 0); _title.Height.Set(20, 0);
        _rows.Top.Set(23, 0); _rows.Width.Set(width - 16, 0); _rows.Height.Set(_height-26, 0);
        _scroll.Left.Set(width - 12, 0); _scroll.Top.Set(23, 0);
        _scroll.Width.Set(12, 0); _scroll.Height.Set(_height-26, 0);
    }

    public override void Update(GameTime time)
    {
        var hosts = UISystem.GetStationHosts();
        var hash = new HashCode(); hash.Add(_width); hash.Add(UISystem.TerminalReadable);
        foreach (var host in hosts)
        {
            hash.Add(host.Position); hash.Add(host.Stations.Length);
            foreach (var item in host.Stations) { hash.Add(item?.type); hash.Add(item?.stack); }
        }
        int signature = hash.ToHashCode();
        if (_signature != signature)
        {
            _signature = signature; float offset = _scroll.ViewPosition;
            ContentsChanged?.Invoke();
            _rows.Clear(); int column = 0, occupied = 0;
            int columns = Math.Max(1, (int)((_width - 16) / 39));
            UIElement row = null;
            foreach (var host in hosts)
                for (int i = 0; i < host.Stations.Length; i++)
                {
                    var item = host.Stations[i];
                    if ((item == null || item.IsAir) && host.Position != UISystem.ActiveCraftingTerminal?.Position) continue;
                    if (item != null && !item.IsAir) occupied++;
                    if (column == 0)
                    {
                        row = new UIElement(); row.Width.Set(0, 1); row.Height.Set(36, 0); _rows.Add(row);
                    }
                    var slot = UIIntegratedCraftingPanel.CreateStationSlot(host.Position, i, () => _signature = int.MinValue);
                    slot.Left.Set(column * 39, 0); slot.Width.Set(36, 0); slot.Height.Set(36, 0); row.Append(slot);
                    column = (column + 1) % columns;
                }
            _title.SetText(UISystem.TerminalReadable ? "工作站 · 已接入 " + occupied + " 个" : UISystem.TerminalAccessStatus);
            if(_rows.Count>2) {if(_scroll.Parent==null)Append(_scroll);} else {_scroll.Remove();_scroll.ViewPosition=0;}
            float height=26+Math.Min(2,Math.Max(1,_rows.Count))*39;
            if(_height!=height) {_height=height;LayoutChanged?.Invoke();}
            Recalculate(); _scroll.ViewPosition = offset;
        }
        base.Update(time);
    }
}
