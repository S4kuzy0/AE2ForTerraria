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
	/// ME 网络协议 —— Storage 域(从 MENet.cs 拆出来的 partial 分片)。
	/// </summary>
	public static partial class MENet
	{
        // A new standalone message leaves every existing v3 packet layout unchanged.
        private const byte ServerSyncStorageUnit = (MessageTag << ProtocolShift) | 37;

        public static void SendStorageUnit(Point16 position, int toClient = -1, int ignoreClient = -1)
        {
            if (!IsServer || !TileEntity.ByPosition.TryGetValue(position, out var entity)
                || entity is not MEStorageUnitEntity unit) return;
            using var buffer = new MemoryStream();
            using var packet = new BinaryWriter(buffer);
            packet.Write(ServerSyncStorageUnit);
            packet.Write(position.X);
            packet.Write(position.Y);
            unit.NetSend(packet);
            SendAuthorizedPayload(buffer.ToArray(), position, toClient, ignoreClient);
        }

		/// <summary>提交驱动器 / 存储单元的分区清单。</summary>
		public static void SendStorageFilter(Point16 position, bool whitelist, List<int> filter)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientStorageFilter);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(whitelist);
			packet.Write(filter?.Count ?? 0);

			if (filter != null)
			{
				foreach (int id in filter)
				{
					packet.Write(id);
				}
			}

			packet.Send();
		}


		/// <summary>服务器广播某个存储方块的分区清单。</summary>
		public static void SyncStorageFilter(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not IPartitionedStorage storage)
			{
				return;
			}

			List<int> filter = storage.Filter ?? new List<int>();

			ModPacket packet = CreatePacket();
			packet.Write(ServerSyncStorageFilter);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(storage.IsWhitelist);
			packet.Write(filter.Count);

			foreach (int id in filter)
			{
				packet.Write(id);
			}

			packet.Send(toClient, ignoreClient);
		}


		/// <summary>请求服务器把某个存储单元升一级(服务器负责扣掉手上的升级件)。</summary>
		public static void UpgradeUnit(Point16 position)
		{
            if (!IsClient) return;
            Player player = Main.LocalPlayer;
            int slot = player.selectedItem;
            int request = BeginStorageUpgrade(player, slot);
            if (request == 0) return;
            // The same vanilla per-slot lock used by quick-stack prevents local edits
            // and permits the server's SyncEquipment response to update this player.
            NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot);
            ModPacket packet = CreatePacket();
            packet.Write(ClientUpgradeStorageUnit);
            packet.Write(position.X); packet.Write(position.Y);
            packet.Write(request); packet.Write(slot);
            packet.Send();
		}


		/// <summary>驱动器磁盘槽位:整份发过去(插/拔/换盘都会走这里)。</summary>
		public static void SendDisks(Point16 position, Item[] disks)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientSetDisks);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(disks.Length);

			foreach (Item disk in disks)
			{
				ItemIO.Send(disk, packet, true, true);
			}

			packet.Send();
		}


		/// <summary>
		/// 单个收集器方块的配置(开关 / 半径 / 黑白名单 / 过滤清单)。
		/// 收集器的设置是**每块独立**的,而且存在方块实体里(服务器权威),
		/// 所以客户端改完必须发给服务器,由服务器写进真正的实体再广播回来。
		/// </summary>
		public static void SendCollectorConfig(Point16 position, bool enabled, float radius, bool blacklist,
			List<int> itemIds)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientSetCollector);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(enabled);
			packet.Write(radius);
			packet.Write(blacklist);
			packet.Write(itemIds?.Count ?? 0);

			if (itemIds != null)
			{
				foreach (int id in itemIds)
				{
					packet.Write(id);
				}
			}

			packet.Send();
		}


		/// <summary>客户端请求切换存储总线的导入 / 导出模式。</summary>
		public static void ToggleBusMode(Point16 position)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientToggleBus);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Send();
		}


		/// <summary>服务器广播存储总线的模式。</summary>
		public static void SyncBus(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEStorageBusEntity bus)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ServerSyncBus);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(bus.ExportMode);
			packet.Send(toClient, ignoreClient);
		}


		/// <summary>客户端提交存储总线的完整配置(模式 + 分区清单)。</summary>
		public static void SendBusConfig(Point16 position, bool exportMode, bool whitelist, List<int> filter)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientBusConfig);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(exportMode);
			packet.Write(whitelist);
			packet.Write(filter?.Count ?? 0);

			if (filter != null)
			{
				foreach (int id in filter)
				{
					packet.Write(id);
				}
			}

			packet.Send();
		}


		/// <summary>服务器把存储总线的完整配置广播出去。</summary>
		public static void SyncBusConfig(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEStorageBusEntity bus)
			{
				return;
			}

			List<int> filter = bus.Filter ?? new List<int>();

			ModPacket packet = CreatePacket();
			packet.Write(ServerSyncBusConfig);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(bus.ExportMode);
			packet.Write(bus.IsWhitelist);
			packet.Write(filter.Count);

			foreach (int id in filter)
			{
				packet.Write(id);
			}

			packet.Send(toClient, ignoreClient);
		}


		/// <summary>服务器把一个收集器的配置广播出去。</summary>
		public static void SyncCollector(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEDropCollectorEntity collector)
			{
				return;
			}

			List<int> ids = collector.Filter ?? new List<int>();

			ModPacket packet = CreatePacket();
			packet.Write(ServerSyncCollector);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(collector.Enabled);
			packet.Write(collector.Radius);
			packet.Write(collector.IsBlacklist);
			packet.Write(ids.Count);

			foreach (int id in ids)
			{
				packet.Write(id);
			}

			packet.Send(toClient, ignoreClient);
		}

	}
}
