using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Progression;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>A draft subpage. Reordering never changes the live graph until Save succeeds.</summary>
internal sealed class UIBossOrderPanel : UIElement
{
    private static UIBossOrderPanel _active;
    internal static bool IsOpen => _active?.Parent != null;
    private List<ProgressionTier> _draft;
    private int _scroll;
    private int _dragFrom = -1, _dropAt = -1, _autoScrollTicks;
    private Vector2 _pressPoint;
    private bool _dragging, _suppressClick;
    private const int RowHeight = 30;
    private int VisibleRows => Math.Max(1, (int)((GetDimensions().Height - 65) / RowHeight));
    private Rectangle RowBounds(int index)
    {
        var d = GetDimensions();
        return new Rectangle((int)d.X + 5, (int)d.Y + 62 + (index - _scroll) * RowHeight, (int)d.Width - 10, 28);
    }
    private int RowAt(Vector2 point)
    {
        var d = GetDimensions();
        if (point.X < d.X + 5 || point.X >= d.X + d.Width - 5 || point.Y < d.Y + 62) return -1;
        int row = (int)((point.Y - d.Y - 62) / RowHeight);
        int index = _scroll + row;
        return row < VisibleRows && index < _draft.Count ? index : -1;
    }
    private static Rectangle UpBounds(Rectangle row) => new(row.Right - 118, row.Y + 1, 54, 26);
    private static Rectangle DownBounds(Rectangle row) => new(row.Right - 58, row.Y + 1, 54, 26);
    private void Move(int from, int to)
    {
        if (from < 0 || from >= _draft.Count || to < 0 || to >= _draft.Count || from == to) return;
        var tier = _draft[from]; _draft.RemoveAt(from); _draft.Insert(to, tier);
    }
    private void FinishDrag(Vector2 point)
    {
        if (_dragging)
        {
            _suppressClick = true;
            SetDropPosition(point);
            int to = _dropAt > _dragFrom ? _dropAt - 1 : _dropAt;
            Move(_dragFrom, Math.Clamp(to, 0, _draft.Count - 1));
        }
        _dragFrom = _dropAt = -1; _dragging = false;
    }
    private void SetDropPosition(Vector2 point)
    {
        var d = GetDimensions();
        _dropAt = Math.Clamp(_scroll + (int)Math.Floor((point.Y - d.Y - 62 + RowHeight / 2f) / RowHeight),
            _scroll, Math.Min(_draft.Count, _scroll + VisibleRows));
    }
    private Action _refresh;
    private MovableWindowState _owner;
    internal static bool Close()
    {
        var panel = _active; _active = null;
        if (panel == null) return false;
        panel._owner.AfterChildUpdate(panel.Remove); return true;
    }
    internal static void Show(MovableWindowState owner, UIElement host, Action refresh)
    {
        if (StorageSystem.Progression == null) return;
        Close(); UIChoiceMenu.Close();
        var panel = new UIBossOrderPanel { _owner = owner, _refresh = refresh,
            _draft = StorageSystem.Progression.Tiers.Where(t => t.Id != "start").ToList() };
        panel.Left.Set(8, 0); panel.Top.Set(35, 0); panel.Width.Set(-16, 1); panel.Height.Set(-53, 1);
        _active = panel;
        owner.AfterChildUpdate(() => { panel.Activate(); host.Append(panel); panel.Recalculate(); });
    }
    public override void OnInitialize()
    {
        AddButton("返回", 0, 70, () => Close());
        AddButton("恢复默认", 76, 100, () => { _draft = StorageSystem.Progression.DefaultOrder.Where(t => t.Id != "start").ToList(); _scroll = 0; });
        AddButton("保存", 182, 70, () =>
        {
            if (!BossOrderPreferences.Save(_draft.Select(t => t.Id))) return;
            StorageSystem.Progression.ApplyOrder(_draft.Select(t => t.Id));
            Close(); _refresh();
        });
        OnLeftMouseDown += (evt, _) =>
        {
            _suppressClick = false;
            int index = RowAt(evt.MousePosition);
            if (index < 0 || evt.MousePosition.X >= UpBounds(RowBounds(index)).X) return;
            _dragFrom = index; _pressPoint = evt.MousePosition; _dragging = false;
        };
        OnLeftMouseUp += (evt, _) => FinishDrag(evt.MousePosition);
        OnLeftClick += (evt, _) =>
        {
            if (_suppressClick) { _suppressClick = false; return; }
            int index = RowAt(evt.MousePosition);
            if (index < 0) return;
            var row = RowBounds(index);
            if (UpBounds(row).Contains(evt.MousePosition.ToPoint())) Move(index, index - 1);
            else if (DownBounds(row).Contains(evt.MousePosition.ToPoint())) Move(index, index + 1);
        };
    }
    private void AddButton(string label, float x, float width, Action action)
    {
        var button = new UIButton(label, .72f, false);
        button.Left.Set(x + 6, 0); button.Top.Set(5, 0); button.Width.Set(width, 0); button.Height.Set(26, 0);
        button.OnLeftClick += (_, _) => action(); Append(button);
    }
    public override void Update(GameTime time)
    {
        base.Update(time);
        if (_dragFrom >= 0)
        {
            if (Vector2.DistanceSquared(Main.MouseScreen, _pressPoint) > 16) _dragging = true;
            if (_dragging)
            {
                var d = GetDimensions();
                if (++_autoScrollTicks >= 8)
                {
                    _autoScrollTicks = 0;
                    int direction = Main.mouseY < d.Y + 76 ? -1 : Main.mouseY > d.Y + 62 + VisibleRows * RowHeight - 15 ? 1 : 0;
                    _scroll = Math.Clamp(_scroll + direction, 0, Math.Max(0, _draft.Count - VisibleRows));
                }
                SetDropPosition(Main.MouseScreen);
            }
            Main.LocalPlayer.mouseInterface = true;
            if (!Main.mouseLeft) FinishDrag(Main.MouseScreen);
        }
        if (!ContainsPoint(Main.MouseScreen)) return;
        int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
        _scroll = Math.Clamp(_scroll - wheel * 3, 0, Math.Max(0, _draft.Count - VisibleRows));
        Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
        Main.LocalPlayer.mouseInterface = true;
    }

