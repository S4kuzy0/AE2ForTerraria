using AE2ForTerraria.Content.Achievements;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 「下一个成就还差什么」。
	///
	/// 成就已经有 7 个,但玩家看不到进度 —— 这里挑一个**还没完成**的,
	/// 把它的名字 + 描述(描述本身写清了怎么达成)显示在网络概览的标题栏上。
	/// </summary>
	public static class AchievementHint
	{
		private static AE2Achievement[] All()
		{
			return new AE2Achievement[]
			{
				ModContent.GetInstance<StoreFirstItemAchievement>(),
				ModContent.GetInstance<AutomateFirstItemAchievement>(),
				ModContent.GetInstance<BindTerminalAchievement>(),
				ModContent.GetInstance<KeepStockAchievement>(),
				ModContent.GetInstance<BigStorageAchievement>(),
				ModContent.GetInstance<LongRangeWirelessAchievement>(),
				ModContent.GetInstance<KeepFiveAchievement>(),
				ModContent.GetInstance<TransferNetworkAchievement>(),
				ModContent.GetInstance<FiveAssemblersAchievement>(),
			};
		}

		/// <summary>「已完成 3/9 · 下一个成就:…」——一行里给全进度。</summary>
		public static string Progress()
		{
			try
			{
				AE2Achievement[] all = All();
				int done = 0;

				foreach (AE2Achievement achievement in all)
				{
					if (achievement?.Achievement?.IsCompleted == true)
					{
						done++;
					}
				}

				string count = ModText.Get("LblAchievementCount", "成就") + " "
					+ done + "/" + all.Length;
				string next = Next();

				return string.IsNullOrEmpty(next) ? count : count + "  ·  " + next;
			}
			catch
			{
				return string.Empty;
			}
		}

		/// <summary>返回一句「下一个成就:…」;全做完 / 取不到就返回空串。</summary>
		public static string Next()
		{
			try
			{
				AE2Achievement[] all = All();

				foreach (AE2Achievement achievement in all)
				{
					if (achievement == null || achievement.Achievement?.IsCompleted == true)
					{
						continue;
					}

					string name = achievement.FriendlyName?.Value;
					if (string.IsNullOrWhiteSpace(name))
					{
						continue;
					}

					string description = achievement.Description?.Value;

					return ModText.Get("LblNextAchievement", "下一个成就") + ": " + name
						+ (string.IsNullOrWhiteSpace(description) ? string.Empty : " — " + description);
				}
			}
			catch
			{
				// 内容还没加载完 —— 忽略
			}

			return string.Empty;
		}
	}
}
