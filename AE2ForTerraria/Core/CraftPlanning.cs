using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace AE2ForTerraria.Core;

public sealed class MissingIngredient
{
    public ItemKey Key;
    public long Amount;
    public string ItemName;
    public string Acquisition;
    public int TierIndex = -1;
}
public sealed class CraftPlan
{
    public ItemKey Target;
    public long Requested;
    public long ProducedAmount;
    public int MaxTierIndex = -1;
    public bool Limited;
    public readonly List<string> BlockedReasons=new();
    public readonly List<MissingIngredient> Missing = new();
    public readonly Dictionary<ItemKey, long> Consumed = new();
    public readonly Dictionary<ItemKey, long> Outputs = new();
    public readonly List<int> Recipes = new();
    public readonly List<string> Steps = new();
    public bool FullySatisfied => !Limited && Missing.Count == 0;
}
/// <summary>Call-local planning, with exact recipe outputs and surplus intermediates.</summary>
public sealed class CraftPlanner
{
    private readonly RecipeIndex _recipes;
    internal RecipeIndex Index => _recipes;
    private readonly Func<ItemKey, string> _name;
    private readonly Func<ItemKey, string> _acquisition;
    private readonly Func<ItemKey, int> _tier;
    private sealed class Context
    {
        public int Budget = 3000;
        public bool Limited;
        public bool QuantityLimited;
        public Func<Recipe,bool> Filter;
        public HashSet<string> Blocked=new();
        public bool Spend()
        {
            if (QuantityLimited) return false;
            if (Budget <= 0) { Limited = true; return false; }
            Budget--; return true;
        }
        public void LimitQuantity() { QuantityLimited = true; Limited = true; }
    }
    private sealed class State
    {
        public Dictionary<ItemKey,long> Pool = new();
        public Dictionary<ItemKey,long> Missing = new();
        public List<int> Recipes = new();
        public State Copy() => new() { Pool = new(Pool), Missing = new(Missing), Recipes = new(Recipes) };
        public void Use(State other) { Pool = other.Pool; Missing = other.Missing; Recipes = other.Recipes; }
    }
    public CraftPlanner(RecipeIndex recipes, Func<ItemKey,string> itemName = null,
        Func<ItemKey,string> acquisitionOf = null, Func<ItemKey,int> tierOf = null)
    { _recipes=recipes; _name=itemName ?? (k=>"Item #"+k.Type); _acquisition=acquisitionOf ?? (_=>""); _tier=tierOf ?? (_=>-1); }

    public CraftPlan Plan(StorageLedger storage, ItemKey target, long amount,
        Func<Recipe,bool> recipeFilter = null, bool useExistingTarget = true)
        => Plan(storage.Entries, target, amount, recipeFilter, useExistingTarget);

    // A network is a read-only inventory view, not one artificial disk. Its aggregate
    // count/capacity can exceed the capacity of any individual ledger.
    public CraftPlan Plan(IReadOnlyDictionary<ItemKey,long> storage, ItemKey target, long amount,
        Func<Recipe,bool> recipeFilter = null, bool useExistingTarget = true)
    {
        // Preserve the cheap successful route and the existing multi-leaf shortage explanation.
        // Only a failed greedy plan pays for backtracking across earlier ingredient choices.
        CraftPlan diagnostic = PlanGreedy(storage, target, amount, recipeFilter, useExistingTarget);
        if (diagnostic.FullySatisfied || target.IsAir || amount <= 0) return diagnostic;
        var before = new Dictionary<ItemKey,long>(storage);
        if (!useExistingTarget) before.Remove(target);
        var search = new ExactSearch(this, before, recipeFilter);
        bool solved = search.Resolve(target, amount);
        if (!solved)
        {
            diagnostic.Limited |= search.Limited;
            return diagnostic;
        }
        var plan = new CraftPlan
        {
            Target = target, Requested = amount,
            ProducedAmount = amount - (useExistingTarget ? Math.Min(amount, before.GetValueOrDefault(target)) : 0)
        };
        foreach (var entry in before)
        {
            long used = entry.Value - search.Pool.GetValueOrDefault(entry.Key);
            if (used > 0) plan.Consumed[entry.Key] = used;
        }
        foreach (var entry in search.Pool)
        {
            long surplus = entry.Value - before.GetValueOrDefault(entry.Key);
            if (surplus > 0) plan.Outputs[entry.Key] = surplus;
        }
        if (!TryAdd(plan.Outputs, target, amount)) { diagnostic.Limited = true; return diagnostic; }
        plan.Recipes.AddRange(search.Recipes);
        foreach (int index in plan.Recipes)
        {
            Recipe recipe = Main.recipe[index];
            plan.Steps.Add(_name(ItemKey.Crafted(recipe.createItem.type)) + " · 每次产出 " + recipe.createItem.stack);
        }
        return plan;
    }

