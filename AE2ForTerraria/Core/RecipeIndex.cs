using System;
using System.Collections.Generic;
using Terraria;

namespace AE2ForTerraria.Core
{
	/// <summary>
	/// 配方反向索引:产出物 -> 配方下标列表。
	/// 这是「多个配方」与「几十万配方不卡」的关键 —— 查询永远是 O(该产出的配方数),不做线性扫描。
	/// </summary>
	public sealed class RecipeIndex
	{
		private readonly Dictionary<ItemKey, List<int>> _byOutput = new Dictionary<ItemKey, List<int>>();
		/// <summary>原料物品类型 -> 用到它的配方(用于「用途」查询)。</summary>
		private readonly Dictionary<int, List<int>> _byIngredient = new Dictionary<int, List<int>>();

		public bool IsBuilt { get; private set; }
        public int Revision { get; private set; }

		public int IndexedOutputs => _byOutput.Count;
        private readonly List<int> _outputTypes = new();
        public IReadOnlyList<int> OutputTypes { get { EnsureBuilt(); return _outputTypes; } }

		public int IndexedRecipes { get; private set; }

		public void Build()
		{
			_byOutput.Clear();
            _outputTypes.Clear();
			_byIngredient.Clear();
			IndexedRecipes = 0;

			int total = Recipe.numRecipes;
			for (int i = 0; i < total; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.Disabled || recipe.createItem == null || recipe.createItem.type <= 0)
				{
					continue;
				}

				// 合成产物不带前缀
				ItemKey key = ItemKey.Crafted(recipe.createItem.type);
				if (!_byOutput.TryGetValue(key, out List<int> list))
				{
					list = new List<int>();
					_byOutput[key] = list;
                    _outputTypes.Add(key.Type);
				}

				list.Add(i);
				IndexedRecipes++;

                var ingredientTypes = new HashSet<int>();
                foreach (Item ingredient in recipe.requiredItem)
                    if (ingredient != null && ingredient.type > 0) ingredientTypes.Add(ingredient.type);
                foreach (int groupId in recipe.acceptedGroups)
                    if (RecipeGroup.recipeGroups.TryGetValue(groupId, out var group)) ingredientTypes.UnionWith(group.ValidItems);
                foreach (int type in ingredientTypes)
                {
                    if (!_byIngredient.TryGetValue(type, out var usedBy)) _byIngredient[type] = usedBy = new List<int>();
                    usedBy.Add(i);
                }

			}

			IsBuilt = true;
            Revision++;
		}

		public void EnsureBuilt()
		{
			if (!IsBuilt)
			{
				Build();
			}
		}

		public IReadOnlyList<int> GetRecipesFor(ItemKey key)
		{
			EnsureBuilt();
			// 配方只记录无前缀产物,查询时统一剥掉前缀
			ItemKey lookup = key.WithoutPrefix();
			return _byOutput.TryGetValue(lookup, out List<int> list)
				? list
				: (IReadOnlyList<int>)Array.Empty<int>();
		}

		/// <summary>哪些配方会用到这个物品(「用途」)。</summary>
		public IReadOnlyList<int> GetRecipesUsing(int itemType)
		{
			EnsureBuilt();
			return _byIngredient.TryGetValue(itemType, out List<int> list)
				? list
				: (IReadOnlyList<int>)Array.Empty<int>();
		}
	}
}
