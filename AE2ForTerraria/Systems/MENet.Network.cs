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
	/// ME 网络协议 —— Network 域(从 MENet.cs 拆出来的 partial 分片)。
	/// </summary>
	public static partial class MENet
	{

		/// <summary>请求把「来源网络」的东西全部搬进「目标网络」(服务器执行)。</summary>
		public static void TransferNetwork(Point16 from, Point16 to)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientTransferNetwork);
			packet.Write((short)from.X);
			packet.Write((short)from.Y);
			packet.Write((short)to.X);
			packet.Write((short)to.Y);
			packet.Send();
		}


		/// <summary>锁定 / 解锁某一种物品(只有所有者 / 管理者能做)。</summary>
		public static void SetLocked(Point16 anchor, int itemType, bool locked)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientSetLocked);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			packet.Write(itemType);
			packet.Write(locked);
			packet.Send();
		}

		/// <summary>整份提交锁定清单(勾选网格编辑完一次性发过去)。</summary>
		public static bool SetLockedList(Point16 anchor, List<int> types)
		{
			if (!IsClient)
			{
				return false;
			}

			return SendFilterRequest(anchor, packet =>
            {
			packet.Write(ClientSetLockedList);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			WriteFilter(packet, types);

			});
		}


		/// <summary>改网络名字(服务器执行并广播,否则重连就丢)。</summary>
		public static void RenameNetwork(Point16 anchor, string name)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientRenameNetwork);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			packet.Write(name ?? string.Empty);
			packet.Send();
		}


		/// <summary>把一个网络的名字广播给所有客户端。</summary>
		public static void SyncNetworkName(Point16 anchor, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ServerSyncNetworkName);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			packet.Write(NetworkRegistry.GetName(anchor) ?? string.Empty);
			packet.Send(toClient, ignoreClient);
		}


		/// <summary>客户端请求把自己看到的那个桥接器换到下一个频道。</summary>
		public static void CycleBridgeChannel(Point16 position)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientCycleBridge);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Send();
		}


		/// <summary>服务器把一个桥接器的频道广播给所有客户端。</summary>
		public static void SyncBridge(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEWirelessBridgeEntity bridge)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ServerBridgeState);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(bridge.Channel); packet.Write(bridge.Enabled);
			packet.Send(toClient, ignoreClient);
		}


		/// <summary>把一个网络的权限信息推给客户端(不给就广播)。</summary>
		public static void SyncSecurity(Point16 anchor, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer || anchor.X < 0)
			{
				return;
			}

            Point16 canonical = AnchorOf(anchor);
            if (canonical.X >= 0) anchor = canonical;
            byte[] readable = null, revoked = null;
            for (int client = 0; client < Main.maxPlayers; client++)
            {
                if ((toClient >= 0 && client != toClient) || client == ignoreClient
                    || Main.player[client] is not { active: true } player) continue;
                bool canRead = NetworkRegistry.GetLevel(anchor, player.name) >= NetworkRegistry.LevelReadOnly;
                // Former readers also need a reply so their old grants, audit and lock view are cleared.
                byte[] bytes = canRead ? (readable ??= WriteSecuritySnapshot(anchor, true))
                    : (revoked ??= WriteSecuritySnapshot(anchor, false));
                SendPayload(bytes, client, ignoreClient);
            }
            ValidateTerminalSubscriptions();
        }

        private static byte[] WriteSecuritySnapshot(Point16 anchor, bool canRead)
        {
            Dictionary<string, int> acl = canRead ? NetworkRegistry.GetAcl(anchor) : new();
            using var buffer = new MemoryStream();
            using var packet = new BinaryWriter(buffer);
			packet.Write(ServerSyncSecurity);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			packet.Write(NetworkRegistry.GetOwner(anchor) ?? string.Empty);
			packet.Write(acl.Count);

			foreach (KeyValuePair<string, int> pair in acl)
			{
				packet.Write(pair.Key);
				packet.Write(pair.Value);
			}

			// 顺手把审计日志一起发过去(权限页要显示「谁取走了什么」)
			List<string> audit = canRead ? NetworkRegistry.GetAudit(anchor) : new();
			packet.Write(audit.Count);

			foreach (string line in audit)
			{
				packet.Write(line);
			}

			// 物品级锁定清单
			List<int> locked = canRead ? NetworkRegistry.GetLocked(anchor) : new();
			WriteFilter(packet, locked);

			return buffer.ToArray();
		}


		/// <summary>客户端:请服务器把这个网络的权限发过来。</summary>
		public static void RequestSecurity(Point16 anchor)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientRequestSecurity);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			packet.Send();
		}


		/// <summary>客户端:改某个玩家的权限等级。</summary>
		public static void SetPermission(Point16 anchor, string playerName, int level)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientSetPermission);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			packet.Write(playerName ?? string.Empty);
			packet.Write(level);
			packet.Send();
		}


		/// <summary>客户端:认领无主网络。</summary>
		/// <summary>
		/// 服务器给某个客户端发一条提示(toast)。
		/// 用于「转移完成 / 导出完成」这类**客户端自己看不到结果**的操作 ——
		/// 以前它们只在服务器上执行,客户端完全没有反馈。
		/// </summary>
		public static void Notify(int toClient, string text)
		{
			if (!IsServer || toClient < 0 || string.IsNullOrEmpty(text))
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ServerNotice);
			packet.Write((short)0);
			packet.Write((short)0);
			packet.Write(text);
			packet.Send(toClient);
		}

        internal static void NotifyCraftCompleted(string playerName, string text)
        {
            if (string.IsNullOrEmpty(playerName)) return; // Anonymous keep-stock jobs stay quiet.
            if (IsServer)
            {
                foreach (var player in Main.player)
                    if (player?.active == true && player.name == playerName) { Notify(player.whoAmI, text); break; }
            }
            else if (Main.netMode == NetmodeID.SinglePlayer && Main.LocalPlayer?.name == playerName) UIStatus.Toast(text);
        }

		public static void ClaimNetwork(Point16 anchor)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientClaimNetwork);
			packet.Write((short)anchor.X);
			packet.Write((short)anchor.Y);
			packet.Send();
		}

	}
}
