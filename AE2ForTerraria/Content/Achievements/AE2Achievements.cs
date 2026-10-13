using Microsoft.Xna.Framework.Graphics;
using Terraria.Achievements;
using Terraria.GameContent.Achievements;
using Terraria.ModLoader;

namespace AE2ForTerraria.Content.Achievements
{
	/// <summary>
	/// 成就基类。
	///
	/// tModLoader 的成就模型是「成就持有一个条件,条件满足就完成」,
	/// 所以每个成就自己持有一个 <see cref="AchievementCondition"/>,
	/// 需要触发时调用 <see cref="Trigger"/> 把它标记完成。
	/// 名字 / 描述的本地化键由框架按 `Achievements.&lt;类名&gt;` 推出来
	/// (见 Localization 里的 Achievements 段)。
	/// </summary>
	public abstract class AE2Achievement : ModAchievement
	{
		/// <summary>本成就的条件 —— 触发点调用 Trigger() 就是完成它。</summary>
		protected AchievementCondition Condition;

		/// <summary>图标资源路径(子类覆写框架的贴图名)。</summary>
		public abstract override string TextureName { get; }

		public override void SetStaticDefaults()
		{
			// 自定义条件:我们自己决定什么时候算完成(见 Trigger)
			Condition = CustomFlagCondition.Create(Name);
			AddCondition(Condition);
		}

		/// <summary>完成这个成就(重复调用无副作用)。</summary>
		public void Trigger()
		{
			try
			{
				if (Condition?.IsCompleted == true)
				{
					return;
				}

				Condition?.Complete();
				NotifyToast();
			}
			catch
			{
				// 不在世界里时会拿不到追踪器,忽略即可
			}
		}

		/// <summary>完成时在屏幕下方给一条提示(不需要开着窗口也能看到)。</summary>
		private void NotifyToast()
		{
			try
			{
				Systems.UIStatus.Toast(
					Systems.ModText.Get("MsgAchievementUnlocked", "成就达成:") + " "
					+ FriendlyName?.Value,
					new Microsoft.Xna.Framework.Color(255, 225, 140));
			}
			catch
			{
				// 取不到本地化文本就算了
			}
		}
	}

	/// <summary>第一次把物品存进 ME 网络。</summary>
	public sealed class StoreFirstItemAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MEDiskDrive";
	}

	/// <summary>第一次让分子装配室自动造出东西。</summary>
	public sealed class AutomateFirstItemAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MEMolecularAssembler";
	}

	/// <summary>绑定一个 ME 访问终端。</summary>
	public sealed class BindTerminalAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MEAccessTerminal";
	}

	/// <summary>给合成 CPU 设一次「保持库存」。</summary>
	public sealed class KeepStockAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MECraftingCpu";
	}

	/// <summary>单个网络存到 64K 字节。</summary>
	public sealed class BigStorageAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MEDisk64K";
	}

	/// <summary>用无线桥接把相隔很远的两个网络连起来。</summary>
	public sealed class LongRangeWirelessAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MEWirelessBridge";
	}

	/// <summary>同时维持 5 种物品的库存。</summary>
	public sealed class KeepFiveAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MECraftingCpu";
	}

	/// <summary>把一整个网络的东西转移到另一个网络。</summary>
	public sealed class TransferNetworkAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MEStorageBus";
	}

	/// <summary>一个网络里同时有 5 个分子装配室(并行合成)。</summary>
	public sealed class FiveAssemblersAchievement : AE2Achievement
	{
		public override string TextureName => "AE2ForTerraria/Content/Items/MEMolecularAssembler";
	}

	/// <summary>
	/// 触发成就的统一入口(取不到实例就静默跳过)。
	/// 名字不叫 Achievements —— 那会和 `AE2ForTerraria.Content.Achievements` 这个命名空间撞名。
	/// </summary>
	public static class AchievementTriggers
	{
		public static void Trigger<T>() where T : AE2Achievement
		{
			if (Systems.RegressionChecks.IsRunning) return;
			try
			{
				ModContent.GetInstance<T>()?.Trigger();
			}
			catch
			{
				// 内容还没加载完 / 非游戏上下文 —— 忽略
			}
		}
	}
}
