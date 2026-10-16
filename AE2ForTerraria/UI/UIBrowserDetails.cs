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
    private readonly UIButton _back, _previous, _next, _favorite, _forward;
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
    private sealed record IngredientCard(int Index, int Type, int Required, long Owned, RecipeGroup Group);
    private readonly List<IngredientCard> _ingredients = new();
    private readonly Dictionary<int, int> _alternativeSelection = new();
    private readonly List<string> _stationText = new();
    private readonly List<(Rectangle Bounds, IngredientCard Card, bool Alternatives)> _ingredientHits = new();
    internal readonly record struct IngredientVisual(int Index, Rectangle Card, Rectangle Icon,
        Rectangle Name, Rectangle Count, Rectangle Alternatives);
    internal readonly List<IngredientVisual> IngredientVisuals = new();
    private readonly Dictionary<int, long> _owned = new();
    private Recipe _shownRecipe;
    private AlternativePopup _alternatives;
    private int _stationScroll;
    private Rectangle _stationBounds;
    private readonly List<string> _stationLines = new();
    private float _stationLineWidth = -1;
    private int ContentTop => _width < 520 ? 105 : _npc == 0 ? 121 : 153;
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
        _favorite = Button("收藏", () => { BrowserFavorites.Toggle(_npc > 0 ? _npc : _key.Type, _npc > 0); UpdateFavorite(); FavoriteChanged?.Invoke(); });
        _previous = Button("上一配方", () => CycleRecipe(-1));
        _next = Button("下一配方", () => CycleRecipe(1));
        _tabs = new UIButton[4];
        string[] labels = { "配方", "来源", "用途", "合成树" };
        for (int i = 0; i < 4; i++) { int tab = i; _tabs[i] = Button(labels[i], () => SetTab(tab)); }
        if (terminal) _plan = new UIRecipePanel();
        OnLeftClick += (e, _) =>
        {
            if (e.Target != this) return;
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
        IsOpen = true; _entrance = 0; _host.Append(this); Arrange();
    }
    public void Close() { IsOpen = false; _alternatives?.Remove(); _alternatives = null; Remove(); _hits.Clear(); _ingredientHits.Clear(); IngredientVisuals.Clear(); Closed?.Invoke(); }
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
    }
    public void ShowNpc(int type, bool remember = true)
    {
        if (remember && (_npc != type || !_key.IsAir)) Remember();
        _npc = type; _key = ItemKey.None; _tab = 1; Open(); Rebuild(); Selected?.Invoke(ItemKey.None);
    }
    private Page CurrentPage => new(_key, _npc, _tab, _recipe, ReaderScroll, _stationScroll, _quantity);
    private static void Push(Stack<Page> stack, Page page)
    {
        if (stack.Count >= 64)
        {
            var recent = stack.Take(63).Reverse().ToArray();
            stack.Clear(); foreach (var entry in recent) stack.Push(entry);
        }
        stack.Push(page);
    }
    private void Remember()
    {
        if (_key.IsAir && _npc <= 0) return;
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
    private void UpdateFavorite() => _favorite.Selected = BrowserFavorites.Contains(_npc > 0 ? _npc : _key.Type, _npc > 0);
    public void SetTab(int tab)
    {
        if (_npc > 0 || tab < 0 || tab >= _tabs.Length || tab == _tab) return;
        Remember(); _tab = tab; _recipe = 0; Rebuild();
    }
    private void CycleRecipe(int delta)
    {
        if (_recipeCount <= 1) return;
        Remember(); _recipe = (_recipe + delta + _recipeCount) % _recipeCount; _alternativeSelection.Clear(); Rebuild();
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
        Place(_back, 10, 4, 46, 22); Place(_forward, 60, 4, 46, 22); Place(_favorite, _width - offset - 56, 4, 46, 22);
        if (_width < 520)
        {
            float compactTab = (_width - offset - 142) / 4;
            for (int i = 0; i < 4; i++) Place(_tabs[i], 10 + i * (compactTab + 2), 76, compactTab, 22);
            Place(_previous, _width - offset - 122, 76, 52, 22);
            Place(_next, _width - offset - 66, 76, 52, 22);
            if (_plan != null) Place(_plan, 8, ContentTop, _width - offset - 16, Math.Max(40, _height - ContentTop - 8 - BottomReserve));
            Recalculate(); return;
        }
        float tabWidth = _width >= 520 && _npc == 0 ? Math.Min(90, (_width - offset - 224) / 4) : (_width - offset - 26) / 4;
        for (int i = 0; i < 4; i++) Place(_tabs[i], 10 + i * (tabWidth + 2), 87, tabWidth, 28);
        if (ContentTop == 121) { Place(_previous, _width - offset - 202, 87, 70, 26); Place(_next, _width - offset - 80, 87, 70, 26); }
        else { Place(_previous, 10, 120, 86, 26); Place(_next, _width - offset - 96, 120, 86, 26); }
        if (_plan != null) Place(_plan, 8, ContentTop, _width - offset - 16, Math.Max(40, _height - ContentTop - 8 - BottomReserve));
        Recalculate();
    }
    public override void Update(GameTime time)
    {
        if (_entrance < 1) { _entrance = Math.Min(1, _entrance + (float)time.ElapsedGameTime.TotalSeconds / .16f); Arrange(); }
        base.Update(time);
        if (!BlocksPointer) return;
        Main.LocalPlayer.mouseInterface = true;
        if (_plan?.Parent == this || _alternatives?.Parent == this) return;
        int wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
        if (wheel != 0)
        {
            if (_shownRecipe != null && _stationBounds.Contains(Main.MouseScreen.ToPoint())) _stationScroll = Math.Max(0, _stationScroll - wheel * 2);
            else _scroll = Math.Max(0, _scroll - wheel * (_shownRecipe != null ? 1 : 3));
            Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
        }
    }
    private void Add(string text, int item = 0, int npc = 0, Color? color = null, int indent = 0) => _rows.Add(new(text, item, npc, color, indent));
    private void Rebuild()
    {
        _rows.Clear(); _hits.Clear(); _wrappedWidth = -1; _scroll = 0; _recipeCount = 0;
        _ingredients.Clear(); _ingredientHits.Clear(); IngredientVisuals.Clear(); _stationText.Clear(); _shownRecipe = null; _stationScroll = 0;
        _stationLineWidth = -1;
        _alternatives?.Remove(); _alternatives = null;
        _owned.Clear();
        if (_plan != null && UISystem.ActiveNetworkContents != null)
            foreach (var entry in UISystem.ActiveNetworkContents) AccumulateOwned(entry.Key.Type, entry.Value);
        else if (Main.LocalPlayer?.inventory != null)
            foreach (var item in Main.LocalPlayer.inventory) if (item != null && !item.IsAir) AccumulateOwned(item.type, item.stack);
        _plan?.Remove();
        GameDataIndex.EnsureBuilt();
        _forward.Enabled = _future.Count > 0; UpdateFavorite();
        for (int i = 0; i < 4; i++) { _tabs[i].Selected = i == _tab; _tabs[i].Enabled = _npc == 0; }
        if (_npc > 0) BuildNpc();
        else
        {
            _title = UIItemNames.Display(_key);
            _subtitle = UIItemNames.ModNameOf(_key.Type);
            if (_plan != null) _subtitle += "  ·  已储存 " + UIFormat.Count(UISystem.GetStoredAmount(_key));
            if (_tab == 1) BuildSources();
            else if (_tab == 3 && _plan != null) { Append(_plan); _plan.Show(_key, _quantity); }
            else if (_tab == 3) BuildTree(_key.Type, _quantity, 0, new HashSet<int>());
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
        int index = 0;
        foreach (var ingredient in r.requiredItem)
        {
            if (ingredient.IsAir) continue;
            RecipeGroup group = r.acceptedGroups.Select(id => RecipeGroup.recipeGroups[id]).FirstOrDefault(g => g.ValidItems.Contains(ingredient.type));
            long owned = Owned(ingredient.type);
            if (group != null) { owned = 0; foreach (int type in group.ValidItems) owned = SaturatingAdd(owned, Owned(type)); }
            int displayed = group != null && _alternativeSelection.TryGetValue(index, out int selected) && group.ValidItems.Contains(selected) ? selected : ingredient.type;
            _ingredients.Add(new IngredientCard(index++, displayed, ingredient.stack, owned, group));
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
            Add(item > 0 ? Lang.GetItemNameValue(item) : "工作站 #" + tile, item);
        }
        if (!any) Add("徒手");
        foreach (var condition in recipe.Conditions) Add(condition.Description.Value, color: UITheme.Warn);
    }
    private void BuildTree(int type, long amount, int depth, HashSet<int> path)
    {
        if (_rows.Count >= 350) return;
        if (depth == 0) Add("配方展开 · 下级点击可切换其配方", color: UITheme.Muted);
        Add(Lang.GetItemNameValue(type) + "  ×" + amount, type, indent: Math.Min(depth, 5));
        if (!path.Add(type)) { Add("循环配方，停止展开", color: UITheme.Warn, indent: Math.Min(depth + 1, 5)); return; }
        var recipes = StorageSystem.Recipes.GetRecipesFor(ItemKey.Crafted(type));
        if (depth == 0) _recipeCount = recipes.Count;
        if (recipes.Count == 0 || depth >= 16) { path.Remove(type); return; }
        var recipe = Main.recipe[recipes[depth == 0 ? Math.Clamp(_recipe, 0, recipes.Count - 1) : 0]];
        long output = Math.Max(1, recipe.createItem.stack);
        long batches = amount / output + (amount % output == 0 ? 0 : 1);
        foreach (var ingredient in recipe.requiredItem)
            if (!ingredient.IsAir)
            {
                if (batches > long.MaxValue / ingredient.stack)
                    Add(ingredient.Name + "：需求数量超出可显示范围", ingredient.type, color: UITheme.Warn, indent: Math.Min(depth + 1, 5));
                else BuildTree(ingredient.type, batches * ingredient.stack, depth + 1, path);
            }
        path.Remove(type);
        if (depth == 0) Stations(recipe);
    }
    private void AddDrop(DropRecord drop, bool source)
    {
        string label = source ? drop.NpcType > 0 ? "掉落：" + NpcIndex.GetName(drop.NpcType)
            : "开启：" + Lang.GetItemNameValue(drop.ContainerType) : Lang.GetItemNameValue(drop.ItemType);
        Add(label + "  " + drop.Summary, source ? drop.ContainerType : drop.ItemType, source ? drop.NpcType : 0);
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
        _ingredientHits.Clear();
        IngredientVisuals.Clear();
        int top = (int)d.Y + ContentTop;
        DrawIcon(batch, _shownRecipe.createItem.type, 0, new Rectangle((int)d.X + 12, top, 26, 26));
        Utils.DrawBorderString(batch, Fit("产出  " + _shownRecipe.createItem.Name + " ×" + _shownRecipe.createItem.stack,
            .75f, d.Width - 55), new Vector2(d.X + 43, top + 6), UITheme.Info, .75f);
        int bottom = (int)(d.Y + d.Height - BottomReserve - 8);
        int metadataHeight = Math.Clamp((int)(d.Height * .23f), 48, 100);
        metadataHeight=Math.Min(metadataHeight,Math.Max(24,bottom-top-82));
        _stationBounds = new Rectangle((int)d.X + 10, Math.Max(top + 82, bottom - metadataHeight), (int)d.Width - 20, metadataHeight);
        UITheme.Fill(batch, _stationBounds, UITheme.Panel);
        float lineWidth = _stationBounds.Width - 18;
        if (_stationLineWidth != lineWidth)
        {
            _stationLineWidth = lineWidth; _stationLines.Clear();
            foreach (string text in _stationText) _stationLines.AddRange(Wrap(text, lineWidth, .72f));
        }
        int visibleStations = Math.Max(1, (_stationBounds.Height - 10) / 21);
        _stationScroll = Math.Clamp(_stationScroll, 0, Math.Max(0, _stationLines.Count - visibleStations));
        for (int i = 0; i < visibleStations && i + _stationScroll < _stationLines.Count; i++)
            Utils.DrawBorderString(batch, _stationLines[i + _stationScroll], new Vector2(_stationBounds.X + 7, _stationBounds.Y + 6 + i * 21), UITheme.TextDim, .72f);
        DrawScroll(batch, _stationBounds, _stationScroll, visibleStations, _stationLines.Count);
        int cardsTop = top + 31, available = Math.Max(44, _stationBounds.Top - cardsTop - 7);
        int columns = Math.Max(1, (int)((d.Width - 20) / 188));
        int cardWidth = ((int)d.Width - 20 - (columns - 1) * 6) / columns;
        int cardHeight = Math.Min(82, available);
        int visibleRows = Math.Max(1, (available + 6) / (cardHeight + 6));
        int totalRows = (_ingredients.Count + columns - 1) / columns;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, totalRows - visibleRows));
        for (int row = 0; row < visibleRows; row++)
            for (int col = 0; col < columns; col++)
            {
                int index = (row + _scroll) * columns + col;
                if (index >= _ingredients.Count) break;
                var card = _ingredients[index];
                var rect = new Rectangle((int)d.X + 10 + col * (cardWidth + 6), cardsTop + row * (cardHeight + 6), cardWidth, cardHeight);
                UITheme.FillOutline(batch, rect, rect.Contains(Main.MouseScreen.ToPoint()) ? UITheme.BtnHot : UITheme.Slot, UITheme.Border);
                string count="持有 "+UIFormat.Count(card.Owned)+" / "+card.Required
                    +(card.Owned>=card.Required ? " · 齐全" : " · 缺 "+(card.Required-card.Owned));
                count=Fit(count,.65f,rect.Width-10);
                int countHeight=TextBounds(count,Vector2.Zero,.65f).Height;
                bool compact=cardHeight<Math.Max(76,63+countHeight);
                var countPosition=new Vector2(rect.X+5,rect.Bottom-countHeight-(card.Group==null || compact ? 2 : 27));
                int iconSize=Math.Min(compact ? 20 : 30,(int)countPosition.Y-rect.Y-6);
                var icon=new Rectangle(rect.X+4,rect.Y+4,iconSize,iconSize);
                var choice=card.Group==null ? Rectangle.Empty : compact
                    ? new Rectangle(rect.Right-72,rect.Y+3,68,Math.Min(20,(int)countPosition.Y-rect.Y-4))
                    : new Rectangle(rect.X+4,rect.Bottom-25,rect.Width-8,21);
                var namePosition=new Vector2(icon.Right+4,rect.Y+(compact ? 3 : 5));
                float nameWidth=(compact && card.Group!=null ? choice.X-4 : rect.Right-5)-namePosition.X;
                string name=Lang.GetItemNameValue(card.Type);
                float nameScale=Math.Min(.7f,(countPosition.Y-namePosition.Y-2)
                    / Math.Max(1,FontAssets.MouseText.Value.MeasureString(name).Y));
                int nameHeight=(int)Math.Ceiling(FontAssets.MouseText.Value.MeasureString(name).Y*nameScale);
                int nameLineCount=Math.Clamp((int)(countPosition.Y-namePosition.Y-2-nameHeight)/17+1,1,compact ? 1 : 2);
                var nameLines=nameLineCount==1 ? new[]{Fit(name,nameScale,nameWidth)}
                    : Wrap(name,nameWidth,nameScale).Take(nameLineCount).ToArray();
                Rectangle nameBounds=Rectangle.Empty;
                DrawIcon(batch,card.Type,0,icon);
                for(int i=0;i<nameLines.Length;i++)
                {
                    var textPosition=namePosition+new Vector2(0,i*17);
                    string text=nameLines[i];
                    Rectangle lineBounds=TextBounds(text,textPosition,nameScale);
                    Utils.DrawBorderString(batch,text,textPosition,UITheme.Text,nameScale);
                    nameBounds=nameBounds.IsEmpty ? lineBounds : Rectangle.Union(nameBounds,lineBounds);
                }
                _ingredientHits.Add((rect, card, false));
                Utils.DrawBorderString(batch,count,countPosition,card.Owned>=card.Required ? UITheme.Ok : UITheme.Danger,.65f);
                if (card.Group != null)
                {
                    UITheme.Fill(batch, choice, UITheme.Btn);
                    string label=(compact ? "替代（" : "替代材料（")+card.Group.ValidItems.Count+"）";
                    float labelScale=Math.Min(.65f,(choice.Height-2)/Math.Max(1,FontAssets.MouseText.Value.MeasureString(label).Y));
                    Utils.DrawBorderString(batch,Fit(label,labelScale,choice.Width-8),new Vector2(choice.X+4,choice.Y+1),UITheme.Info,labelScale);
                    _ingredientHits.Insert(0, (choice, card, true));
                }
                IngredientVisuals.Add(new(card.Index,rect,icon,nameBounds,TextBounds(count,countPosition,.65f),choice));
            }
        DrawScroll(batch, new Rectangle((int)d.X + 7, cardsTop, (int)d.Width - 14, available), _scroll, visibleRows, totalRows);
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
        UITheme.Fill(batch, box, new Color(32, 42, 68)); UITheme.Outline(batch, box, UITheme.BorderHot);
        Utils.DrawBorderString(batch, Fit(_title, .85f, d.Width - 24), new Vector2(d.X + 12, d.Y + (_width < 520 ? 32 : 43)), UITheme.Text, .85f);
        Utils.DrawBorderString(batch, Fit(_subtitle, .65f, d.Width - 96), new Vector2(d.X + 12, d.Y + (_width < 520 ? 55 : 67)), UITheme.Muted, .65f);
        if (_recipeCount > 0) Utils.DrawBorderString(batch, (_recipe + 1) + " / " + _recipeCount,
            new Vector2(_width < 520 ? d.X + d.Width - 40 : ContentTop == 121 ? d.X + d.Width - 107 : d.X + d.Width / 2, d.Y + (_width < 520 ? 55 : ContentTop == 121 ? 94 : 126)), UITheme.TextDim, .65f, .5f);
        _hits.Clear();
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
        foreach (var hit in _ingredientHits) if (hit.Bounds.Contains(Main.MouseScreen.ToPoint())) { BrowserHover.Item(ItemKey.Crafted(hit.Card.Type)); return; }
        foreach (var hit in _hits) if (hit.Bounds.Contains(Main.MouseScreen.ToPoint()))
        {
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
