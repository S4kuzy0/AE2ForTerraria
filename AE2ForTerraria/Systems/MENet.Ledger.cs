using System.Collections.Generic;
using System.IO;
using AE2ForTerraria.Content.Achievements;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AE2ForTerraria.UI;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// ME 网络协议 —— Ledger 域(从 MENet.cs 拆出来的 partial 分片)。
	/// </summary>
	public static partial class MENet
	{

		/// <summary>
		/// 「谁正在看哪个网络」。
		/// 以前任何人的存取都会广播给所有客户端,哪怕他们根本没打开那个网络;
		/// 而且每个存储方块单独发一个包(10 个驱动器 = 10 个包)。
		/// 现在按订阅者定向发,并且一个网络合并成**单包**。
		/// </summary>
		private static readonly Dictionary<int, Point16> Subscribers = new Dictionary<int, Point16>();


		// 写入 / 读取工具(WriteKey / ReadKey / WriteLedger / ReadLedgerInto / SkipLedger)
		// 已经挪到 MENet.Serialization.cs —— 协议格式的改动集中在那一个文件里。

		/// <summary>单个存储方块(驱动器 / 存储单元)。</summary>
		public static void SendLedger(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer)
			{
				return;
			}

			if (!TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not IMEStorage storage)
			{
				return;
			}

            SendInventoryPages(position, storage.Ledger.Entries, false, storage.Ledger.MaxTypes, storage.Ledger.MaxBytes,
                storage.Ledger.MaxAmountPerType, toClient, ignoreClient);
		}


		/// <summary>整个网络里所有存储方块(一次操作可能碰到多个)。</summary>
		public static void SendNetworkLedgers(int x, int y, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer)
			{
				return;
			}

			List<Point16> blocks = MENetwork.Collect(x, y);
			if (blocks.Count == 0)
			{
				return;
			}

			if (toClient >= 0)
			{
				SendNetworkSnapshot(blocks, toClient, ignoreClient);
				return;
			}

			// 只发给正在看这个网络的客户端
			var inNetwork = new HashSet<Point16>(blocks);
			var targets = new List<int>();

			foreach (KeyValuePair<int, Point16> pair in Subscribers)
			{
				if (pair.Key < 0 || pair.Key >= Main.maxPlayers)
				{
					continue;
				}

				// 玩家掉线后订阅还留着,发之前确认一下
				if (!Main.player[pair.Key].active)
				{
					continue;
				}

				if (inNetwork.Contains(pair.Value))
				{
					targets.Add(pair.Key);
				}
			}

			foreach (int client in targets)
			{
				if (NetworkRegistry.GetLevel(MENetwork.CanonicalAnchor(blocks), Main.player[client].name) < NetworkRegistry.LevelReadOnly) continue;
                SendNetworkSnapshot(blocks, client, ignoreClient);
                SendAggregate(Subscribers[client], client);
			}
		}


		/// <summary>把整个网络的所有账本压进**一个**包发出去。</summary>
		private static void SendNetworkSnapshot(List<Point16> blocks, int toClient, int ignoreClient)
		{
            foreach (var position in blocks)
                if (TileEntity.ByPosition.TryGetValue(position, out var entity) && entity is IMEStorage)
                    SendLedger(position, toClient, ignoreClient);
		}


		/// <summary>
		/// 把整张网络的**汇总内容**发给客户端。
		///
		/// 为什么需要这个:用远程访问终端时,玩家人在远处,
		/// 客户端本地根本没有那些驱动器 / 存储单元的方块实体,
		/// 于是单个账本快照会被 HandleOnClient 开头的 TryGetValue 直接丢掉,
		/// 界面里库存永远是空的。这里发的是「网络总计」,不依赖本地实体是否存在。
		/// </summary>
		public static void SendAggregate(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer)
			{
				return;
			}

			Dictionary<ItemKey, long> totals =
				MENetwork.Aggregate(MENetwork.CollectStorage(position.X, position.Y));

            for (int client = 0; client < Main.maxPlayers; client++)
            {
                if ((toClient >= 0 && client != toClient) || client == ignoreClient || Main.player[client] is not { active: true }) continue;
                int level = EffectiveReadLevel(position, Main.player[client]);
                if (level < NetworkRegistry.LevelReadOnly) continue;
                int epoch = ReadSessions.TryGetValue(client, out var session) && session.Position == position ? session.Epoch : 0;
                SendTerminalPolicy(position, epoch, client);
                SendInventoryPages(position, totals, true, long.MaxValue, long.MaxValue, long.MaxValue, client, ignoreClient, epoch, level);
                SendCraftingHosts(position, client);
            }
		}


		// —— 客户端请求 ——

		public static void RequestLedger(Point16 position)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientRequestLedger);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Send();
		}


		public static void DepositAll(Point16 position, bool includeAmmoAndCoins = false)
        {
            SubmitInventoryDeposit(position, includeAmmoAndCoins);
        }

        public static void Withdraw(Point16 position, ItemKey key, long amount, bool exactStorage=false, bool dropOnGround=false)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientWithdraw);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			WriteKey(packet, key);
			packet.Write(amount);
            packet.Write(exactStorage); packet.Write(dropOnGround);
			packet.Send();
		}


		/// <summary>请求把整个网络导出到导出模式总线连着的箱子(服务器执行)。</summary>
		public static void ExportToChest(Point16 position)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientExportToChest);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Send();
		}


		public static void SendDriveSnapshot(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEDiskDriveEntity drive)
			{
				return;
			}

			using var buffer=new MemoryStream();
            using var packet=new BinaryWriter(buffer);
			packet.Write(ServerSyncDrive);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(drive.Disks.Length);

			foreach (Item disk in drive.Disks)
			{
				ItemIO.Send(disk, packet, true, true);
			}

			SendAuthorizedPayload(buffer.ToArray(),position,toClient,ignoreClient);
		}

	}
}
