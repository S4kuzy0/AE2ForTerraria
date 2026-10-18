using System.Collections.Generic;
using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace AE2ForTerraria
{
	/// <summary>
	/// 模组配置(游戏内 设置 → 模组配置 里可以改,会自动存档)。
	/// </summary>
	public class MEConfig : ModConfig
	{
		public static MEConfig Instance => ModContent.GetInstance<MEConfig>();

		/// <summary>
		/// 「详细日志」的静态读取口。索引构建、图鉴诊断、商店扫描样本、拼音表
		/// 这类日志默认不打 —— 每次启动都刷一遍太吵。排查问题时在模组配置里打开。
		/// </summary>
		public static bool DiagnosticsEnabled
		{
			get
			{
				try
				{
					MEConfig instance = Instance;
					return instance != null && instance.VerboseLogging;
				}
				catch
				{
					// 配置还没装载完时会取不到,当成关闭
					return false;
				}
			}
		}

		/// <summary>「网络满了是否销毁掉落物」的安全读取(配置没加载时当 false)。</summary>
		public static bool VoidOverflowDropsOn
		{
			get
			{
				try
				{
					MEConfig instance = Instance;
					return instance != null && instance.VoidOverflowDrops;
				}
				catch
				{
					return false;
				}
			}
		}

		/// <summary>打开详细日志(索引统计、图鉴诊断、商店扫描样本、拼音表等)。</summary>
		[DefaultValue(false)]
		public bool VerboseLogging = false;

		public override ConfigScope Mode => ConfigScope.ServerSide;

		/// <summary>掉落收集器方块是否工作。</summary>
		[DefaultValue(true)]
		public bool DropCollectorEnabled = true;

		/// <summary>
		/// 作弊模式开关(物品浏览器右下角的扳手按钮,点亮 = 变红)。
		/// 默认**开启** —— 点亮后 Shift + 左键可以直接刷出物品。
		/// 这是服务器端配置,所以开服的人关掉它,客户端就不会出现这个按钮。
		/// </summary>
		[DefaultValue(true)]
		public bool CheatModeEnabled = true;

		/// <summary>收集半径(格)。</summary>
		[Range(2f, 257f)]
		[DefaultValue(24f)]
		public float DropCollectorRadius = 24f;

		/// <summary>
		/// 随身掉落磁铁:站在自己的 ME 网络旁边时,附近的掉落物直接进网。
		/// 默认关闭(它会改变拾取手感,而且多人下是服务器执行)。
		/// </summary>
		[DefaultValue(false)]
		public bool PersonalMagnetEnabled = false;

		/// <summary>随身磁铁的半径(格)。</summary>
		[Range(2f, 40f)]
		[DefaultValue(12f)]
		public float PersonalMagnetRadius = 12f;

		/// <summary>
		/// 随身磁铁**自己的**过滤清单(和掉落收集器的默认清单分开)。
		/// 收集器和存储总线都有独立清单,磁铁挂在玩家身上,所以清单也就放在配置里。
		/// </summary>
		public List<ItemDefinition> PersonalMagnetFilteredItems = new List<ItemDefinition>();

		/// <summary>随身磁铁清单的用法:true = 黑名单,false = 白名单。</summary>
		[DefaultValue(true)]
		public bool PersonalMagnetIsBlacklist = true;

		/// <summary>
		/// 网络满了之后,收集器 / 磁铁**吸不进去的掉落物**要不要直接销毁。
		/// 默认 false(留在原地)—— 大量刷怪时打开它就不会堆一地。
		/// </summary>
		[DefaultValue(false)]
		public bool VoidOverflowDrops = false;

		/// <summary>
		/// 「存入全部」是否连**弹药栏和钱币栏**一起存。
		///
		/// 泰拉瑞亚的 player.inventory 一共 59 格:0–49 是主背包,50–53 是弹药栏,
		/// 54–57 是钱币栏,58 是鼠标/其它。以前我们扫的是整个 inventory,
		/// 于是「存入全部」会顺手把子弹和钱一起塞进 ME —— 这是真正的坑。
		/// 默认只扫主背包。
		/// </summary>
		[DefaultValue(false)]
		public bool DepositIncludesAmmoAndCoins = false;

		/// <summary>
		/// 兼容未连接合成终端的旧装配室耗时；合成终端的集成调度不使用此设置。
		/// </summary>
		[Range(1, 600)]
		[DefaultValue(20)]
		public int CraftTicksPerItem = 20;

		/// <summary>true = 黑名单(清单里的不收),false = 白名单(只收清单里的)。</summary>
		[DefaultValue(true)]
		public bool FilterIsBlacklist = true;

		/// <summary>过滤清单。按物品身份过滤,和数量无关。</summary>
		public List<ItemDefinition> FilteredItems = new List<ItemDefinition>();

		/// <summary>
		/// 玩家自定义的 Boss / 阶段顺序(可选)。
		/// 每行一个阶段名,从上到下就是先后顺序。留空则忽略。
		/// 填了就用它,不再用内置表和 BossChecklist。
		/// 名字要和怪物显示名对得上(可以写一部分,做包含匹配)。
		/// </summary>
		public List<string> CustomBossOrder = new List<string>();

		/// <summary>判断某个物品身份是否允许被收进来。</summary>
		/// <summary>
		/// 过滤缓存。改成**显式失效**:清单在配置界面被改动时由 OnChanged() 清一次,
		/// 之后每次判断都是纯查表。以前那种「每次重算签名」的写法虽然修好了
		/// 「同数量换物品不刷新」的 bug,但每次调用都要遍历整张清单。
		/// </summary>
		private HashSet<int> _cachedTypes;

		public bool Allows(int itemType)
		{
			bool listed = GetFilterSet().Contains(itemType);
			return FilterIsBlacklist ? !listed : listed;
		}

		/// <summary>玩家在配置界面改了清单之后,让缓存失效。</summary>
		public override void OnChanged()
		{
			_cachedTypes = null;
			_cachedMagnetTypes = null;
		}

		private HashSet<int> _cachedMagnetTypes;

		/// <summary>随身磁铁自己的过滤判断(和 <see cref="Allows"/> 各用一份清单)。</summary>
		public bool AllowsMagnet(int itemType)
		{
			if (PersonalMagnetFilteredItems == null || PersonalMagnetFilteredItems.Count == 0)
			{
				return true; // 清单为空 = 不过滤
			}

			if (_cachedMagnetTypes == null)
			{
				_cachedMagnetTypes = new HashSet<int>();

				foreach (ItemDefinition definition in PersonalMagnetFilteredItems)
				{
					if (definition != null && definition.Type > 0)
					{
						_cachedMagnetTypes.Add(definition.Type);
					}
				}
			}

			bool listed = _cachedMagnetTypes.Contains(itemType);
			return PersonalMagnetIsBlacklist ? !listed : listed;
		}

		private HashSet<int> GetFilterSet()
		{
			if (_cachedTypes == null)
			{
				_cachedTypes = new HashSet<int>();

				if (FilteredItems != null)
				{
					foreach (ItemDefinition definition in FilteredItems)
					{
						if (definition != null && definition.Type > 0)
						{
							_cachedTypes.Add(definition.Type);
						}
					}
				}
			}

			return _cachedTypes;
		}
	}
}
