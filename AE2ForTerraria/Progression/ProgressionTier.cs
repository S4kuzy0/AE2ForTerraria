using System.Collections.Generic;

namespace AE2ForTerraria.Progression
{
	/// <summary>
	/// 一个进度检查点(阶段)。所有物品的时间点都落在这些检查点上。
	/// Index 就是排序用的时间点序号。
	/// </summary>
	public sealed class ProgressionTier
	{
		public int Index;
		public string Id;
		public string DisplayName;

		/// <summary>来源:vanilla / bosschecklist / builtin / inferred / player</summary>
		public string Source;

		/// <summary>
		/// 这个阶段对应的 NPC id 列表(只有 BossChecklist 会填)。
		/// 有了精确 id 就不用再拿阶段名去和怪物名做「包含匹配」,
		/// 多 NPC 事件(比如「克苏鲁之眼」这种一个阶段两个 NPC)也锚得准。
		/// </summary>
		public List<int> NpcIds = new List<int>();

		/// <summary>召唤这个阶段 Boss 用的物品(由 BossChecklist 提供,可选)。</summary>
		public List<int> SpawnItems = new List<int>();
        public List<int> LootItems = new();

		/// <summary>击败后拿到的宝藏袋物品 id(可选)。</summary>
		public int TreasureBag;

		/// <summary>BossChecklist 给的召唤说明(可选)。</summary>
		public string SpawnInfo;
        public System.Func<bool> IsDowned;

		public ProgressionTier(int index, string id, string displayName, string source)
		{
			Index = index;
			Id = id;
			DisplayName = displayName;
			Source = source;
		}

		public override string ToString()
		{
			return Index + ":" + DisplayName + " (" + Source + ")";
		}
	}
}