    private CraftPlan PlanGreedy(IReadOnlyDictionary<ItemKey,long> storage, ItemKey target, long amount,
        Func<Recipe,bool> recipeFilter, bool useExistingTarget)
    {
        var plan = new CraftPlan { Target=target, Requested=amount };
        if (target.IsAir || amount<=0) { plan.Limited=true; return plan; }
        var state = new State { Pool = new(storage) };
        if (!useExistingTarget) state.Pool.Remove(target);
        var before = new Dictionary<ItemKey,long>(state.Pool);
        long existing = useExistingTarget ? Math.Min(amount,before.GetValueOrDefault(target)) : 0;
        var context = new Context { Filter=recipeFilter };
        Resolve(state,context,new HashSet<int>(),target,amount,0,false);
        if (context.QuantityLimited) state.Missing[target]=amount;
        plan.Limited=context.Limited;
        if (state.Missing.Count>0) plan.BlockedReasons.AddRange(context.Blocked);
        plan.ProducedAmount=amount-existing;
        foreach (var entry in before)
        {
            long used=entry.Value-state.Pool.GetValueOrDefault(entry.Key);
            if (used>0) plan.Consumed[entry.Key]=used;
        }
        foreach (var entry in state.Pool)
        {
            long surplus=entry.Value-before.GetValueOrDefault(entry.Key);
            if (surplus>0) plan.Outputs[entry.Key]=surplus;
        }
        if (!TryAdd(plan.Outputs,target,amount)) { plan.Limited=true; plan.Outputs.Clear(); }
        foreach (var entry in state.Missing)
        {
            int tier=_tier(entry.Key);
            plan.Missing.Add(new MissingIngredient { Key=entry.Key, Amount=entry.Value, ItemName=_name(entry.Key),
                Acquisition=_acquisition(entry.Key), TierIndex=tier });
            plan.MaxTierIndex=Math.Max(plan.MaxTierIndex,tier);
        }
        plan.Missing.Sort((a,b)=>a.TierIndex!=b.TierIndex ? a.TierIndex.CompareTo(b.TierIndex) : a.Key.Type.CompareTo(b.Key.Type));
        plan.Recipes.AddRange(state.Recipes);
        foreach (int index in state.Recipes)
        {
            Recipe recipe=Main.recipe[index];
            plan.Steps.Add(_name(ItemKey.Crafted(recipe.createItem.type))+" · 每次产出 "+recipe.createItem.stack);
        }
        return plan;
    }
    private static bool TryAdd(Dictionary<ItemKey,long> values, ItemKey key, long amount)
    {
        if (amount<=0) return true;
        long current=values.GetValueOrDefault(key);
        if (!TryAddAmount(current,amount,out long total)) return false;
        values[key]=total; return true;
    }
    private static bool TryAddAmount(long left,long right,out long total)
    {
        total=0;
        if (left<0 || right<0 || right>long.MaxValue-left) return false;
        total=left+right; return true;
    }
    private static bool TryMultiply(long count,long perBatch,out long amount)
    {
        amount=0;
        if (count<0 || perBatch<0 || count>0 && perBatch>long.MaxValue/count) return false;
        amount=count*perBatch; return true;
    }
    private static void AddMissing(State state,Context context,ItemKey key,long amount)
    { if (!TryAdd(state.Missing,key,amount)) context.LimitQuantity(); }
    private static long Take(State state, ItemKey key, long amount, bool variants)
    {
        long taken=0;
        var keys=new List<ItemKey> { key };
        if (variants) keys.AddRange(state.Pool.Keys.Where(k=>k.Type==key.Type && !k.Equals(key)).ToArray());
        foreach (var candidate in keys)
        {
            long n=Math.Min(amount-taken,state.Pool.GetValueOrDefault(candidate));
            if (n<=0) continue;
            state.Pool[candidate]-=n; taken+=n;
            if (taken==amount) break;
        }
        return taken;
    }
    private void Resolve(State state, Context context, HashSet<int> visiting, ItemKey key, long amount, int depth, bool variants)
    {
        amount-=Take(state,key,amount,variants);
        if (amount<=0) return;
        if (depth>=24) { context.Limited=true; AddMissing(state,context,key,amount); return; }
        if (!context.Spend()) { AddMissing(state,context,key,amount); return; }
        if (visiting.Contains(key.Type) || key.Prefix!=0 || key.Data!=null) { AddMissing(state,context,key,amount); return; }
        visiting.Add(key.Type);
        State best=null;
        decimal bestScore=decimal.MaxValue;
        foreach (int index in _recipes.GetRecipesFor(key))
        {
            if (!context.Spend()) break;
            Recipe recipe=Main.recipe[index];
            if (recipe==null || recipe.Disabled || recipe.createItem.stack<=0 || (context.Filter!=null && !context.Filter(recipe))) continue;
            string blocked=RecipeExecutionPolicy.BlockReason(recipe);
            if (blocked!=null) { context.Blocked.Add(_name(key)+": "+blocked); continue; }
            if (!ItemKey.FromItem(recipe.createItem).Equals(key)) { context.Blocked.Add(_name(key)+": 特殊产物数据需手动制作"); continue; }
            long crafts=(amount-1)/recipe.createItem.stack+1;
            if (!TryMultiply(crafts,recipe.createItem.stack,out long made)) { context.LimitQuantity(); break; }
            var trial=state.Copy();
            foreach (Item ingredient in recipe.requiredItem)
            {
                if (ingredient==null || ingredient.IsAir) continue;
                if (!TryMultiply(crafts,ingredient.stack,out long required)) { context.LimitQuantity(); break; }
                ResolveIngredient(trial,context,visiting,recipe,ingredient,required,depth+1);
                if (context.QuantityLimited) break;
            }
            if (context.QuantityLimited) break;
            if (!TryAdd(trial.Pool,key,made-amount)) { context.LimitQuantity(); break; }
            trial.Recipes.Add(index);
            decimal score=trial.Missing.Values.Sum(n=>(decimal)n);
            if (best==null || score<bestScore) { best=trial; bestScore=score; }
            if (score==0) break;
        }
        visiting.Remove(key.Type);
        if (context.QuantityLimited) return;
        if (best==null) AddMissing(state,context,key,amount); else state.Use(best);
    }
    private void ResolveIngredient(State state, Context context, HashSet<int> visiting, Recipe recipe, Item ingredient, long amount, int depth)
    {
        var alternatives=new SortedSet<int> { ingredient.type };
        foreach (int groupId in recipe.acceptedGroups)
            if (RecipeGroup.recipeGroups.TryGetValue(groupId,out var group) && group.ValidItems.Contains(ingredient.type)) alternatives.UnionWith(group.ValidItems);
        foreach (int type in alternatives)
        {
            amount-=Take(state,ItemKey.Crafted(type),amount,true);
            if (amount<=0) return;
        }
        State best=null;
        decimal score=decimal.MaxValue;
        foreach (int type in alternatives)
        {
            if (!context.Spend()) break;
            var trial=state.Copy();
            Resolve(trial,context,visiting,ItemKey.Crafted(type),amount,depth,true);
            if (context.QuantityLimited) return;
            decimal missing=trial.Missing.Values.Sum(n=>(decimal)n);
            if (best==null || missing<score) { best=trial; score=missing; }
            if (score==0 || context.Budget<=0) break;
        }
        if (best!=null) state.Use(best);
    }

