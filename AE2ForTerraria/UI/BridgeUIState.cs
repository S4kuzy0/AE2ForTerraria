using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;

namespace AE2ForTerraria.UI;

/// <summary>Channel management uses current-network permission; recovery only disconnects the original local bridge.</summary>
public sealed class BridgeUIState : MovableWindowState
{
    private static readonly WindowGeometry Geometry = WindowGeometry.Compact();
    private UIText _channel, _network, _original, _feedback;
    private UIButton _inspect, _cycle, _disconnect;
    private bool _enabled, _manage, _recover, _waiting;
    private int _channelValue;
    public Point16 Position { get; private set; } = new(-1, -1);
    protected override bool CornerResizeOnly => true;
    protected override bool HighlightFrame => false;

    public override void OnInitialize()
    {
        InitWindow(Geometry, "ME 无线桥");
        _channel = Text(); _network = Text(); _original = Text(); _feedback = Text(.72f);
        _inspect = new UIButton("检查", .75f);
        _cycle = new UIButton("切换频道", .75f);
        _disconnect = new UIButton("断开此无线桥", .75f);
        _inspect.OnLeftClick += (_, _) => Refresh();
        _cycle.OnLeftClick += (_, _) => ChangeChannel();
        _disconnect.OnLeftClick += (_, _) => Disconnect();
        Window.Append(_inspect); Window.Append(_cycle); Window.Append(_disconnect);
        RequestLayout(); UpdateLabels();
    }

    private UIText Text(float scale = .8f)
    {
        var text = new UIText(string.Empty, scale) { DynamicallyScaleDownToWidth = true };
        Window.Append(text); AddDragTarget(text); return text;
    }

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        if (_channel == null) return;
        var rows = new[] { _channel, _network, _original, _feedback };
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].Left.Set(Pad, 0); rows[i].Top.Set(56 + i * 36, 0);
            rows[i].Width.Set(Geo.W - Pad * 2, 0); rows[i].Height.Set(i == 3 ? 45 : 26, 0);
        }
        float width = (Geo.W - Pad * 2 - 16) / 3;
        var buttons = new[] { _inspect, _cycle, _disconnect };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Left.Set(Pad + i * (width + 8), 0); buttons[i].Top.Set(Geo.H - 48, 0);
            buttons[i].Width.Set(width, 0); buttons[i].Height.Set(30, 0);
        }
        Window.Recalculate();
    }

    public void SetBridge(MEWirelessBridgeEntity bridge)
    {
        Position = bridge?.Position ?? new Point16(-1, -1);
        _enabled = bridge?.Enabled ?? false; _channelValue = bridge?.Channel ?? 0;
        _manage = _recover = false; Refresh();
    }

    public void Apply(Point16 position, int channel, bool enabled, bool manage, bool original)
    {
        if (position != Position) return;
        _channelValue = channel; _enabled = enabled; _manage = manage; _recover = original; _waiting = false;
        UpdateLabels();
    }

    public void Refresh()
    {
        if (Position.X < 0) return;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            _waiting = true; _manage = _recover = false;
            MENet.RequestBridge(Position); UpdateLabels(); return;
        }
        if (!TryGetLocal(out var bridge)) { _waiting = false; _manage = _recover = false; UpdateLabels(); return; }
        Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(Position.X, Position.Y));
        Apply(Position, bridge.Channel, bridge.Enabled,
            NetworkRegistry.GetLevel(anchor, Main.LocalPlayer.name) >= NetworkRegistry.LevelManage,
            NetworkRegistry.CanManageOriginalBlock(Position, Main.LocalPlayer.name));
    }

    private bool TryGetLocal(out MEWirelessBridgeEntity bridge)
    {
        bridge = null;
        if (!TileEntity.ByPosition.TryGetValue(Position, out var entity) || entity is not MEWirelessBridgeEntity found
            || !found.IsTileValidForEntity(Position.X, Position.Y)) return false;
        bridge = found; return true;
    }

    private void ChangeChannel()
    {
        if (_waiting || !_manage || !BridgeRecovery.InRange(Position, Main.LocalPlayer)) return;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            _waiting = true; MENet.CycleBridgeChannel(Position); MENet.RequestBridge(Position); UpdateLabels(); return;
        }
        if (TryGetLocal(out var bridge))
        {
            Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(Position.X, Position.Y));
            if (NetworkRegistry.GetLevel(anchor, Main.LocalPlayer.name) >= NetworkRegistry.LevelManage) bridge.CycleChannel();
        }
        Refresh();
    }

    private void Disconnect()
    {
        if (_waiting || !_enabled || !_recover || !BridgeRecovery.InRange(Position, Main.LocalPlayer)) return;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        { _waiting = true; MENet.RequestBridge(Position, disconnect: true); UpdateLabels(); return; }
        if (BridgeRecovery.TryDisconnect(Position, Main.LocalPlayer)) UIStatus.Toast("此无线桥已断开，网络恢复独立");
        Refresh();
    }

    protected override void UpdateWindow(GameTime gameTime) => UpdateLabels();

    private void UpdateLabels()
    {
        if (_channel == null) return;
        bool near = Position.X >= 0 && BridgeRecovery.InRange(Position, Main.LocalPlayer);
        UITextCache.Set(_channel, "当前频道    " + _channelValue + (_enabled ? " · 已连接" : " · 已断开"));
        UITextCache.Set(_network, "网络管理    " + (_waiting ? "检查中…" : _manage ? "可以管理" : "无管理权限"));
        UITextCache.Set(_original, "本地无线桥    " + (_waiting ? "检查中…" : _recover ? "你可以管理" : "无原网络管理权限"));
        string status = !near ? "靠近此无线桥后操作" : _waiting ? "等待服务器检查权限"
            : !_enabled ? "此无线桥已断开，网络恢复独立" : !_manage && _recover ? "无法管理合并网络；可断开自己的无线桥"
            : !_manage ? "需要网络管理权限" : "同频道的已启用无线桥会连接网络";
        UITextCache.Set(_feedback, status);
        _feedback.TextColor = !near || (!_waiting && !_manage && _enabled) ? UITheme.Warn : UITheme.TextDim;
        _inspect.Enabled = near;
        _cycle.Enabled = near && !_waiting && _manage;
        _disconnect.Enabled = near && !_waiting && _recover && _enabled;
        _cycle.SetText(_enabled ? "切换频道" : "换频道并连接");
        _disconnect.SetText(_enabled ? "断开此无线桥" : "已断开");
    }
}
