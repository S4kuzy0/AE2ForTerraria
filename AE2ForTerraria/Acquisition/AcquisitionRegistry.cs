using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;

namespace AE2ForTerraria.Acquisition
{
	/// <summary>
	/// 获取途径注册表。原始资料(微光表、宝藏袋内容、怪物掉落、刷新条件)以可注册的数据表形式存在,
	/// 由本模组自带数据 + 其他模组/玩家数据共同填充。
	/// </summary>
	public sealed class AcquisitionRegistry
	{
		private readonly Dictionary<ItemKey, AcquisitionInfo> _info = new Dictionary<ItemKey, AcquisitionInfo>();
		private readonly Dictionary<int, int> _shimmer = new Dictionary<int, int>();
		private readonly Dictionary<int, List<int>> _treasureBags = new Dictionary<int, List<int>>();
		private readonly Dictionary<int, string> _spawnConditions = new Dictionary<int, string>();
		private readonly Dictionary<ItemKey, List<string>> _sources = new Dictionary<ItemKey, List<string>>();
		private readonly Dictionary<ItemKey, List<string>> _uses = new Dictionary<ItemKey, List<string>>();
		// Generated indexes can be replaced without deleting explicit registrations from callers.
		private readonly Dictionary<string, AcquisitionRegistry> _derived = new(StringComparer.Ordinal);
		private IEnumerable<AcquisitionRegistry> Layers => new[] { this }.Concat(_derived.Values);
		internal void ReplaceDerived(string owner, AcquisitionRegistry data)
		{
			if (string.IsNullOrEmpty(owner) || data == this || data?._derived.Count > 0)
				throw new ArgumentException("A derived acquisition layer must be a separate, flat registry.");
			if (data == null) _derived.Remove(owner); else _derived[owner] = data;
		}

		public int EntryCount => Layers.SelectMany(layer => layer._info.Keys).Distinct().Count();

		public int ShimmerRecipeCount => Layers.SelectMany(layer => layer._shimmer.Keys).Distinct().Count();

		public int TreasureBagCount => Layers.SelectMany(layer => layer._treasureBags.Keys).Distinct().Count();

		public int SpawnConditionCount => Layers.SelectMany(layer => layer._spawnConditions.Keys).Distinct().Count();

		public int SourceCount
		{
			get
			{
				return Layers.SelectMany(layer => layer._sources.Keys).Distinct().Sum(key => GetSources(key).Count);
			}
		}

		public void Register(ItemKey key, AcquisitionKind kind, string detail)
		{
			if (!_info.TryGetValue(key, out AcquisitionInfo info))
			{
				info = new AcquisitionInfo { Key = key };
				_info[key] = info;
			}

			info.Kind = kind;
			if (!string.IsNullOrEmpty(detail))
			{
				info.Detail = detail;
			}
		}

		/// <summary>
		/// 只在「还没有类别信息」时登记。
		/// 用来按优先级填:微光 > 宝藏袋 > 商店 > 怪物掉落。
		/// </summary>
		public bool RegisterIfAbsent(ItemKey key, AcquisitionKind kind, string detail)
		{
			if (Get(key) is AcquisitionInfo existing && existing.Kind != AcquisitionKind.Unknown)
			{
				return false;
			}

			Register(key, kind, detail);
			return true;
		}

		public AcquisitionInfo Get(ItemKey key)
		{
			if (_info.TryGetValue(key, out AcquisitionInfo info)) return info;
			foreach (var layer in _derived.Values)
				if (layer._info.TryGetValue(key, out info)) return info;
			return null;
		}

		public string Describe(ItemKey key)
		{
			// 有显式登记的类别信息就优先用它
			if (Get(key) is AcquisitionInfo info && info.Kind != AcquisitionKind.Unknown)
			{
				return info.Describe();
			}

			// 否则回退到已登记的来源列表(怪物掉落 / 商店 / 微光 等)。
			// GameDataIndex 会自动填这张表,所以这里通常都能取到东西。
			var list = GetSources(key);
			if (list.Count > 0)
			{
					int take = list.Count < 3 ? list.Count : 3;
					string joined = string.Join(" / ", list.Take(take));
					return list.Count > take
						? ModText.Format("AcqMoreWays", "{0} 等 {1} 种途径", joined, list.Count)
						: joined;
			}

			return string.Empty;
		}

		public void RegisterShimmer(int inputType, int outputType)
		{
			if (inputType > 0 && outputType > 0)
			{
				_shimmer[inputType] = outputType;
			}
		}

		public bool TryGetShimmerOutput(int inputType, out int outputType)
		{
			if (_shimmer.TryGetValue(inputType, out outputType)) return true;
			foreach (var layer in _derived.Values)
				if (layer._shimmer.TryGetValue(inputType, out outputType)) return true;
			outputType = 0; return false;
		}

		public void RegisterTreasureBag(int bagType, IEnumerable<int> contents)
		{
			if (bagType <= 0)
			{
				return;
			}

			if (!_treasureBags.TryGetValue(bagType, out List<int> list))
			{
				list = new List<int>();
				_treasureBags[bagType] = list;
			}

			foreach (int type in contents)
				if (type > 0 && !list.Contains(type)) list.Add(type);
		}

		public IReadOnlyList<int> GetTreasureBagContents(int bagType)
		{
			return Layers.Where(layer => layer._treasureBags.ContainsKey(bagType))
				.SelectMany(layer => layer._treasureBags[bagType]).Distinct().ToArray();
		}

		public void RegisterSpawnCondition(int npcType, string condition)
		{
			if (npcType > 0 && !string.IsNullOrEmpty(condition))
			{
				_spawnConditions[npcType] = condition;
			}
		}

		public string GetSpawnCondition(int npcType)
		{
			foreach (var layer in Layers)
				if (layer._spawnConditions.TryGetValue(npcType, out string condition)) return condition;
			return null;
		}

		// —— 来源 / 用途(供 JEI 式覆盖窗口显示) ——

		public void RegisterSource(ItemKey key, string text)
		{
			Add(_sources, key, text);
		}

		public void RegisterUse(ItemKey key, string text)
		{
			Add(_uses, key, text);
		}

		public IReadOnlyList<string> GetSources(ItemKey key)
		{
			if (_derived.Count == 0) return _sources.TryGetValue(key, out var list) ? list : Array.Empty<string>();
			return Layers.Where(layer => layer._sources.ContainsKey(key)).SelectMany(layer => layer._sources[key]).Distinct().ToArray();
		}

		public IReadOnlyList<string> GetUses(ItemKey key)
		{
			if (_derived.Count == 0) return _uses.TryGetValue(key, out var list) ? list : Array.Empty<string>();
			return Layers.Where(layer => layer._uses.ContainsKey(key)).SelectMany(layer => layer._uses[key]).Distinct().ToArray();
		}

		private static void Add(Dictionary<ItemKey, List<string>> map, ItemKey key, string text)
		{
			if (key.IsAir || string.IsNullOrEmpty(text))
			{
				return;
			}

			if (!map.TryGetValue(key, out List<string> list))
			{
				list = new List<string>();
				map[key] = list;
			}

			if (!list.Contains(text))
			{
				list.Add(text);
			}
		}

		public void Clear()
		{
			_derived.Clear();
			_info.Clear();
			_shimmer.Clear();
			_treasureBags.Clear();
			_spawnConditions.Clear();
			_sources.Clear();
			_uses.Clear();
		}
	}
}
