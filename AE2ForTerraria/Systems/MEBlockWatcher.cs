using Terraria;
using Terraria.Chat;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// ME 方块增删时清掉连通性缓存。
	/// 以前 MENetwork.InvalidateCache() 没有任何调用者,
	/// 导致刚接上的新方块最多 30 tick 内"还没连上"。
	/// 用 GlobalTile 就一处搞定,不用改 7 个方块类。
	/// </summary>
	public sealed class MEBlockWatcher : GlobalTile
	{
		private static int _lastNetworkCount = -1;
        private static bool _pending;

		public override void PlaceInWorld(int i, int j, int type, Item item)
		{
			if (IsMEBlockType(type))
			{
				MENetwork.InvalidateCache();
				_pending = true;
			}
		}

		public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
		{
			if (!fail && !effectOnly && IsMEBlockType(type))
			{
				MENetwork.InvalidateCache();
				_pending = true;
			}
		}

		/// <summary>
		/// 方块增删之后看看「世界里有几个网络」变了没有:
		/// 拆掉一块驱动器 / 线缆可能把一个网络切成两个,接上桥接器又会合并。
		/// 玩家以前只会发现「东西突然不见了」(其实在另一个网络里) —— 现在会收到提示。
		/// </summary>
		public static void FlushChanges()
        {
            if (!_pending) return;
            _pending=false;
            NetworkRegistry.PruneRemovedBlocks();
            MENetwork.InvalidateCache();
            CheckNetworkCountChanged();
        }
        private static void CheckNetworkCountChanged()
		{
			if (Main.gameMenu)
			{
				return;
			}

			int now = MENetwork.CollectAllNetworks().Count;

			if (_lastNetworkCount < 0)
			{
				_lastNetworkCount = now;
				return;
			}

			if (now == _lastNetworkCount)
			{
				return;
			}

			bool split = now > _lastNetworkCount;
			_lastNetworkCount = now;

			string text = split
				? ModText.Format("MsgNetworkSplit",
					"ME 网络被切断了 —— 现在世界上有 {0} 个网络,东西可能留在了另一个网络里", now)
				: ModText.Format("MsgNetworkMerged",
					"ME 网络已合并 —— 现在世界上有 {0} 个网络", now);

			var color = new Microsoft.Xna.Framework.Color(255, 210, 140);

			if (Main.netMode == Terraria.ID.NetmodeID.Server)
			{
				ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), color);
			}
			else
			{
				UIStatus.Toast(text, color);
			}
		}

		/// <summary>世界切换 / 重载时把基准值清掉,免得拿上一个世界的数字来比。</summary>
		public static void ResetBaseline()
		{
			_lastNetworkCount = -1;
            _pending=false;
		}

		private static bool IsMEBlockType(int tileType)
		{
			// 用共享表:以前这里少列了线缆和无线桥接器,
			// 铺/拆线缆不会清缓存,出现「刚铺好却还没连上」。
			return MEBlocks.IsMEBlockType(tileType);
		}
	}
}
