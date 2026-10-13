using AE2ForTerraria.Acquisition;
using AE2ForTerraria.Core;
using AE2ForTerraria.Progression;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 全局索引系统 —— 持有存储账本、配方索引、进度图、获取途径表,并在适当时机构建它们。
	/// </summary>
	public sealed class StorageSystem : ModSystem
	{
		public static RecipeIndex Recipes { get; private set; }

		public static ProgressionGraph Progression { get; private set; }

		public static AcquisitionRegistry Acquisition { get; private set; }

		public static CraftPlanner Planner { get; private set; }


		public static bool Ready => Recipes != null && Planner != null;

		public override void PostSetupContent()
		{
			// 拼音表(Assets/pinyin.dat),供搜索用
			PinyinIndex.Load(AE2ForTerraria.Instance);

			// 先创建索引对象；最终配方在 PostAddRecipes 阶段重新建立。
			Recipes = new RecipeIndex();
			Recipes.Build();

			Acquisition = new AcquisitionRegistry();

			// 注意:这里**不建** GameDataIndex。
			// PostSetupContent 阶段很多模组还没把自己的掉落规则 / 商店注册完,
			// 那时建出来的索引商店 0 条、Boss 只匹配到一小部分,等于白干 40–240ms,
			// 之后进世界还得整份重来一遍。改成第一次真正需要时一次建好。

			Progression = new ProgressionGraph(Recipes);
			Planner = new CraftPlanner(
				Recipes,
				key => Lang.GetItemNameValue(key.Type),
				key => Acquisition.Describe(key),
				key => Progression.GetTierIndex(key));
		}

		/// <summary>
		/// 配方索引必须在 PostSetupRecipes 构建：PostAddRecipes 之后仍会重排配方数组。
		/// BossChecklist 明确提示:GetBossInfoDictionary 太早调用会拿到不完整数据,
		/// 「For best results, call in PostAddRecipes」。
		/// </summary>
        public override void PostAddRecipes()
        {
            // Client loading runs in the background while Main can draw. Its opt-in
            // fixture borrows global player/world state, so run it from the menu Draw hook instead.
            if (Main.dedServ) RegressionChecks.RunIfRequested();
        }

		public override void PostSetupRecipes()
		{
			Recipes?.Build();
			Progression?.Build();
		}

		public override void Unload()
		{
			// 「重新加载模组」时,若模组的程序集没能被完整卸载,静态字段会活到下一个会话,
			// 于是新加的模组的物品 / 怪物就扫不进来。所以所有静态缓存都在这里显式清一遍。
			PinyinIndex.Unload();
			NpcIndex.Reset();
			GameDataIndex.Reset();
			ProgressionGraph.ResetInference();
			MENetwork.InvalidateCache();
			MEBlocks.Reset();
			WirelessRegistry.Reset();
			MEBlockWatcher.ResetBaseline();
            MENet.Reset();
            RecipeStation.Reset();

			// 这几个是静态引用,不置空的话重载后会留着上一轮的实例。
			Recipes = null;
			Progression = null;
			Acquisition = null;
			Planner = null;
		}

		public override void PostUpdateWorld() => MEBlockWatcher.FlushChanges();

        public override void ClearWorld()
        {
            MENet.Reset();
            MENetwork.InvalidateCache();
            WirelessRegistry.Reset();
        }

        public override void OnWorldLoad()
		{
			// 换世界之后「有几个网络」的基准要重算
			MEBlockWatcher.ResetBaseline();
            MENet.Reset();
            MENetwork.InvalidateCache();
            RecipeStation.Reset();

			// 进世界时所有模组的内容都已经注册完毕,是重建索引的最佳时机:
			// 这时拿到的商店 / 掉落 / Boss 数据才是完整的,而且只建这一次
			// (以前装载阶段先白建一次,进世界再整份重来)。
			bool alreadyBuilt = GameDataIndex.Built;
			GameDataIndex.EnsureBuilt();

			// 索引是刚建的,EnsureBuilt 里已经顺带重建过进度图了;
			// 如果是重进世界(索引早就建好),这里还要按当前世界的 downed* 状态再建一次。
			if (alreadyBuilt)
			{
				Progression?.Build();
			}
		}
	}
}
