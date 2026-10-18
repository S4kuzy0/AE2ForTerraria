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
        OnLeftClick += (evt, _) =>
        {
            var d = GetDimensions(); float y = evt.MousePosition.Y - d.Y - 62;
            int visible = Math.Max(0, (int)((d.Height - 65) / 30));
            if (y < 0 || y >= visible * 30) return;
            int index = _scroll + (int)(y / 30);
            if (index >= _draft.Count) return;
            float x = evt.MousePosition.X - d.X;
            int other = x >= d.Width - 34 ? index + 1 : x >= d.Width - 70 ? index - 1 : -1;
            if (other >= 0 && other < _draft.Count) (_draft[index], _draft[other]) = (_draft[other], _draft[index]);
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
        if (!ContainsPoint(Main.MouseScreen)) return;
        int rows = Math.Max(1, (int)((GetDimensions().Height - 65) / 30));
        int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
        _scroll = Math.Clamp(_scroll - wheel * 3, 0, Math.Max(0, _draft.Count - rows));
        Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
        Main.LocalPlayer.mouseInterface = true;
    }
    protected override void DrawSelf(SpriteBatch batch)
    {
        var d = GetDimensions(); UITheme.Fill(batch, d.ToRectangle(), UITheme.Panel);
        UITheme.Outline(batch, d.ToRectangle(), UITheme.BorderHot);
        Utils.DrawBorderString(batch, "Boss 顺序 · 仅影响查询排序（滚轮浏览）", new Vector2(d.X + 8, d.Y + 39), UITheme.Muted, .65f);
        int rows = Math.Max(0, (int)((d.Height - 65) / 30));
        for (int i = _scroll; i < Math.Min(_draft.Count, _scroll + rows); i++)
        {
            var row = new Rectangle((int)d.X + 5, (int)d.Y + 62 + (i - _scroll) * 30, (int)d.Width - 10, 28);
            if (row.Contains(Main.MouseScreen.ToPoint())) UITheme.Fill(batch, row, UITheme.BtnHot);
            var tier = _draft[i];
            if (tier.NpcIds.Count > 0) NpcVisuals.Draw(batch, tier.NpcIds[0], new Rectangle(row.X + 2, row.Y + 1, 26, 26));
            string label = (i + 1) + ". " + tier.DisplayName;
            while (label.Length > 1 && FontAssets.MouseText.Value.MeasureString(label).X * .7f > row.Width - 106) label = label[..^1];
            Utils.DrawBorderString(batch, label, new Vector2(row.X + 32, row.Y + 6), UITheme.Text, .7f);
            Utils.DrawBorderString(batch, "↑    ↓", new Vector2(row.Right - 61, row.Y + 6), UITheme.Text, .75f);
        }
    }
}
