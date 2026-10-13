using System.Collections.Generic;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 存取审计。
	///
	/// 权限系统只管「能不能动」,出了问题还得知道「谁动了什么」——
	/// 所以服务端在**真正执行**的时候记一条:谁、什么时候、取/存/下单了什么、多少。
	/// 每个网络保留最近 <see cref="NetworkRegistry.AuditLimit"/> 条,存进世界存档,
	/// 在网络概览的「权限设置」页里可以直接看。
	///
	/// 放在 partial 文件里也是为了别再往 MENet.cs 里堆东西。
	/// </summary>
	public static partial class MENet
	{
		/// <summary>记录一次「取出」。服务端调用。</summary>
		private static void AuditWithdraw(Point16 position, int whoAmI, ItemKey key, long amount)
		{
			NetworkRegistry.RecordAudit(AnchorOf(position),
				PlayerNameOf(whoAmI),
				ModText.Get("AuditWithdraw", "取出"),
				ItemLabel(key),
				amount);
		}

		/// <summary>记录一次「存入」。</summary>
		private static void AuditDeposit(Point16 position, int whoAmI, long amount)
		{
			NetworkRegistry.RecordAudit(AnchorOf(position),
				PlayerNameOf(whoAmI),
				ModText.Get("AuditDeposit", "存入"),
				ModText.Get("AuditBatch", "整批"),
				amount);
		}

		/// <summary>记录一次「下单」。</summary>
		private static void AuditOrder(Point16 position, int whoAmI, ItemKey key, long amount)
		{
			NetworkRegistry.RecordAudit(AnchorOf(position),
				PlayerNameOf(whoAmI),
				ModText.Get("AuditOrder", "下单"),
				ItemLabel(key),
				amount);
		}

		private static string PlayerNameOf(int whoAmI)
		{
			if (whoAmI < 0 || whoAmI >= Main.maxPlayers)
			{
				return "?";
			}

			Player player = Main.player[whoAmI];
			return player == null || string.IsNullOrEmpty(player.name) ? "?" : player.name;
		}

		private static string ItemLabel(ItemKey key)
		{
			if (key.IsAir)
			{
				return "?";
			}

			string name = Lang.GetItemNameValue(key.Type);
			return string.IsNullOrWhiteSpace(name) ? "#" + key.Type : name;
		}
	}
}