    /// <summary>
    /// Bounded continuation search. A later ingredient can reject an earlier feasible route.
    /// Inventory changes are rolled back with a trail, not whole-inventory copies per branch.
    /// The greedy pass spends at most 3000 choices and this pass at most 9000 choices.
    /// </summary>
    private sealed class ExactSearch
    {
        private readonly CraftPlanner _owner;
        private readonly Context _context;
        private readonly Dictionary<int,List<ItemKey>> _variants = new();
        private readonly Dictionary<int,List<int>> _recipes = new();
        private readonly Dictionary<(int,int),int[]> _alternatives = new();
        private readonly List<(ItemKey Key,long Old,bool Existed)> _trail = new();
        private int _continuationDepth;
        public readonly Dictionary<ItemKey,long> Pool;
        public readonly List<int> Recipes = new();
        public bool Limited => _context.Limited;

        public ExactSearch(CraftPlanner owner, Dictionary<ItemKey,long> initial, Func<Recipe,bool> filter)
        {
            _owner = owner;
            Pool = new(initial);
            _context = new Context { Budget = 9000, Filter = filter };
            foreach (ItemKey key in initial.Keys)
            {
                if (!_variants.TryGetValue(key.Type,out var variants)) _variants[key.Type] = variants = new();
                variants.Add(key);
            }
        }
        private void Set(ItemKey key, long value)
        {
            bool existed = Pool.TryGetValue(key,out long old);
            if (existed && old == value) return;
            _trail.Add((key,old,existed));
            Pool[key] = value;
        }
        private void Rewind(int mark, int recipeMark)
        {
            for (int i = _trail.Count - 1; i >= mark; i--)
            {
                var entry = _trail[i];
                if (entry.Existed) Pool[entry.Key] = entry.Old;
                else Pool.Remove(entry.Key);
            }
            if (_trail.Count > mark) _trail.RemoveRange(mark,_trail.Count-mark);
            if (Recipes.Count > recipeMark) Recipes.RemoveRange(recipeMark,Recipes.Count-recipeMark);
        }
        private long Take(ItemKey key, long wanted, bool variants)
        {
            long taken = Math.Min(wanted,Pool.GetValueOrDefault(key));
            if (taken > 0) Set(key,Pool[key]-taken);
            if (variants && taken < wanted && _variants.TryGetValue(key.Type,out var keys))
                foreach (ItemKey candidate in keys)
                {
                    if (candidate.Equals(key)) continue;
                    long take = Math.Min(wanted-taken,Pool.GetValueOrDefault(candidate));
                    if (take > 0) { Set(candidate,Pool[candidate]-take); taken += take; }
                    if (taken == wanted) break;
                }
            return taken;
        }
        private long Available(int type)
        {
            ItemKey plain = ItemKey.Crafted(type);
            long result = Pool.GetValueOrDefault(plain);
            if (_variants.TryGetValue(type,out var keys))
                foreach (ItemKey key in keys)
                    if (!key.Equals(plain)) result = StorageAmounts.Add(result,Pool.GetValueOrDefault(key));
            return result;
        }
        private List<int> Candidates(int type)
        {
            if (_recipes.TryGetValue(type,out var cached)) return cached;
            var result = new List<int>();
            ItemKey key = ItemKey.Crafted(type);
            foreach (int index in _owner._recipes.GetRecipesFor(key))
            {
                if (!_context.Spend()) break;
                Recipe recipe = Main.recipe[index];
                if (recipe == null || recipe.Disabled || recipe.createItem.stack <= 0
                    || (_context.Filter != null && !_context.Filter(recipe))
                    || RecipeExecutionPolicy.BlockReason(recipe) != null
                    || !ItemKey.FromItem(recipe.createItem).Equals(key)) continue;
                result.Add(index);
            }
            _recipes[type] = result;
            return result;
        }
        private int[] Alternatives(int recipeIndex, int type)
        {
            if (_alternatives.TryGetValue((recipeIndex,type),out var cached)) return cached;
            var values = new SortedSet<int> { type };
            foreach (int groupId in Main.recipe[recipeIndex].acceptedGroups)
                if (RecipeGroup.recipeGroups.TryGetValue(groupId,out var group) && group.ValidItems.Contains(type))
                    values.UnionWith(group.ValidItems);
            return _alternatives[(recipeIndex,type)] = values.ToArray();
        }
        public bool Resolve(ItemKey key, long amount) => Resolve(key,amount,0,false,new HashSet<int>(),() => true);

