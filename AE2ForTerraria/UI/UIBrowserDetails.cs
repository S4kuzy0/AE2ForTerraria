using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Recipe reader over the existing catalogue; closing restores its unchanged list and scroll.</summary>
internal sealed class UIBrowserDetails : UIElement
{
    private sealed record Row(string Text, int Item = 0, int Npc = 0, Color? Color = null, int Indent = 0, Action Click = null);
    private readonly HashSet<DropRecord> _expandedConditions = new();
    private readonly List<Row> _rows = new();
    private readonly List<(Rectangle Bounds, Row Row)> _hits = new();
    private readonly List<(Row Row, string Text, bool First)> _wrapped = new();
    private float _wrappedWidth = -1;
    private readonly record struct Page(ItemKey Key, int Npc, int Tab, int Recipe, int Scroll, int Stations, long Quantity);
    private readonly Stack<Page> _history = new();
    private readonly UIButton[] _tabs;
    private readonly UIButton _back, _previous, _next, _forward;
    private readonly Stack<Page> _future = new();
    public Action FavoriteChanged;
    public Action Closed;
    private readonly UIElement _host;
    private readonly UIRecipePanel _plan;
    private ItemKey _key = ItemKey.None;
    private int _npc, _tab, _recipe, _recipeCount, _scroll;
    private float _x, _y, _width, _height, _entrance = 1;
    private string _title = "", _subtitle = "";
    private long _quantity = 1;
    private sealed record IngredientCard(int Index, int Type, long Required, long Owned, RecipeGroup Group, bool Overflow);
    private readonly List<IngredientCard> _ingredients = new();
    private readonly Dictionary<int, int> _alternativeSelection = new();
    private readonly List<string> _stationText = new();
    private readonly List<(Rectangle Bounds, IngredientCard Card, bool Alternatives)> _ingredientHits = new();
    internal readonly record struct IngredientVisual(int Index, Rectangle Card, Rectangle Icon,
        Rectangle Name, Rectangle Count, Rectangle Alternatives);
    internal readonly List<IngredientVisual> IngredientVisuals = new();
    private readonly Dictionary<int, long> _owned = new();
    private Recipe _shownRecipe;
    private BookmarkTree _tree;
    private int _treeRevision = -1;
    private AlternativePopup _alternatives;
    private int _stationScroll;
    private Rectangle _stationBounds;
    private readonly List<string> _stationLines = new();
    private float _stationLineWidth = -1;
    private int ContentTop => 76;
    public Action<ItemKey> Selected;
    public bool IsOpen { get; private set; }
    public float BottomReserve;
    public ItemKey SelectedKey => _key;
    public long Quantity => _quantity;
    private int ReaderScroll => _plan?.Parent == this ? _plan.ScrollOffset : _scroll;
    public bool BlocksPointer => IsOpen && ContainsPoint(Main.MouseScreen);
    public UIBrowserDetails(UIElement host, bool terminal = false)
    {
        _host = host;
        _back = Button("返回", Back);
        _forward = Button("前进", Forward);
        _previous = Button("上一配方", () => CycleRecipe(-1));
        _next = Button("下一配方", () => CycleRecipe(1));
        _tabs = new UIButton[4];
        string[] labels = { "配方", "来源", "用途", "合成树" };
        for (int i = 0; i < 4; i++) { int tab = i; _tabs[i] = Button(labels[i], () => SetTab(tab)); }
        if (terminal) _plan = new UIRecipePanel();
        OnLeftClick += (e, _) =>
        {
            if (e.Target != this) return;
            if (FavoriteInput.IsDown)
            {
                foreach (var hit in _ingredientHits)
                    if (hit.Bounds.Contains(e.MousePosition.ToPoint())) { FavoriteInput.Toggle(hit.Card.Type); FavoriteChanged?.Invoke(); return; }
                foreach (var hit in _hits)
                    if (hit.Bounds.Contains(e.MousePosition.ToPoint()) && (hit.Row.Item > 0 || hit.Row.Npc > 0))
                    {
                        int recipe = hit.Row.Item == _key.Type || hit.Row.Item == _shownRecipe?.createItem.type ? CurrentRecipeIndex : -1;
                        FavoriteInput.Toggle(hit.Row.Npc > 0 ? hit.Row.Npc : hit.Row.Item, hit.Row.Npc > 0, recipe, _quantity);
                        FavoriteChanged?.Invoke(); return;
                    }
            }
            foreach (var hit in _ingredientHits)
                if (hit.Bounds.Contains(e.MousePosition.ToPoint()))
                {
                    if (hit.Alternatives) ShowAlternatives(hit.Card);
                    else Show(ItemKey.Crafted(hit.Card.Type));
                    return;
                }
            foreach (var hit in _hits)
                if (hit.Bounds.Contains(e.MousePosition.ToPoint()))
                {
                    if (hit.Row.Click != null) hit.Row.Click();
                    else if (hit.Row.Npc > 0) ShowNpc(hit.Row.Npc);
                    else if (hit.Row.Item > 0) Show(ItemKey.Crafted(hit.Row.Item));
                    return;
                }
        };
    }
    private UIButton Button(string label, Action action)
    {
        var b = new UIButton(label, .75f); b.OnLeftClick += (_, _) => action(); Append(b); return b;
    }
    private void Open()
    {
        if (IsOpen) return;
        // Terminal action rows use the full reader width immediately, including on the first click.
        IsOpen = true; _entrance = _plan == null ? 0 : 1; _host.Append(this); Arrange();
    }
    public void Close()
    {
        if (IsOpen) BrowserHistory.Record(CurrentVisit);
        IsOpen = false; _alternatives?.Remove(); _alternatives = null; Remove();
        _history.Clear(); _future.Clear(); _key = ItemKey.None; _npc = 0;
        _hits.Clear(); _ingredientHits.Clear(); IngredientVisuals.Clear(); Closed?.Invoke();
    }
    public void Show(ItemKey key, long quantity = 1, bool uses = false, bool remember = true, bool preserveTab = false)
    {
        if (key.IsAir) return;
        if (remember && (!_key.Equals(key) || _npc > 0 || !preserveTab && _tab != (uses ? 2 : 0))) Remember();
        bool changed = !_key.Equals(key) || _npc > 0;
        _key = key; _npc = 0; _quantity = quantity;
        int previousScroll = ReaderScroll, previousStations = _stationScroll;
        if (changed) { _recipe = 0; _alternativeSelection.Clear(); }
        if (!preserveTab) _tab = uses ? 2 : 0;
        Open(); Rebuild();
        if (!changed && preserveTab) { _scroll = previousScroll; if (_plan?.Parent == this) _plan.ScrollOffset = previousScroll; _stationScroll = previousStations; }
        Selected?.Invoke(key);
        if (remember) BrowserHistory.Record(CurrentVisit);
    }
    public void ShowNpc(int type, bool remember = true)
    {
        if (remember && (_npc != type || !_key.IsAir)) Remember();
        _npc = type; _key = ItemKey.None; _tab = 1; Open(); Rebuild(); Selected?.Invoke(ItemKey.None);
        if (remember) BrowserHistory.Record(CurrentVisit);
    }
    private Page CurrentPage => new(_key, _npc, _tab, _recipe, ReaderScroll, _stationScroll, _quantity);
    private int CurrentRecipeIndex
    {
        get
        {
            if (_key.IsAir) return -1;
            var recipes = _tab == 2 ? StorageSystem.Recipes.GetRecipesUsing(_key.Type) : StorageSystem.Recipes.GetRecipesFor(_key);
            return recipes.Count == 0 ? -1 : recipes[Math.Clamp(_recipe, 0, recipes.Count - 1)];
        }
    }
    private BrowserVisit CurrentVisit => new(BrowserContent.Name(_npc > 0 ? _npc : _key.Type, _npc > 0),
        _key.Prefix, _key.Data, _tab, CurrentRecipeIndex < 0 ? "" : BrowserContent.RecipeKey(Main.recipe[CurrentRecipeIndex]),
        ReaderScroll, _stationScroll, Math.Max(1, _quantity));
    public void ShowVisit(BrowserVisit visit)
    {
        if (!visit.TryResolve(out var key, out int npc)) { UIStatus.Toast("这条浏览记录的内容当前未加载"); return; }
        Remember();
        if (npc > 0) ShowNpc(npc, false);
        else Show(key, visit.Quantity, remember: false);
        _tab = Math.Clamp(visit.Tab, 0, 3);
        var recipes = npc > 0 ? Array.Empty<int>() : (_tab == 2 ? StorageSystem.Recipes.GetRecipesUsing(key.Type) : StorageSystem.Recipes.GetRecipesFor(key)).ToArray();
        _recipe = Math.Max(0, Array.FindIndex(recipes, i => BrowserContent.RecipeKey(Main.recipe[i]) == visit.Recipe));
        Rebuild(); _scroll = Math.Max(0, visit.Scroll); _stationScroll = Math.Max(0, visit.Stations);
        if (_plan?.Parent == this) _plan.ScrollOffset = _scroll;
        BrowserHistory.Record(CurrentVisit);
    }
    private static void Push(Stack<Page> stack, Page page)
    {
        if (stack.Count >= BrowserHistory.Capacity)
        {
            var recent = stack.Take(BrowserHistory.Capacity - 1).Reverse().ToArray();
            stack.Clear(); foreach (var entry in recent) stack.Push(entry);
        }
        stack.Push(page);
    }
    private void Remember()
    {
        if (!IsOpen || _key.IsAir && _npc <= 0) return;
        BrowserHistory.Record(CurrentVisit);
        Push(_history, CurrentPage); _future.Clear();
    }
    private void Restore(Page page)
    {
        _tab = page.Tab;
        if (page.Npc > 0) ShowNpc(page.Npc, false);
        else Show(page.Key, page.Quantity, false, false, preserveTab: true);
        _tab = page.Tab; _recipe = page.Recipe; _quantity = page.Quantity;
        Rebuild(); _scroll = page.Scroll; if (_plan?.Parent == this) _plan.ScrollOffset = page.Scroll;
        _stationScroll = page.Stations;
        BrowserHistory.Record(CurrentVisit);
    }
    private void Back()
    {
        if (_history.Count == 0) { Close(); return; }
        Push(_future, CurrentPage); Restore(_history.Pop());
    }
    private void Forward()
    {
        if (_future.Count == 0) return;
        Push(_history, CurrentPage); Restore(_future.Pop());
    }
    public void SetTab(int tab)
    {
        if (_npc > 0 || tab < 0 || tab >= _tabs.Length || tab == _tab) return;
        Remember(); _tab = tab; _recipe = 0; Rebuild(); BrowserHistory.Record(CurrentVisit);
    }
    private void CycleRecipe(int delta)
    {
        if (_recipeCount <= 1) return;
        Remember(); _recipe = (_recipe + delta + _recipeCount) % _recipeCount; _alternativeSelection.Clear(); Rebuild(); BrowserHistory.Record(CurrentVisit);
    }
    public void SetBounds(float x, float y, float width, float height)
    {
        _x = x; _y = y; _width = width; _height = height; Arrange();
    }
    private static void Place(UIElement e, float x, float y, float w, float h)
    { e.Left.Set(x, 0); e.Top.Set(y, 0); e.Width.Set(w, 0); e.Height.Set(h, 0); }
    private void Arrange()
    {
        float offset = 16 * (1 - _entrance) * (1 - _entrance);
        Place(this, _x + offset, _y, Math.Max(100, _width - offset), _height);
        Place(_back, 8, 4, 38, 22); Place(_forward, 50, 4, 38, 22);
        float tabWidth = Math.Min(90, (_width - offset - 104) / 4);
        for (int i = 0; i < 4; i++) Place(_tabs[i], 94 + i * (tabWidth + 2), 4, tabWidth, 22);
        Place(_previous, _width - offset - 116, 32, 52, 22);
        Place(_next, _width - offset - 60, 32, 52, 22);
        if (_plan != null) Place(_plan, 8, ContentTop, _width - offset - 16, Math.Max(40, _height - ContentTop - 8 - BottomReserve));
        Recalculate();
    }
    public override void Update(GameTime time)
    {
        if (_entrance < 1) { _entrance = Math.Min(1, _entrance + (float)time.ElapsedGameTime.TotalSeconds / .16f); Arrange(); }
        base.Update(time);
        if (_tree != null && !_tree.Complete) { _tree.Advance(); RefreshTreeRows(); }
        if (!BlocksPointer) return;
        Main.LocalPlayer.mouseInterface = true;
        if (_plan?.Parent == this || _alternatives?.Parent == this) return;
        int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
        if (wheel != 0)
        {
            _scroll = Math.Max(0, _scroll - wheel * (_shownRecipe != null ? 1 : 3));
            Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
        }
    }
    private void Add(string text, int item = 0, int npc = 0, Color? color = null, int indent = 0) => _rows.Add(new(text, item, npc, color, indent));
    private void Rebuild()
    {
        _rows.Clear(); _hits.Clear(); _wrappedWidth = -1; _scroll = 0; _recipeCount = 0;
        _ingredients.Clear(); _ingredientHits.Clear(); IngredientVisuals.Clear(); _stationText.Clear(); _shownRecipe = null; _stationScroll = 0;
        _stationLineWidth = -1;
        _tree = null; _treeRevision = -1;
        _alternatives?.Remove(); _alternatives = null;
        _owned.Clear();
        if (_plan != null && UISystem.ActiveNetworkContents != null)
            foreach (var entry in UISystem.ActiveNetworkContents) AccumulateOwned(entry.Key.Type, entry.Value);
        else if (Main.LocalPlayer?.inventory != null)
            foreach (var item in Main.LocalPlayer.inventory) if (item != null && !item.IsAir) AccumulateOwned(item.type, item.stack);
        _plan?.Remove();
        GameDataIndex.EnsureBuilt();
        _forward.Enabled = _future.Count > 0;
        for (int i = 0; i < 4; i++) { _tabs[i].Selected = i == _tab; _tabs[i].Enabled = _npc == 0; }
        if (_npc > 0) BuildNpc();
        else
        {
            _title = UIItemNames.Display(_key);
            _subtitle = UIItemNames.ModNameOf(_key.Type);
            if (_plan != null) _subtitle += "  ·  已储存 " + UIFormat.Count(UISystem.GetStoredAmount(_key));
            if (_tab == 1) BuildSources();
            else if (_tab == 3 && _plan != null) { Append(_plan); _plan.Show(_key, _quantity); }
            else if (_tab == 3) BeginTree();
            else BuildRecipe();
        }
        _previous.Enabled = _next.Enabled = _recipeCount > 1;
        if (_rows.Count == 0 && _plan?.Parent != this && _shownRecipe == null) Add("没有登记相关信息", color: UITheme.Muted);
        Arrange();
    }
    private void BuildRecipe()
    {
        var recipes = _tab == 2 ? StorageSystem.Recipes.GetRecipesUsing(_key.Type) : StorageSystem.Recipes.GetRecipesFor(_key);
        _recipeCount = recipes.Count;
        if (_recipeCount == 0)
        {
            Add(_tab == 2 ? "没有合成用途" : "没有合成配方，可在来源中查看获取方式", color: UITheme.Muted);
            if (_tab == 2) foreach (string use in StorageSystem.Acquisition.GetUses(_key.WithoutPrefix())) Add(use);
            return;
        }
        _recipe = Math.Clamp(_recipe, 0, _recipeCount - 1);
        var r = _shownRecipe = Main.recipe[recipes[_recipe]];
        long output = Math.Max(1, r.createItem.stack);
        long batches = _tab == 2 ? 1 : _quantity / output + (_quantity % output > 0 ? 1 : 0);
        int index = 0;
        foreach (var ingredient in r.requiredItem)
        {
            if (ingredient.IsAir) continue;
            RecipeGroup group = r.acceptedGroups.Select(id => RecipeGroup.recipeGroups[id]).FirstOrDefault(g => g.ValidItems.Contains(ingredient.type));
            long owned = Owned(ingredient.type);
            if (group != null) { owned = 0; foreach (int type in group.ValidItems) owned = SaturatingAdd(owned, Owned(type)); }
            int displayed = group != null && _alternativeSelection.TryGetValue(index, out int selected) && group.ValidItems.Contains(selected) ? selected : ingredient.type;
            bool overflow = batches > long.MaxValue / ingredient.stack;
            _ingredients.Add(new IngredientCard(index++, displayed, overflow ? long.MaxValue : batches * ingredient.stack, owned, group, overflow));
        }
        foreach (int tile in r.requiredTile)
            if (tile >= 0) _stationText.Add("工作站：" + RecipeStation.DescribeStation(tile));
        if (_stationText.Count == 0) _stationText.Add("工作站：徒手");
        foreach (var condition in r.Conditions) _stationText.Add("条件：" + condition.Description.Value);
        if (r.Conditions.Count == 0) _stationText.Add("环境条件：无额外要求");
    }
    private static long SaturatingAdd(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + b;
    private void AccumulateOwned(int type, long amount) { if (amount > 0) _owned[type] = SaturatingAdd(Owned(type), amount); }
    private long Owned(int type) => _owned.TryGetValue(type, out long amount) ? amount : 0;
    private void Stations(Recipe recipe)
    {
        Add("工作站与条件", color: UITheme.Info);
        bool any = false;
        foreach (int tile in recipe.requiredTile)
        {
            if (tile < 0) continue;
            any = true;
            int item = ContentSamples.ItemsByType.Values.FirstOrDefault(i => i.createTile == tile)?.type ?? 0;
            Add(RecipeStation.DescribeStation(tile), item);
        }
        if (!any) Add("徒手");
        foreach (var condition in recipe.Conditions) Add(condition.Description.Value, color: UITheme.Warn);
    }
    private void BeginTree()
    {
        var recipes = StorageSystem.Recipes.GetRecipesFor(_key);
        _recipeCount = recipes.Count; _recipe = Math.Clamp(_recipe, 0, Math.Max(0, recipes.Count - 1));
        string signature = recipes.Count > 0 ? BrowserContent.RecipeKey(Main.recipe[recipes[_recipe]]) : "";
        _tree = new BookmarkTree(new BrowserBookmark(BrowserContent.Name(_key.Type), signature, _quantity));
        _tree.Advance(); RefreshTreeRows();
    }
    private void RefreshTreeRows()
    {
        if (_treeRevision == _tree.Revision) return;
        _treeRevision = _tree.Revision; _rows.Clear(); _wrappedWidth = -1;
        foreach (var node in _tree.Nodes)
        {
            string label = (node.Group ?? Lang.GetItemNameValue(node.Item)) + "  ×" + UIFormat.Count(node.Amount);
            if (node.Overflow) label += "（数量溢出）";
            if (node.Reference >= 0) label += "（重复分支）";
            if (node.Reference >= 0)
            {
                int reference = node.Reference;
                _rows.Add(new Row(label, node.Item, Indent: Math.Min(node.Depth, 5), Click: () =>
                    _scroll = Math.Max(0, _wrapped.FindIndex(line => ReferenceEquals(line.Row, _rows[reference])))));
            }
            else Add(label, node.Item, indent: Math.Min(node.Depth, 5));
        }
        if (!_tree.Complete) Add("正在展开…", color: UITheme.Muted);
        if (_tree.RootRecipe >= 0)
        {
            var recipes = StorageSystem.Recipes.GetRecipesFor(_key);
            _recipe = Math.Max(0, recipes.ToList().IndexOf(_tree.RootRecipe));
            Stations(Main.recipe[_tree.RootRecipe]);
        }
    }
    private void AddDrop(DropRecord drop, bool source)
    {
        string label = source ? drop.NpcType != 0 ? "掉落：" + NpcIndex.GetName(drop.NpcType)
            : "开启：" + Lang.GetItemNameValue(drop.ContainerType) : Lang.GetItemNameValue(drop.ItemType);
        // Variant net IDs are source records, not catalogue NPC identities.
        // Keep their proper name without sending negative IDs to texture/history APIs.
        Add(label + "  " + drop.Summary, source ? drop.ContainerType : drop.ItemType, source ? Math.Max(0,drop.NpcType) : 0);
        if (drop.Conditions.Count == 0) return;
        bool expanded = _expandedConditions.Contains(drop);
        _rows.Add(new Row((expanded ? "▾ " : "▸ ") + "条件（" + drop.Conditions.Count + "）", Color: UITheme.Warn, Indent: 1,
            Click: () => { if (!_expandedConditions.Add(drop)) _expandedConditions.Remove(drop); Rebuild(); }));
        if (expanded) foreach (string condition in drop.ConditionDescriptions()) Add(condition, color: UITheme.Muted, indent: 1);
    }
    private void BuildSources()
    {
        foreach (string source in StorageSystem.Acquisition.GetSources(_key.WithoutPrefix()))
            if (!GameDataIndex.IsIndexedDropText(source)) Add(source);
        int shimmer = ItemID.Sets.ShimmerTransformToItem[_key.Type];
        if (shimmer > 0) Add("微光转化：" + Lang.GetItemNameValue(shimmer), shimmer);
        foreach (var drop in GameDataIndex.GetContainerDrops(_key.Type)) AddDrop(drop, false);
        foreach (var drop in GameDataIndex.GetNpcSources(_key.Type)) AddDrop(drop, true);
        foreach (var drop in GameDataIndex.GetContainerSources(_key.Type)) AddDrop(drop, true);
    }
    private void BuildNpc()
    {
        _title = NpcIndex.GetName(_npc);
        _subtitle = BrowserCategories.NpcKinds[BrowserCategories.NpcKind(_npc)] + " · " + NpcVisuals.ModName(_npc);
        if (ContentSamples.NpcsByNetId.TryGetValue(_npc, out var npc))
            Add("生命 " + npc.lifeMax + "    伤害 " + npc.damage + "    防御 " + npc.defense);
        string spawn = StorageSystem.Acquisition.GetSpawnCondition(_npc);
        if (!string.IsNullOrWhiteSpace(spawn)) Add(spawn);
        Add("掉落物", color: UITheme.Info);
        var drops = GameDataIndex.GetNpcDropRecords(_npc);
        foreach (var drop in drops) AddDrop(drop, false);
        if (drops.Count == 0) Add("没有登记掉落物", color: UITheme.Muted);
    }
    private static IEnumerable<string> Wrap(string text, float width, float scale)
    {
        foreach (string paragraph in (text ?? "").Split('\n'))
        {
            string remaining = paragraph;
            if (remaining.Length == 0) { yield return ""; continue; }
            while (remaining.Length > 0)
            {
                int length = remaining.Length;
                while (length > 1 && FontAssets.MouseText.Value.MeasureString(remaining.Substring(0, length)).X * scale > width) length--;
                yield return remaining.Substring(0, length);
                remaining = remaining.Substring(length);
            }
        }
    }
    private void DrawRecipe(SpriteBatch batch, CalculatedStyle d)
    {
        _ingredientHits.Clear(); IngredientVisuals.Clear();
        int top = (int)d.Y + ContentTop;
        var output = new Rectangle((int)d.X + 10, top, 24, 24);
        DrawIcon(batch, _shownRecipe.createItem.type, 0, output);
        if (BrowserFavorites.Contains(_shownRecipe.createItem.type)) FavoriteInput.Mark(batch, output);
        _hits.Add((new Rectangle((int)d.X + 8, top, (int)d.Width - 16, 25), new Row("", _shownRecipe.createItem.type, Click: static () => { })));
        Utils.DrawBorderString(batch, Fit("产出  " + _shownRecipe.createItem.Name + " ×" + _shownRecipe.createItem.stack,
            .65f, d.Width - 48), new Vector2(d.X + 38, top + 5), UITheme.Info, .65f);
        int bottom = (int)(d.Y + d.Height - BottomReserve - 6), rowsTop = top + 27;
        float lineWidth = d.Width - 34;
        if (_stationLineWidth != lineWidth)
        {
            _stationLineWidth = lineWidth; _stationLines.Clear();
            _stationLines.AddRange(Wrap(string.Join("；", _stationText), lineWidth, .6f));
        }
        const int rowHeight = 27;
        int columns = Math.Max(1, (int)((d.Width - 20) / 390));
        int cardWidth = ((int)d.Width - 20 - (columns - 1) * 5) / columns;
        int materialRows = (_ingredients.Count + columns - 1) / columns;
        int metadataHeight = _stationLines.Count * 16 + 8;
        bool pinned = metadataHeight <= Math.Max(24, bottom - rowsTop - rowHeight - 5);
        _stationBounds = pinned
            ? new Rectangle((int)d.X + 10, bottom - metadataHeight, (int)d.Width - 20, metadataHeight)
            : Rectangle.Empty;
        int available = Math.Max(rowHeight, (pinned ? _stationBounds.Top - 4 : bottom) - rowsTop);
        int visible = Math.Max(1, available / rowHeight), total = materialRows + (pinned ? 0 : _stationLines.Count);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, total - visible)); _stationScroll = 0;
        for (int row = 0; row < visible && row + _scroll < total; row++)
        {
            int contentRow = row + _scroll, y = rowsTop + row * rowHeight;
            if (contentRow >= materialRows)
            {
                Utils.DrawBorderString(batch, _stationLines[contentRow - materialRows], new Vector2(d.X + 14, y + 4), UITheme.TextDim, .6f);
                continue;
            }
            for (int col = 0; col < columns; col++)
            {
                int index = contentRow * columns + col;
                if (index >= _ingredients.Count) break;
                var card = _ingredients[index];
                var rect = new Rectangle((int)d.X + 10 + col * (cardWidth + 5), y, cardWidth, rowHeight - 2);
                UITheme.Fill(batch, rect, rect.Contains(Main.MouseScreen.ToPoint()) ? UITheme.BtnHot : UITheme.Slot);
                var icon = new Rectangle(rect.X + 2, rect.Y + 1, 23, 23);
                DrawIcon(batch, card.Type, 0, icon);
                if (BrowserFavorites.Contains(card.Type)) FavoriteInput.Mark(batch, icon);
                string count = card.Overflow ? "需求超出数量范围" : "需求 " + UIFormat.Count(card.Required) + "  缺少 " + UIFormat.Count(Math.Max(0, card.Required - card.Owned));
                int countWidth = TextBounds(count, Vector2.Zero, .6f).Width;
                var choice = card.Group == null ? Rectangle.Empty : new Rectangle(rect.Right - 31, rect.Y + 2, 29, 21);
                var countPosition = new Vector2((card.Group == null ? rect.Right - 4 : choice.X - 5) - countWidth, rect.Y + 4);
                var namePosition = new Vector2(icon.Right + 4, rect.Y + 4);
                string name = Fit(card.Group?.GetText() ?? Lang.GetItemNameValue(card.Type), .6f, Math.Max(4, countPosition.X - namePosition.X - 7));
                Utils.DrawBorderString(batch, name, namePosition, UITheme.Text, .6f);
                Utils.DrawBorderString(batch, count, countPosition, card.Owned >= card.Required ? UITheme.Ok : UITheme.Danger, .6f);
                _ingredientHits.Add((rect, card, false));
                if (card.Group != null)
                {
                    UITheme.Fill(batch, choice, UITheme.Btn);
                    Utils.DrawBorderString(batch, "替代", new Vector2(choice.X + 3, choice.Y + 4), UITheme.Info, .55f);
                    _ingredientHits.Insert(0, (choice, card, true));
                }
                IngredientVisuals.Add(new(card.Index, rect, icon, TextBounds(name, namePosition, .6f), TextBounds(count, countPosition, .6f), choice));
            }
        }
        if (pinned)
        {
            UITheme.Fill(batch, _stationBounds, UITheme.Panel);
            for (int i = 0; i < _stationLines.Count; i++)
                Utils.DrawBorderString(batch, _stationLines[i], new Vector2(_stationBounds.X + 5, _stationBounds.Y + 4 + i * 16), UITheme.TextDim, .6f);
        }
        DrawScroll(batch, new Rectangle((int)d.X + 7, rowsTop, (int)d.Width - 14, available), _scroll, visible, total);
    }
    private static Rectangle TextBounds(string text,Vector2 position,float scale)
    {
        Vector2 size=FontAssets.MouseText.Value.MeasureString(text)*scale;
        return new Rectangle((int)position.X,(int)position.Y,(int)Math.Ceiling(size.X),(int)Math.Ceiling(size.Y));
    }
    private static void DrawScroll(SpriteBatch batch, Rectangle area, int scroll, int visible, int total)
    {
        if (total <= visible) return;
        int thumb = Math.Max(10, area.Height * visible / total);
        UITheme.Fill(batch, new Rectangle(area.Right - 3, area.Top + (area.Height - thumb) * scroll / Math.Max(1, total - visible), 3, thumb), UITheme.Muted);
    }
    private void ShowAlternatives(IngredientCard card)
    {
        _alternatives?.Remove();
        _alternatives = new AlternativePopup(this, card);
        Append(_alternatives); _alternatives.Recalculate();
    }
    private sealed class AlternativePopup : UIElement
    {
        private readonly UIBrowserDetails _owner;
        private readonly IngredientCard _card;
        private readonly int[] _types;
        private readonly List<(Rectangle Bounds, int Type)> _hits = new();
        private int _scroll;
        private Rectangle _closeBounds;
        internal AlternativePopup(UIBrowserDetails owner, IngredientCard card)
        {
            _owner = owner; _card = card; _types = card.Group.ValidItems.OrderBy(type => type).ToArray();
            Width.Set(0, 1); Height.Set(0, 1);
            OnLeftClick += (evt, _) =>
            {
                if (_closeBounds.Contains(evt.MousePosition.ToPoint())) { Remove(); return; }
                foreach (var hit in _hits) if (hit.Bounds.Contains(evt.MousePosition.ToPoint()))
                {
                    int scroll = owner._scroll, stationScroll = owner._stationScroll;
                    owner._alternativeSelection[card.Index] = hit.Type; owner.Rebuild();
                    owner._scroll = scroll; owner._stationScroll = stationScroll; return;
                }
            };
            OnRightClick += (evt, _) =>
            {
                foreach (var hit in _hits) if (hit.Bounds.Contains(evt.MousePosition.ToPoint())) { owner.Show(ItemKey.Crafted(hit.Type)); return; }
            };
        }
        public override void Update(GameTime time)
        {
            base.Update(time);
            if (!ContainsPoint(Main.MouseScreen)) return;
            Main.LocalPlayer.mouseInterface = true;
            int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
            _scroll = Math.Max(0, _scroll - delta);
            Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
            foreach (var hit in _hits) if (hit.Bounds.Contains(Main.MouseScreen.ToPoint())) { BrowserHover.Item(ItemKey.Crafted(hit.Type)); break; }
        }
        protected override void DrawSelf(SpriteBatch batch)
        {
            var d = GetDimensions(); var area = d.ToRectangle();
            UITheme.Fill(batch, area, UITheme.Panel); UITheme.Outline(batch, area, UITheme.BorderHot);
            Utils.DrawBorderString(batch, Fit("可替代材料 · " + _card.Group.GetText(), .8f, d.Width - 100), new Vector2(d.X + 12, d.Y + 14), UITheme.Info, .8f);
            _closeBounds = new Rectangle(area.Right - 78, area.Top + 8, 66, 27);
            UITheme.Fill(batch, _closeBounds, UITheme.Btn); Utils.DrawBorderString(batch, "关闭", new Vector2(_closeBounds.X + 15, _closeBounds.Y + 6), UITheme.Text, .75f);
            int columns = Math.Max(1, (area.Width - 24) / 176), width = (area.Width - 24 - (columns - 1) * 6) / columns;
            int rows = Math.Max(1, (area.Height - 54) / 74), total = (_types.Length + columns - 1) / columns;
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, total - rows)); _hits.Clear();
            for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
            {
                int index = (row + _scroll) * columns + col; if (index >= _types.Length) break;
                int type = _types[index]; var rect = new Rectangle(area.X + 12 + col * (width + 6), area.Y + 46 + row * 74, width, 68);
                _hits.Add((rect, type));
                UITheme.FillOutline(batch, rect, rect.Contains(Main.MouseScreen.ToPoint()) ? UITheme.BtnHot : UITheme.Slot, type == _card.Type ? UITheme.Accent : UITheme.Border);
                DrawIcon(batch, type, 0, new Rectangle(rect.X + 4, rect.Y + 5, 30, 30));
                int line = 0; foreach (string text in Wrap(Lang.GetItemNameValue(type), width - 45, .68f).Take(2)) Utils.DrawBorderString(batch, text, new Vector2(rect.X + 39, rect.Y + 5 + line++ * 18), UITheme.Text, .68f);
                Utils.DrawBorderString(batch, "持有 " + UIFormat.Count(_owner.Owned(type)), new Vector2(rect.X + 7, rect.Bottom - 20), UITheme.TextDim, .65f);
            }
            DrawScroll(batch, new Rectangle(area.X + 4, area.Y + 44, area.Width - 8, area.Height - 50), _scroll, rows, total);
        }
    }
    protected override void DrawSelf(SpriteBatch batch)
    {
        var d = GetDimensions(); var box = d.ToRectangle();
        UITheme.Fill(batch, box, UITheme.Panel); UITheme.Outline(batch, box, UITheme.ItemBorder);
        var titleIcon = new Rectangle(box.X + 10, box.Y + 32, 22, 22);
        DrawIcon(batch, _key.Type, _npc, titleIcon);
        if (BrowserFavorites.Contains(_npc > 0 ? _npc : _key.Type, _npc > 0)) FavoriteInput.Mark(batch, titleIcon);
        Utils.DrawBorderString(batch, Fit(_title, .7f, d.Width - 166), new Vector2(d.X + 36, d.Y + 34), UITheme.Text, .7f);
        Utils.DrawBorderString(batch, Fit(_subtitle, .6f, d.Width - 76), new Vector2(d.X + 12, d.Y + 56), UITheme.Muted, .6f);
        if (_recipeCount > 0) Utils.DrawBorderString(batch, (_recipe + 1) + " / " + _recipeCount,
            new Vector2(d.X + d.Width - 36, d.Y + 56), UITheme.TextDim, .6f, .5f);
        _hits.Clear();
        _hits.Add((new Rectangle(titleIcon.X, titleIcon.Y, Math.Max(22, box.Width - 140), 22),
            new Row("", _key.Type, _npc, Click: static () => { })));
        if (titleIcon.Contains(Main.MouseScreen.ToPoint())) FavoriteInput.Cursor();
        if (_shownRecipe != null) { DrawRecipe(batch, d); return; }
        _ingredientHits.Clear();
        IngredientVisuals.Clear();
        int rowTop = ContentTop;
        if (_npc > 0)
        {
            var portrait = new Rectangle(box.X + 12, box.Y + ContentTop, box.Width - 24, (int)Math.Clamp(d.Height * .25f, 56f, 138f));
            UITheme.FillRounded(batch, portrait, UITheme.Slot, 3);
            NpcVisuals.Draw(batch, _npc, portrait, 2f);
            rowTop = portrait.Bottom - box.Y + 10;
        }
        if (_plan?.Parent == this) return;
        var lines = _wrapped;
        if (_wrappedWidth != _width)
        {
        lines.Clear(); _wrappedWidth = _width;
        foreach (var row in _rows)
        {
            float width = Math.Max(50, _width - 36 - row.Indent * 13 - (row.Item > 0 || row.Npc > 0 ? 32 : 0));
            string text = row.Text; bool first = true;
            while (text.Length > 0)
            {
                int length = text.Length;
                while (length > 1 && FontAssets.MouseText.Value.MeasureString(text.Substring(0, length)).X * .75f > width) length--;
                lines.Add((row, text.Substring(0, length), first)); text = text.Substring(length); first = false;
            }
        }
        }
        int visible = Math.Max(1, (int)((d.Height - rowTop - 5 - BottomReserve) / 31));
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, lines.Count - visible));
        for (int i = _scroll; i < Math.Min(lines.Count, _scroll + visible); i++)
        {
            var (row, text, first) = lines[i];
            int y = (int)d.Y + rowTop + (i - _scroll) * 31;
            var rect = new Rectangle((int)d.X + 8 + row.Indent * 13, y, (int)d.Width - 24 - row.Indent * 13, 30);
            if (row.Item > 0 || row.Npc > 0 || row.Click != null)
            {
                _hits.Add((rect, row));
                if (rect.Contains(Main.MouseScreen.ToPoint())) UITheme.Fill(batch, rect, UITheme.BtnHot);
                if (first && (row.Item > 0 || row.Npc > 0)) DrawIcon(batch, row.Item, row.Npc, new Rectangle(rect.X, y, 28, 28));
            }
            Utils.DrawBorderString(batch, text, new Vector2(rect.X + (row.Item > 0 || row.Npc > 0 ? 33 : 3), y + 7), row.Color ?? UITheme.Text, .75f);
        }
        if (lines.Count > visible)
        {
            int track = Math.Max(12, (int)(d.Height - BottomReserve) - rowTop - 7), thumb = Math.Max(12, track * visible / lines.Count);
            UITheme.Fill(batch, new Rectangle(box.Right - 6, box.Top + rowTop + 1 + (track - thumb) * _scroll / Math.Max(1, lines.Count - visible), 3, thumb), UITheme.Muted);
        }
    }
    internal static void DrawIcon(SpriteBatch batch, int item, int npc, Rectangle box)
    {
        if (npc > 0) { NpcVisuals.Draw(batch, npc, box); return; }
        Texture2D texture; Rectangle frame;
        if (npc > 0) { Main.instance.LoadNPC(npc); texture = TextureAssets.Npc[npc].Value; frame = texture.Frame(1, Math.Max(1, Main.npcFrameCount[npc])); }
        else { Main.instance.LoadItem(item); texture = TextureAssets.Item[item].Value; frame = Main.itemAnimations[item]?.GetFrame(texture) ?? texture.Frame(); }
        float scale = Math.Min(1, (box.Width - 3f) / Math.Max(frame.Width, frame.Height));
        batch.Draw(texture, box.Center.ToVector2(), frame, Color.White, 0, frame.Size() / 2, scale, SpriteEffects.None, 0);
    }
    public void DrawTooltip(SpriteBatch batch)
    {
        if (!BlocksPointer || _alternatives?.Parent == this || UIChoiceMenu.IsOpen || UISystem.SecondaryInterface?.CurrentState != null || UISystem.OverlayInterface?.CurrentState != null) return;
        foreach (var hit in _ingredientHits) if (hit.Bounds.Contains(Main.MouseScreen.ToPoint())) { FavoriteInput.Cursor(); BrowserHover.Item(ItemKey.Crafted(hit.Card.Type)); return; }
        foreach (var hit in _hits) if (hit.Bounds.Contains(Main.MouseScreen.ToPoint()))
        {
            FavoriteInput.Cursor();
            if (hit.Row.Item > 0) BrowserHover.Item(ItemKey.Crafted(hit.Row.Item));
            else if (hit.Row.Npc > 0) UITooltip.Draw(batch, NpcIndex.GetName(hit.Row.Npc), BrowserCategories.NpcKinds[BrowserCategories.NpcKind(hit.Row.Npc)]);
            return;
        }
    }
    private static string Fit(string text, float scale, float width)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (FontAssets.MouseText.Value.MeasureString(text).X * scale <= width) return text;
        int length = text.Length;
        while (length > 1 && FontAssets.MouseText.Value.MeasureString(text.Substring(0, length) + "…").X * scale > width) length--;
        return text.Substring(0, length) + "…";
    }
}