    protected override void DrawSelf(SpriteBatch batch)
    {
        var d = GetDimensions(); UITheme.Fill(batch, d.ToRectangle(), UITheme.Panel);
        UITheme.Outline(batch, d.ToRectangle(), UITheme.BorderHot);
        Utils.DrawBorderString(batch, "拖动条目排序 · 上调提前／下调延后 · 保存后生效", new Vector2(d.X + 8, d.Y + 39), UITheme.Muted, .65f);
        int rows = Math.Max(0, (int)((d.Height - 65) / 30));
        for (int i = _scroll; i < Math.Min(_draft.Count, _scroll + rows); i++)
        {
            var row = RowBounds(i);
            if (row.Contains(Main.MouseScreen.ToPoint()) || (_dragging && i == _dragFrom)) UITheme.Fill(batch, row, UITheme.BtnHot);
            var tier = _draft[i];
            if (tier.NpcIds.Count > 0) NpcVisuals.Draw(batch, tier.NpcIds[0], new Rectangle(row.X + 2, row.Y + 1, 26, 26));
            string label = (i + 1) + ". " + tier.DisplayName;
            while (label.Length > 1 && FontAssets.MouseText.Value.MeasureString(label).X * .7f > row.Width - 158) label = label[..^1];
            Utils.DrawBorderString(batch, label, new Vector2(row.X + 32, row.Y + 6), UITheme.Text, .7f);
            DrawRankButton(batch, UpBounds(row), "↑ 上调", i > 0);
            DrawRankButton(batch, DownBounds(row), "↓ 下调", i < _draft.Count - 1);
        }
        if (_dragging && _dropAt >= 0)
        {
            int y = (int)d.Y + 62 + (_dropAt - _scroll) * RowHeight;
            UITheme.Fill(batch, new Rectangle((int)d.X + 6, y - 2, (int)d.Width - 12, 3), UITheme.BorderHot);
        }
    }
    private static void DrawRankButton(SpriteBatch batch, Rectangle bounds, string text, bool enabled)
    {
        UITheme.Fill(batch, bounds, enabled && bounds.Contains(Main.MouseScreen.ToPoint()) ? UITheme.BtnHot : UITheme.Field);
        UITheme.Outline(batch, bounds, enabled ? UITheme.BorderHot : UITheme.Muted);
        Utils.DrawBorderString(batch, text, new Vector2(bounds.X + 4, bounds.Y + 6), enabled ? UITheme.Text : UITheme.Muted, .6f);
    }
}