        private bool Resolve(ItemKey key, long amount, int depth, bool variants, HashSet<int> ancestors, Func<bool> next)
        {
            if (!_context.Spend()) return false;
            int mark = _trail.Count, recipeMark = Recipes.Count;
            bool success = false;
            try
            {
                long needed = amount - Take(key,amount,variants);
                if (needed <= 0) return success = next();
                if (ancestors.Contains(key.Type) || key.Prefix != 0 || key.Data != null) return false;
                if (depth >= 24) { _context.Limited = true; return false; }
                var childAncestors = new HashSet<int>(ancestors) { key.Type };
                foreach (int index in Candidates(key.Type))
                {
                    Recipe recipe = Main.recipe[index];
                    long batchCount = (needed-1)/recipe.createItem.stack+1;
                    // Whole route first. Smaller partial routes allow the remainder to use another recipe.
                    for (long batches = batchCount; batches > 0; batches--)
                    {
                        if (!_context.Spend()) return false;
                        int batchMark = _trail.Count, batchRecipeMark = Recipes.Count;
                        if (!TryMultiply(batches,recipe.createItem.stack,out long made))
                        { _context.LimitQuantity(); return false; }
                        bool resolved = Ingredients(index,0,batches,depth+1,childAncestors,() =>
                        {
                            Recipes.Add(index);
                            if (made >= needed)
                            {
                                long surplus = made-needed;
                                if (surplus > 0)
                                {
                                    if (!TryAddAmount(Pool.GetValueOrDefault(key),surplus,out long total))
                                    { _context.LimitQuantity(); return false; }
                                    Set(key,total);
                                }
                                return next();
                            }
                            return Resolve(key,needed-made,depth+1,variants,ancestors,next);
                        });
                        if (resolved) return success = true;
                        Rewind(batchMark,batchRecipeMark);
                    }
                }
                return false;
            }
            finally { if (!success) Rewind(mark,recipeMark); }
        }
        private bool Ingredients(int index, int ingredientIndex, long batches, int depth, HashSet<int> ancestors, Func<bool> next)
        {
            var ingredients = Main.recipe[index].requiredItem;
            while (ingredientIndex < ingredients.Count && (ingredients[ingredientIndex] == null || ingredients[ingredientIndex].IsAir)) ingredientIndex++;
            if (ingredientIndex >= ingredients.Count) return next();
            Item ingredient = ingredients[ingredientIndex];
            int following = ingredientIndex+1;
            if (!TryMultiply(batches,ingredient.stack,out long amount)) { _context.LimitQuantity(); return false; }
            return Allocate(Alternatives(index,ingredient.type),0,amount,depth,ancestors,
                () => Ingredients(index,following,batches,depth,ancestors,next));
        }
        private bool Allocate(int[] types, int offset, long amount, int depth, HashSet<int> ancestors, Func<bool> next)
        {
            // A recipe can contain many ingredients and a group can contain many alternatives.
            // Bound continuation nesting as well as recipe depth, including zero-stock alternatives.
            if (_continuationDepth >= 128) { _context.Limited = true; return false; }
            _continuationDepth++;
            try { return AllocateCore(types,offset,amount,depth,ancestors,next); }
            finally { _continuationDepth--; }
        }
        private bool AllocateCore(int[] types, int offset, long amount, int depth, HashSet<int> ancestors, Func<bool> next)
        {
            if (!_context.Spend()) return false;
            if (amount <= 0) return next();
            if (offset >= types.Length) return false;
            int type = types[offset];
            if (offset == types.Length-1) return Resolve(ItemKey.Crafted(type),amount,depth,true,ancestors,next);
            long maximum = amount;
            if (ancestors.Contains(type) || Candidates(type).Count == 0) maximum = Math.Min(amount,Available(type));
            for (long share = maximum; share >= 0; share--)
            {
                if (!_context.Spend()) return false;
                long remaining = amount-share;
                if (share == 0)
                {
                    if (Allocate(types,offset+1,remaining,depth,ancestors,next)) return true;
                }
                else if (Resolve(ItemKey.Crafted(type),share,depth,true,ancestors,
                    () => Allocate(types,offset+1,remaining,depth,ancestors,next))) return true;
            }
            return false;
        }
    }
}
