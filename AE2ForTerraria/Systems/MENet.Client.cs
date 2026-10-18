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
	/// ME 网络协议 —— Client 域(从 MENet.cs 拆出来的 partial 分片)。
	/// </summary>
	public static partial class MENet
	{

		private static void HandleOnClient(byte message, Point16 position, BinaryReader reader)
		{
			if (HandleAccessResponse(message, position, reader)) return;
            if (message == ServerInventoryPage) { ReceiveInventoryPage(position, reader); return; }
            if (message == ServerCraftingHosts) { ReceiveCraftingHosts(position, reader); return; }
            if (message == ServerTerminalStations) { ReceiveTerminalStations(position, reader); return; }
            if (message == ServerSyncCpu) { ReceiveCpu(position, reader); return; }
            if (message == ServerFragment) { ReceiveFragment(reader); return; }
            if (message == ServerInventoryDepositComplete)
            {
                CompleteInventoryDeposit(reader.ReadInt32());
                return;
            }
            if (message == ServerStorageUpgradeComplete)
            {
                CompleteStorageUpgrade(reader.ReadInt32());
                return;
            }
            // 网络汇总不依赖本地方块实体,所以在 TryGetValue 之前就要处理 ——
			// 远程终端正是「本地没有那些实体」的情况,以前会被直接丢掉。
			if (message == ServerSyncAggregate)
			{
				var cpuPos=new Point16(reader.ReadInt16(),reader.ReadInt16());
                var assemblerPos=new Point16(reader.ReadInt16(),reader.ReadInt16());
                if (position==UISystem.ActiveTerminalPos) UISystem.SetRemoteMachinePositions(cpuPos,assemblerPos);
                int total = ReadCount(reader);
                var contents = new Dictionary<ItemKey, long>(total);

				for (int i = 0; i < total; i++)
				{
					ItemKey key = ReadKey(reader);
					long amount = reader.ReadInt64();
					if (!key.IsAir && amount > 0)
					{
						contents[key] = contents.TryGetValue(key, out long current)
							? StorageAmounts.Add(current, amount)
							: amount;
					}
				}

				UISystem.SetRemoteContents(position, contents);
				return;
			}

			// 整网快照:一个包带着网络里所有存储方块的账本。
			// 必须在「找本地方块实体」之前处理 —— 用远程终端时本地根本没有它们。
			if (message == ServerSyncNetwork)
			{
				int total = reader.ReadInt32();

				for (int i = 0; i < total; i++)
				{
					var blockPosition = new Point16(reader.ReadInt16(), reader.ReadInt16());

					if (TileEntity.ByPosition.TryGetValue(blockPosition, out TileEntity block)
						&& block is IMEStorage storage)
					{
						ReadLedgerInto(reader, storage.Ledger);
					}
					else
					{
						// 本地没这个实体也要把字节读掉,否则后面的数据全部错位
						SkipLedger(reader);
					}
				}

				UISystem.RefreshNetwork();
				return;
			}

			// 网络改名:不依赖本地方块实体(远程/远处也要能看到)
			if (message == ServerSyncNetworkName)
			{
				string name = reader.ReadString();
				NetworkRegistry.SetName(position, name);
				UISystem.NetworkOverviewState?.RefreshNetworks();
				return;
			}

			// 网络权限:同样不依赖本地方块实体
			if (message == ServerSyncSecurity)
			{
				string owner = reader.ReadString();
				int count = ReadCount(reader);
				var table = new Dictionary<string, int>();

				for (int i = 0; i < count; i++)
				{
					string playerName = reader.ReadString();
					int level = reader.ReadInt32();
                    if (level < NetworkRegistry.LevelNone || level > NetworkRegistry.LevelManage)
                        throw new InvalidDataException("Invalid network permission level");

					if (!string.IsNullOrEmpty(playerName))
					{
						table[playerName] = level;
					}
				}

				int auditCount = ReadCount(reader);
                if (auditCount > NetworkRegistry.AuditLimit) throw new InvalidDataException("Invalid audit length");
				var audit = new List<string>();

				for (int i = 0; i < auditCount; i++)
				{
					audit.Add(reader.ReadString());
				}

				int lockedCount = ReadCount(reader);
				var locked = new List<int>();

				for (int i = 0; i < lockedCount; i++)
				{
                    int type = reader.ReadInt32();
                    if (!SavedItemGuard.IsValidFilterEntry(type)) throw new InvalidDataException("Invalid locked item identity");
					locked.Add(type);
				}

                // Publish the policy only after all sections have decoded successfully.
				NetworkRegistry.ApplyRemoteSecurity(position, owner, table);
				NetworkRegistry.ApplyRemoteAudit(position, audit);
				NetworkRegistry.ApplyRemoteLocked(position, locked);
				UISystem.NetworkOverviewState?.RefreshNetworks();
				return;
			}

			// 服务器拒绝:给玩家一条提示
			if (message == ServerDenied)
			{
				reader.ReadString(); // 原因键(目前只有 permission)
                UIStatus.Toast(ModText.Get("MsgNoPermission",
					"你没有这个 ME 网络的权限(找所有者要权限)"),
					new Microsoft.Xna.Framework.Color(255, 170, 150));
				return;
			}

			// 服务器发来的普通提示(转移完成 / 导出完成…)
			if (message == ServerNotice)
			{
				UIStatus.Toast(reader.ReadString());
				return;
			}

            if (message == ServerCursor)
            {
                ReceiveCursor(reader);
                return;
            }
			TileEntity.ByPosition.TryGetValue(position,out TileEntity entity);
            if (entity==null && message==ServerSyncCpu) entity=UISystem.RemoteCpu(position);
            if (entity==null && message==ServerSyncAssembler) entity=UISystem.RemoteAssembler(position);
            if (entity==null) return;

			switch (message)
            {
                case ServerSyncStorageUnit:
                    if (entity is MEStorageUnitEntity unit) unit.NetReceive(reader);
                    break;

				case ServerSyncLedger:
					if (entity is IMEStorage storage)
					{
						ReadLedgerInto(reader, storage.Ledger);
						UISystem.RefreshNetwork();
					}

					break;


				case ServerSyncAssembler:
					if (entity is MEMolecularAssemblerEntity assembler)
					{
						int count = reader.ReadInt32();
						for (int i = 0; i < count && i < assembler.Stations.Length; i++)
						{
							assembler.Stations[i] = ItemIO.Receive(reader, true, true);
						}

						assembler.ActiveKey = ReadKey(reader);
						assembler.ProgressTicks = reader.ReadSingle();
						assembler.Parallel = reader.ReadInt32();
                        assembler.Target=ReadKey(reader);
                        assembler.LastStatus=reader.ReadString();
					}

					break;

				case ServerSyncDrive:
					if (entity is MEDiskDriveEntity drive)
					{
						int count = reader.ReadInt32();
						for (int i = 0; i < count && i < drive.Disks.Length; i++)
						{
							drive.Disks[i] = ItemIO.Receive(reader, true, true);
						}

						drive.RefreshCapacity();
					}

					break;

				case ServerSyncBridge:
					if (entity is MEWirelessBridgeEntity bridge)
					{
						bridge.SetChannel(reader.ReadInt32());
						MENetwork.InvalidateCache();
					}

					break;

				case ServerSyncCollector:
					if (entity is MEDropCollectorEntity collector)
					{
                        ReadCollectorConfig(reader, out bool enabled, out float radius, out bool blacklist, out var ids);
                        collector.Enabled = enabled;
                        collector.Radius = radius;
                        collector.IsBlacklist = blacklist;
						collector.Filter = ids;
						collector.MarkFilterDirty();
					}

					break;

				case ServerSyncBus:
					if (entity is MEStorageBusEntity bus)
					{
						bus.SetExportMode(reader.ReadBoolean());
					}

					break;

				case ServerSyncBusConfig:
					if (entity is MEStorageBusEntity busCfg)
					{
						var ids = new List<int>();
						busCfg.SetExportMode(reader.ReadBoolean());
						busCfg.IsWhitelist = reader.ReadBoolean();
						int count = reader.ReadInt32();

						for (int i = 0; i < count; i++)
						{
							int id = reader.ReadInt32();
							if (id > 0)
							{
								ids.Add(id);
							}
						}

						busCfg.Filter = ids;
						busCfg.MarkFilterDirty();
					}

					break;

				case ServerSyncStorageFilter:
					if (entity is IPartitionedStorage partitioned)
					{
						bool whitelist = reader.ReadBoolean();
						int count = reader.ReadInt32();
						var ids = new List<int>();

						for (int i = 0; i < count; i++)
						{
							int id = reader.ReadInt32();

							if (SavedItemGuard.IsValidFilterEntry(id))
							{
								ids.Add(id);
							}
						}

						partitioned.Filter = ids;
						partitioned.IsWhitelist = whitelist;
						partitioned.MarkFilterDirty();
					}

					break;
			}
		}

	}
}
