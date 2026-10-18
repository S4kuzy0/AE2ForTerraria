using System;
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
	/// 多人同步。
	///
	/// 原则:**服务器权威**。
	/// 客户端只发「请求」(我要存 / 我要取 / 我要下单),
	/// 服务器执行真正改动,再把「快照」广播给所有客户端。
	/// 这样不会出现两边数据打架。
	/// </summary>
	public static partial class MENet

		// 各域的实现已经拆到 MENet.*.cs 的 partial 分片里:
		//   Serialization / Ledger / Crafting / Storage / Network / Server / Client / Audit

	{
        // Explicit envelope allows future protocol versions without exhausting message ID bits.
        public const ushort ProtocolVersion = 9;
        private const byte MessageTag = 3;
        public const byte ClientJobPause = (MessageTag << ProtocolShift) | 47;
        public const byte ServerTerminalStations = (MessageTag << ProtocolShift) | 48;
        private const int ProtocolShift = 6;
        private static ModPacket CreatePacket()
        {
            var packet = Mod.GetPacket(); packet.Write((byte)0); packet.Write(ProtocolVersion); return packet;
        }

		private static bool _loggedProtocolMismatch;


		// —— 客户端 → 服务器 ——
		public const byte ClientRequestLedger = (MessageTag << ProtocolShift) | 1;

		public const byte ClientDepositAll = (MessageTag << ProtocolShift) | 2;

		public const byte ClientWithdraw = (MessageTag << ProtocolShift) | 3;

		public const byte ClientOrder = (MessageTag << ProtocolShift) | 4;

		public const byte ClientCpuRemove = (MessageTag << ProtocolShift) | 5;

		public const byte ClientCpuClear = (MessageTag << ProtocolShift) | 6;

		public const byte ClientStations = (MessageTag << ProtocolShift) | 7;

		public const byte ClientSetTarget = (MessageTag << ProtocolShift) | 8;

		public const byte ClientSetDisks = (MessageTag << ProtocolShift) | 9;

		public const byte ClientCollectorConfig = (MessageTag << ProtocolShift) | 10;

		public const byte ClientUpgradeUnit = (MessageTag << ProtocolShift) | 11;

		public const byte ClientCycleBridge = (MessageTag << ProtocolShift) | 12;

		public const byte ClientSetCollector = (MessageTag << ProtocolShift) | 13;

		public const byte ClientToggleBus = (MessageTag << ProtocolShift) | 14;

		public const byte ClientBusConfig = (MessageTag << ProtocolShift) | 15;

		public const byte ClientCpuPause = (MessageTag << ProtocolShift) | 16;

		public const byte ClientCpuKeep = (MessageTag << ProtocolShift) | 17;

		public const byte ClientRenameNetwork = (MessageTag << ProtocolShift) | 18;

		public const byte ClientStorageFilter = (MessageTag << ProtocolShift) | 19;

		public const byte ClientCpuMove = (MessageTag << ProtocolShift) | 20;

		public const byte ClientExportToChest = (MessageTag << ProtocolShift) | 21;

		public const byte ClientRequestSecurity = (MessageTag << ProtocolShift) | 22;

		public const byte ClientSetPermission = (MessageTag << ProtocolShift) | 23;

		public const byte ClientClaimNetwork = (MessageTag << ProtocolShift) | 24;

		public const byte ClientCpuOutput = (MessageTag << ProtocolShift) | 25;

		public const byte ClientTransferNetwork = (MessageTag << ProtocolShift) | 26;

		public const byte ClientSetLocked = (MessageTag << ProtocolShift) | 27;
		public const byte ClientSetLockedList = (MessageTag << ProtocolShift) | 28;


		// —— 服务器 → 客户端 ——
		public const byte ServerSyncLedger = (MessageTag << ProtocolShift) | 20;

		public const byte ServerSyncCpu = (MessageTag << ProtocolShift) | 21;

		public const byte ServerSyncAssembler = (MessageTag << ProtocolShift) | 22;

		public const byte ServerSyncDrive = (MessageTag << ProtocolShift) | 23;

		public const byte ServerSyncBridge = (MessageTag << ProtocolShift) | 24;

		public const byte ServerSyncCollector = (MessageTag << ProtocolShift) | 25;

		public const byte ServerSyncBus = (MessageTag << ProtocolShift) | 26;

		public const byte ServerSyncAggregate = (MessageTag << ProtocolShift) | 27;

		public const byte ServerSyncNetwork = (MessageTag << ProtocolShift) | 28;

		public const byte ServerSyncBusConfig = (MessageTag << ProtocolShift) | 29;

		public const byte ServerSyncNetworkName = (MessageTag << ProtocolShift) | 30;

		public const byte ServerSyncStorageFilter = (MessageTag << ProtocolShift) | 31;

		public const byte ServerSyncSecurity = (MessageTag << ProtocolShift) | 32;

		public const byte ServerDenied = (MessageTag << ProtocolShift) | 33;
		public const byte ServerNotice = (MessageTag << ProtocolShift) | 34;


		private static Mod Mod => AE2ForTerraria.Instance;


		private static bool IsClient => Main.netMode == NetmodeID.MultiplayerClient;

		private static bool IsServer => Main.netMode == NetmodeID.Server;


		// —— 包处理 ——

		public static void Handle(BinaryReader reader, int whoAmI)
        {
            try
            {
                byte marker = reader.ReadByte();
                if (marker != 0 || reader.ReadUInt16() != ProtocolVersion)
                {
                    if (!_loggedProtocolMismatch) { _loggedProtocolMismatch=true; Mod?.Logger.Warn("ME protocol mismatch; client and server must use the same mod version."); }
                    return;
                }
                HandlePacketCore(reader, whoAmI);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is FormatException || ex is OverflowException)
            { Mod?.Logger.Warn("Rejected malformed ME packet from " + whoAmI + ": " + ex.Message); }
        }
        internal static void HandlePayload(BinaryReader reader, int whoAmI)
        {
            try { HandlePacketCore(reader, whoAmI); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is FormatException || ex is OverflowException)
            { Mod?.Logger.Warn("Rejected malformed ME payload: " + ex.Message); }
        }
        private static void HandlePacketCore(BinaryReader reader, int whoAmI)
		{
			byte message = reader.ReadByte();

			// 协议版本对不上就整包丢弃。以前没有这一层,于是「旧版本收新包」
			// 会静默读错位,表现成空库存或乱数据,极难排查。
			if ((message >> ProtocolShift) != MessageTag)
			{
				if (!_loggedProtocolMismatch)
				{
					_loggedProtocolMismatch = true;
					Mod?.Logger.Warn("收到的 ME 网络包协议版本不一致(收到 v"
						+ (message >> ProtocolShift) + ",本机 v" + ProtocolVersion
						+ "),已忽略。请确认客户端与服务端的模组版本一致。");
				}

				return;
			}

			var position = new Point16(reader.ReadInt16(), reader.ReadInt16());

			if (IsServer)
			{
                if (HandleAccessRequest(message, position, reader, whoAmI)) return;
				HandleOnServer(message, position, reader, whoAmI);
			}
			else if (IsClient)
			{
				HandleOnClient(message, position, reader);
			}
		}


		/// <summary>这一条消息需要什么权限等级。</summary>
		private static int RequiredLevel(byte message)
		{
            if (message == ClientUpgradeStorageUnit) return NetworkRegistry.LevelUse;
			// 只读类:看快照、看权限
			if (message == ClientRequestLedger || message == ClientRequestSecurity)
			{
				return NetworkRegistry.LevelReadOnly;
			}

			// 管理类:改拓扑 / 改配置 / 改权限
			if (message == ClientJobPause || message == ClientSlotAction || message == ClientCpuOutput || message == ClientCpuPause || message == ClientCpuMove || message == ClientSetDisks || message == ClientStations || message == ClientSetTarget
				|| message == ClientCollectorConfig || message == ClientSetCollector
				|| message == ClientCycleBridge || message == ClientToggleBus
				|| message == ClientBusConfig || message == ClientStorageFilter
				|| message == ClientCpuRemove || message == ClientCpuClear
				|| message == ClientCpuKeep || message == ClientRenameNetwork
				|| message == ClientSetPermission || message == ClientSetLocked
				|| message == ClientSetLockedList
				|| message == ClientTransferNetwork)
			{
				return NetworkRegistry.LevelManage;
			}

			// 其余(取出 / 存入 / 下单 / 暂停 / 重排 / 导出 / 升级)都算「使用」
			return NetworkRegistry.LevelUse;
		}


		/// <summary>取这个方块所在网络的稳定锚点(权限 / 命名都挂在它上面)。</summary>
		private static Point16 AnchorOf(Point16 position)
		{
			return MENetwork.CanonicalAnchor(MENetwork.Collect(position.X, position.Y));
		}


		private static void Deny(int whoAmI)
		{
			ModPacket packet = CreatePacket();
			packet.Write(ServerDenied);
			packet.Write((short)0);
			packet.Write((short)0);
			packet.Write("permission");
			packet.Send(whoAmI);
		}


		/// <summary>
		/// 服务端权限收口。
		/// 无主网络对所有人开放,而且**第一个动手的人会自动成为所有者** ——
		/// 这样自己开的服不会出现「自己反而进不去」。
		/// </summary>
		private static bool CheckPermission(byte message, Point16 position, int whoAmI)
		{
			if (!IsServer)
			{
				return true;
			}

			if (whoAmI < 0 || whoAmI >= Main.maxPlayers)
			{
				return false;
			}

			Player player = Main.player[whoAmI];
			if (player == null || !player.active)
			{
				return false;
			}

			int required = RequiredLevel(message);
			if (required <= NetworkRegistry.LevelNone)
			{
				return true;
			}

			Point16 anchor = AnchorOf(position);
			if (anchor.X < 0)
			{
                Deny(whoAmI);
				return false;
			}

			int level = NetworkRegistry.GetLevel(anchor, player.name);
            if (NetworkRegistry.GetOwner(anchor) == null)
            {
                // Knowing a coordinate is not enough to remotely claim an unowned network.
                var center=new Microsoft.Xna.Framework.Vector2(position.X*16f+8f,position.Y*16f+8f);
                if (Microsoft.Xna.Framework.Vector2.DistanceSquared(player.Center,center)>32f*16f*32f*16f
                    && !HasNearbyReadEntrance(position,whoAmI,player)) { Deny(whoAmI); return false; }
                level = NetworkRegistry.LevelManage;
            }

			if (level >= required)
			{
				if (NetworkRegistry.GetOwner(anchor) == null && message != ClientRequestLedger && message != ClientRequestSecurity)
				{
					NetworkRegistry.TryClaim(anchor, player.name);
					SyncSecurity(anchor);
				}

				return true;
			}

			Deny(whoAmI);
			return false;
		}
	}
}
