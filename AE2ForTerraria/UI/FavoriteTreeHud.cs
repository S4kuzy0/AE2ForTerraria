using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Personal pinned recipe trees, drawn behind AE windows with their own bounded pointer area.</summary>
internal static class FavoriteTreeHud
{
    private static readonly List<BookmarkTree> Trees = new();
    private static readonly Dictionary<int, long> Owned = new();
    private static readonly Dictionary<int, long> NextOwned = new();
    private static Frame _panel;
    private static UIElement _body;
    private static int _revision = -1, _scroll, _horizontal, _totalRows;
    private static int _buildCursor, _pendingTrees;
    private static bool _wasDown, _dragging, _hadTerminal;
    private static Vector2? _position;
    private static Vector2 _dragOffset;
    private static string _sourceLabel = "背包";
    private static int _readRevision;
    private static Rectangle _eye;
    private static ulong _stockAt;
    private static object _stockSource;
    internal static bool Visible => !Main.dedServ && !Main.gameMenu && !Main.LocalPlayer.dead
        && (Main.playerInventory || BrowserFavorites.AlwaysVisible) && BrowserFavorites.Entries.Count > 0;
    internal static Rectangle Bounds => Visible && _panel != null ? _panel.GetDimensions().ToRectangle() : Rectangle.Empty;
    internal static bool Contains(Vector2 point) => _dragging || Bounds.Contains(point.ToPoint());
    private static bool MayInteract => !AEUiSession.CoversWindowPoint(AEUiSession.PointerPosition);
    private static int VisibleRows => Math.Max(1, ((int)(_body?.GetDimensions().Height ?? 22)) / 22);
    internal static void Update(GameTime time)
    {
        if (!Visible)
        {
            if (_dragging) { _dragging = false; if (_position.HasValue) BrowserFavorites.Move(_position.Value); }
            _wasDown = Main.mouseLeft; return;
        }
        EnsurePanel();
        bool stockChanged = RefreshStock();
        if (_revision != BrowserFavorites.Revision || stockChanged)
        {
            if (_revision != BrowserFavorites.Revision) _scroll = _horizontal = 0;
            _revision = BrowserFavorites.Revision; Trees.Clear();
            var stock = new RecipeTreeStock(Owned);
            foreach (var bookmark in BrowserFavorites.Entries) Trees.Add(new BookmarkTree(bookmark, stock));
            _buildCursor = 0; _pendingTrees = Trees.Count(tree => !tree.Complete);
            _totalRows = Trees.Sum(tree => Math.Max(1, tree.Nodes.Count));
        }
        long started = Stopwatch.GetTimestamp();
        while (_pendingTrees > 0 && _buildCursor < Trees.Count && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2)
        {
            var tree = Trees[_buildCursor];
            if (tree.Complete) { _buildCursor++; continue; }
            int before = Math.Max(1, tree.Nodes.Count); tree.Advance(32);
            _totalRows += Math.Max(1, tree.Nodes.Count) - before;
            if (tree.Complete) { _pendingTrees--; _buildCursor++; }
        }
        float width = Math.Min(300, Math.Max(200, MovableWindowState.ViewportSize.X * .31f));
        float height = Math.Min(Math.Max(76, _totalRows * 22 + 32), Math.Max(90, MovableWindowState.ViewportSize.Y * .6f));
        _horizontal = Math.Clamp(_horizontal, 0, Math.Max(0, Trees.Max(tree => tree.MaxDepth) * 12 - (int)width / 2));
        Vector2 pointer = AEUiSession.PointerPosition;
        if (_dragging && Main.mouseLeft) _position = pointer - _dragOffset;
        var position = _position ?? BrowserFavorites.Position ?? new Vector2(MovableWindowState.ViewportSize.X - width - 12, 100);
        position = Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, MovableWindowState.ViewportSize - new Vector2(width, height)));
        if (_position.HasValue) _position = position;
        _panel.Left.Set(position.X, 0); _panel.Top.Set(position.Y, 0);
        _panel.Width.Set(width, 0); _panel.Height.Set(height, 0);
        _body.Left.Set(5, 0); _body.Top.Set(28, 0); _body.Width.Set(width - 10, 0); _body.Height.Set(height - 33, 0);
        _panel.Recalculate();
        var bounds = _panel.GetDimensions().ToRectangle();
        _eye = new Rectangle(bounds.Right - 27, bounds.Y + 4, 22, 20);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _totalRows - VisibleRows));
        if (_dragging && !Main.mouseLeft)
        {
            _dragging = false; BrowserFavorites.Move(position);
        }
        if (_dragging) Main.LocalPlayer.mouseInterface = true;
        if (MayInteract && Contains(pointer) && !_dragging)
        {
            int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
            if (Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift))
                _horizontal = Math.Clamp(_horizontal - wheel * 24, 0, Math.Max(0, Trees.Max(tree => tree.MaxDepth) * 12 - (int)width / 2));
            else _scroll = Math.Clamp(_scroll - wheel * 3, 0, Math.Max(0, _totalRows - VisibleRows));
            Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
            Main.LocalPlayer.mouseInterface = true;
            if (Main.mouseLeft && !_wasDown)
            {
                if (_eye.Contains(pointer.ToPoint())) BrowserFavorites.AlwaysVisible = !BrowserFavorites.AlwaysVisible;
                else if (pointer.Y < bounds.Top + 27)
                { _dragging = true; _position = position; _dragOffset = pointer - position; }
                else if (_body.ContainsPoint(pointer)) ActivateRow((int)((pointer.Y - _body.GetDimensions().Y) / 22) + _scroll);
            }
        }
        _wasDown = Main.mouseLeft;
    }
    private static bool RefreshStock()
    {
        bool open = PinnedInventory.HasOpenTerminal;
        object source = open ? UISystem.ActiveNetworkContents : Main.LocalPlayer.inventory;
        if (_stockAt != 0 && Main.GameUpdateCount - _stockAt < 15 && _hadTerminal == open
            && ReferenceEquals(source, _stockSource) && _readRevision == PinnedInventory.Revision) return false;
        _stockAt = Main.GameUpdateCount; _stockSource = source; _hadTerminal = open;
        var snapshot = PinnedInventory.Snapshot(); _readRevision = PinnedInventory.Revision;
        _sourceLabel = PinnedInventory.Status; NextOwned.Clear();
        void Add(int type, long amount) { NextOwned.TryGetValue(type, out long count); NextOwned[type] = StorageAmounts.Add(count, amount); }
        if (snapshot != null) foreach (var entry in snapshot) Add(entry.Key.Type, entry.Value);
        else foreach (var item in Main.LocalPlayer.inventory) if (item != null && !item.IsAir) Add(item.type, item.stack);
        if (Owned.Count == NextOwned.Count && Owned.All(entry => NextOwned.TryGetValue(entry.Key, out long amount) && amount == entry.Value)) return false;
        Owned.Clear(); foreach (var entry in NextOwned) Owned.Add(entry.Key, entry.Value);
        return true;
    }
    private static void EnsurePanel()
    {
        if (_panel != null) return;
        _panel = new Frame(); _body = new UIElement { OverflowHidden = true };
        var canvas = new Canvas { IgnoresMouseInteraction = true };
        canvas.Width.Set(0, 1); canvas.Height.Set(0, 1); _body.Append(canvas); _panel.Append(_body);
    }
    private static (BookmarkTree Tree, int Index, int Start) Row(int index)
    {
        int start = 0;
        foreach (var tree in Trees)
        {
            int count = Math.Max(1, tree.Nodes.Count);
            if (index < start + count) return (tree, index - start, start);
            start += count;
        }
        return (null, 0, 0);
    }
    private static void ActivateRow(int row)
    {
        var (tree, index, start) = Row(row);
        if (tree == null) return;
        if (index >= tree.Nodes.Count)
        {
            if (FavoriteInput.IsDown && tree.MissingContent != null && BrowserFavorites.Remove(tree.MissingContent))
                UIStatus.Toast("已取消收藏");
            return;
        }
        var node = tree.Nodes[index];
        if (FavoriteInput.IsDown) { FavoriteInput.Toggle(node.Npc > 0 ? node.Npc : node.Item, node.Npc > 0); return; }
        if (node.Reference >= 0) { _scroll = Math.Clamp(start + node.Reference, 0, Math.Max(0, _totalRows - VisibleRows)); return; }
        if (node.Npc > 0) UISystem.OpenNpc(node.Npc);
        else UISystem.OpenRecipe(node.Item, false);
    }
    internal static void Draw(SpriteBatch batch) { if (Visible && _panel != null) _panel.Draw(batch); }
    internal static void Reset()
    {
        Trees.Clear(); Owned.Clear(); NextOwned.Clear(); _panel = null; _body = null; _revision = -1;
        _scroll = _horizontal = _totalRows = _buildCursor = _pendingTrees = 0; _wasDown = false; _stockAt = 0; _stockSource = null;
        _dragging = _hadTerminal = false; _position = null; _sourceLabel = "背包"; _readRevision = -1; PinnedInventory.Reset();
    }
    private sealed class Frame : UIElement
    {
        protected override void DrawSelf(SpriteBatch batch)
        {
            var box = GetDimensions().ToRectangle();
            UITheme.Fill(batch, box, new Color(22, 30, 49) * .94f); UITheme.Outline(batch, box, UITheme.ItemBorder);
            Utils.DrawBorderString(batch, "收藏合成树 · " + _sourceLabel
                + (_pendingTrees > 0 ? " · 展开中" : ""), new Vector2(box.X + 7, box.Y + 7), UITheme.Text, .65f);
            Color eyeColor = BrowserFavorites.AlwaysVisible ? Main.OurFavoriteColor : UITheme.TextDim;
            int cx = _eye.Center.X, cy = _eye.Center.Y;
            for (int x = -8; x <= 8; x++)
            {
                int y = (int)Math.Round(4 * (1 - Math.Abs(x) / 8f));
                UITheme.Fill(batch, new Rectangle(cx + x, cy - y, 1, 1), eyeColor);
                UITheme.Fill(batch, new Rectangle(cx + x, cy + y, 1, 1), eyeColor);
            }
            UITheme.Fill(batch, new Rectangle(cx - 2, cy - 2, 4, 4), eyeColor);
            if (MayInteract && _eye.Contains(AEUiSession.PointerPosition.ToPoint()))
                Main.hoverItemName = BrowserFavorites.AlwaysVisible ? "始终显示；点击改为仅打开背包时显示" : "仅打开背包时显示；点击改为始终显示";
            if (_totalRows > VisibleRows)
            {
                int height = Math.Max(10, (box.Height - 34) * VisibleRows / _totalRows);
                UITheme.Fill(batch, new Rectangle(box.Right - 3, box.Y + 29 + (box.Height - 34 - height) * _scroll / Math.Max(1, _totalRows - VisibleRows), 2, height), UITheme.Muted);
            }
        }
    }
    private sealed class Canvas : UIElement
    {
        protected override void DrawSelf(SpriteBatch batch)
        {
            var area = GetDimensions();
            for (int row = 0; row < VisibleRows + 1 && _scroll + row < _totalRows; row++)
            {
                var (tree, index, _) = Row(_scroll + row);
                int y = (int)area.Y + row * 22;
                if (tree == null) continue;
                if (index >= tree.Nodes.Count)
                {
                    Utils.DrawBorderString(batch, tree.MissingContent == null ? "正在展开…" : "未加载：" + tree.MissingContent, new Vector2(area.X + 2, y + 3), UITheme.Muted, .6f);
                    if (MayInteract && _body.ContainsPoint(AEUiSession.PointerPosition)
                        && new Rectangle((int)area.X, y, (int)area.Width, 22).Contains(AEUiSession.PointerPosition.ToPoint()))
                    {
                        FavoriteInput.Cursor(); Main.HoverItem = new Item();
                        Main.hoverItemName = tree.MissingContent + "\n内容未加载；Alt＋左键可取消收藏";
                    }
                    continue;
                }
                var node = tree.Nodes[index];
                int x = (int)area.X + 3 + node.Depth * 12 - _horizontal;
                if (node.Depth > 0)
                {
                    UITheme.Fill(batch, new Rectangle(x - 5, y - 1, 1, node.Last ? 12 : 23), UITheme.Muted);
                    UITheme.Fill(batch, new Rectangle(x - 5, y + 10, 7, 1), UITheme.Muted);
                    for (int parent = node.Parent; parent >= 0; parent = tree.Nodes[parent].Parent)
                    {
                        var ancestor = tree.Nodes[parent];
                        if (ancestor.Depth > 0 && !ancestor.Last)
                            UITheme.Fill(batch, new Rectangle((int)area.X + ancestor.Depth * 12 - _horizontal - 2, y - 1, 1, 23), UITheme.Muted);
                    }
                }
                var icon = new Rectangle(x + 2, y, 21, 21);
                UIBrowserDetails.DrawIcon(batch, node.Item, node.Npc, icon);
                if (BrowserFavorites.Contains(node.Npc > 0 ? node.Npc : node.Item, node.Npc > 0)) FavoriteInput.Mark(batch, icon);
                string name = node.Group ?? (node.Npc > 0 ? NpcIndex.GetName(node.Npc) : Lang.GetItemNameValue(node.Item));
                string suffix = node.Npc > 0 ? "" : node.Parent < 0 && tree.MaterialsReady ? " · 材料已齐"
                    : " ×" + UIFormat.Count(node.Amount) + " 缺" + UIFormat.Count(node.Missing);
                if (node.Reference >= 0) suffix += "（重复分支，点击定位）";
                if (node.Overflow) suffix = "（数量溢出）";
                string label = name + suffix;
                float width = Math.Max(0, area.X + area.Width - x - 28);
                while (label.Length > 1 && FontAssets.MouseText.Value.MeasureString(label).X * .55f > width) label = label[..^1];
                Utils.DrawBorderString(batch, label, new Vector2(x + 26, y + 4), node.Parent < 0 ? Main.OurFavoriteColor : UITheme.TextDim, .55f);
                if (MayInteract && _body.ContainsPoint(AEUiSession.PointerPosition) && new Rectangle((int)area.X, y, (int)area.Width, 22).Contains(AEUiSession.PointerPosition.ToPoint()))
                {
                    FavoriteInput.Cursor();
                    if (node.Npc > 0) { Main.HoverItem = new Item(); Main.hoverItemName = NpcIndex.GetName(node.Npc); }
                    else BrowserHover.Item(ItemKey.Crafted(node.Item));
                }
            }
        }
    }
}
