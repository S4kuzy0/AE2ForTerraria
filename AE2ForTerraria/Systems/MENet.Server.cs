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
	/// ME 网络协议 —— Server 域(从 MENet.cs 拆出来的 partial 分片)。
	/// </summary>
	public static partial class MENet
	{

		private static void HandleOnServer(byte message, Point16 position, BinaryReader reader, int whoAmI)
		{
			if (message==ClientPlayerState) { ReceivePlayerState(reader,whoAmI); return; }
            if (message==ClientDepositCursor || message==ClientSlotAction) { HandleCursorRequest(message,position,reader,whoAmI); return; }
            if (message==ClientUpgradeStorageUnit) { HandleStorageUpgrade(position,reader,whoAmI); return; }
            if (message==ClientInventoryDeposit) { HandleInventoryDeposit(position,reader,whoAmI); return; }
            if (message==ClientDepositAll || message==ClientDepositScope) return; // Retired: no captured slots or completion identity.
            if (message==ClientJobPause)
            {
                if (!CheckPermission(message,position,whoAmI)) return;
                string jobId=reader.ReadString(); bool paused=reader.ReadBoolean();
                if (jobId.Length<=64 && TileEntity.ByPosition.TryGetValue(position,out var entity)
                    && entity is MECraftingCpuEntity cpu && cpu.FindJob(jobId) is var job && job!=null)
                { job.Paused=paused; job.LastStatus=paused ? "已暂停" : "等待处理"; SendCpu(position); }
                return;
            }
            if ((message & 63)<1 || (message & 63)>30) return;
            // —— 权限收口 ——
			// 所有会改动 / 取出网络的请求都在这里统一判一次权限,
			// 免得以后加消息忘了判(以前任何人走到终端前都能把整个网络搬空)。
			if (!CheckPermission(message, position, whoAmI))
			{
				return;
			}

			switch (message)
			{
				case ClientRequestLedger:
					// 记下「这个人在看这个网络」,以后只给他发
					Subscribers[whoAmI] = position;

					SendNetworkLedgers(position.X, position.Y, whoAmI);
					// 同一个位置可能还有 CPU / 装配室,一并同步过去
					SendCpu(position, whoAmI);
					SendAssembler(position, whoAmI);
                    SendTerminalStations(position, whoAmI);
					SendDriveSnapshot(position, whoAmI);
                    SendStorageUnit(position, whoAmI);
                    SyncStorageFilter(position,whoAmI);
                    SyncBusConfig(position,whoAmI);
                    SyncSecurity(AnchorOf(position),whoAmI);
					SyncBridge(position, whoAmI);
					SyncCollector(position, whoAmI);
					// 远程终端用:不依赖客户端本地有没有那些方块实体
					SendAggregate(position, whoAmI);
					break;

                case ClientWithdraw:
				{
					ItemKey key = ReadKey(reader);
					long amount = reader.ReadInt64();
                    bool exactStorage=reader.ReadBoolean(), dropOnGround=reader.ReadBoolean();

					// 物品级锁定:锁着的东西只有所有者 / 管理者能取
					Point16 anchorForLock = AnchorOf(position);
					Player requester = Main.player[whoAmI];

					if (NetworkRegistry.IsLocked(anchorForLock, key.Type)
						&& (requester == null
							|| NetworkRegistry.GetLevel(anchorForLock, requester.name)
								< NetworkRegistry.LevelManage))
					{
						Deny(whoAmI);
						break;
					}

                    var network = MENetwork.CollectStorage(position.X,position.Y);
                    if (exactStorage)
                    {
                        network = new List<IMEStorage>();
                        if (TileEntity.ByPosition.TryGetValue(position,out var entity) && entity is IMEStorage storage) network.Add(storage);
                    }
                    long taken = StorageOperations.Withdraw(network,key,amount,requester,dropOnGround);
                    Notify(whoAmI,"已取出 " + taken + " 件；空间不足时余量仍在网络中");

					SendNetworkLedgers(position.X, position.Y);
					AuditWithdraw(position, whoAmI, key, taken);
					break;
				}

                case ClientOrder:
                {
                    ItemKey key = ReadKey(reader); long amount = reader.ReadInt64();
                    if (!SavedItemGuard.IsValidType(key.Type) || key.Prefix != 0 || key.Data != null || amount <= 0) break;
                    var cpu = MENetwork.FindCraftingCpu(position.X,position.Y);
                    var assembler = MENetwork.FindAssembler(position.X,position.Y);
                    if (cpu != null)
                    {
                        bool added = TerminalCraftingExecutor.Submit(cpu,key,amount,Main.player[whoAmI].name,out string result);
                        if (added) AuditOrder(position,whoAmI,key,amount);
                        Notify(whoAmI,result);
                    }
                    else if (assembler != null && amount == 1)
                    { assembler.Target=key; SendAssembler(assembler.Position); Notify(whoAmI,"已设定装配目标"); }
                    else Notify(whoAmI,"需要连接合成终端");
                    break;
                }
				case ClientCpuRemove:
				{
					string jobId = reader.ReadString();
					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MECraftingCpuEntity cpu
						&& jobId.Length<=64)
					{
						cpu.DropJob(cpu.FindJob(jobId));
						SendCpu(position);
					}

					break;
				}

				case ClientCpuClear:
					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity e2)
						&& e2 is MECraftingCpuEntity cpu2)
					{
						cpu2.ClearQueue();
						SendCpu(position);
					}

					break;

                case ClientStations: return; // Retired unsafe snapshot upload. Use ClientSlotAction.

				case ClientSetTarget:
				{
					ItemKey key = ReadKey(reader);
					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MEMolecularAssemblerEntity assembler)
					{
						assembler.Target = key.IsAir ? ItemKey.None : ItemKey.Crafted(key.Type);
						SendAssembler(position);
					}

					break;
				}

                case ClientSetDisks: return; // Retired unsafe snapshot upload. Use ClientSlotAction.

				case ClientUpgradeUnit:
                    return; // Retired: lacks a captured slot and completion identity.

				case ClientCollectorConfig:
				{
					// 兼容老包:全局配置那套已经不用了,但还是把内容读掉,免得残留字节把后续解析带歪
					reader.ReadBoolean();
					reader.ReadSingle();
					reader.ReadBoolean();
					int legacyCount = ReadCount(reader);

					for (int i = 0; i < legacyCount; i++)
					{
						reader.ReadInt32();
					}

					break;
				}

				case ClientSetCollector:
				{
					bool enabled = reader.ReadBoolean();
					float radius = reader.ReadSingle();
					bool blacklist = reader.ReadBoolean();
					int count = ReadCount(reader);

					var ids = new List<int>();
					for (int i = 0; i < count; i++)
					{
						int id = reader.ReadInt32();
						if (id > 0)
						{
							ids.Add(id);
						}
					}

					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MEDropCollectorEntity collector)
					{
						collector.Enabled = enabled;
						collector.Radius = float.IsFinite(radius) ? System.Math.Clamp(radius, 1f, 80f) : 1f;
						collector.IsBlacklist = blacklist;
						collector.Filter = ids;
						collector.MarkFilterDirty();
						SyncCollector(position);
					}

					break;
				}

				case ClientCycleBridge:
				{
					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MEWirelessBridgeEntity bridge)
					{
						bridge.CycleChannel();
						MENetwork.InvalidateCache();
						SyncBridge(position);
					}

					break;
				}

				case ClientToggleBus:
				{
					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MEStorageBusEntity bus)
					{
						bus.ToggleMode();
						SyncBus(position);
						SyncBusConfig(position);
					}

					break;
				}

				case ClientBusConfig:
				{
					bool exportMode = reader.ReadBoolean();
					bool whitelist = reader.ReadBoolean();
					int count = ReadCount(reader);

					var ids = new List<int>();
					for (int i = 0; i < count; i++)
					{
						int id = reader.ReadInt32();
						if (id > 0)
						{
							ids.Add(id);
						}
					}

					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MEStorageBusEntity bus)
					{
						bus.SetExportMode(exportMode);
						bus.IsWhitelist = whitelist;
						bus.Filter = ids;
					bus.MarkFilterDirty();
						SyncBusConfig(position);
					}

					break;
				}

				case ClientCpuPause:
				{
					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MECraftingCpuEntity cpu)
					{
						cpu.Paused = !cpu.Paused;
						SendCpu(position);
					}

					break;
				}

				case ClientCpuKeep:
				{
					ItemKey key = ReadKey(reader);
					long target = reader.ReadInt64();

					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MECraftingCpuEntity cpu
						&& cpu.SetKeep(key, target))
					{
						SendCpu(position);
					}

					break;
				}

				case ClientRenameNetwork:
				{
					string name = reader.ReadString();

					// 名字长度给个上限,免得有人塞一整本书进存档
					if (name != null && name.Length > 48)
					{
						name = name.Substring(0, 48);
					}

					NetworkRegistry.SetName(AnchorOf(position), name);
					SyncNetworkName(position);
					break;
				}

				case ClientStorageFilter:
				{
					bool whitelist = reader.ReadBoolean();
					int count = ReadCount(reader);
					var ids = new List<int>();

					for (int i = 0; i < count; i++)
					{
						int id = reader.ReadInt32();

						if (SavedItemGuard.IsValidFilterEntry(id))
						{
							ids.Add(id);
						}
					}

					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is IPartitionedStorage storage)
					{
						storage.Filter = ids;
						storage.IsWhitelist = whitelist;
						storage.MarkFilterDirty();
						SyncStorageFilter(position);
					}

					break;
				}

				case ClientCpuMove:
				{
					string jobId = reader.ReadString();
					int delta = reader.ReadInt32();

					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MECraftingCpuEntity cpu
						&& jobId.Length<=64)
					{
						if (cpu.MoveJob(cpu.FindJob(jobId), delta))
						{
							SendCpu(position);
						}
					}

					break;
				}

				case ClientExportToChest:
				{
					// 服务器执行,然后广播账本做同步
					// 注意:必须把发起人传进去 —— 导出同样不能绕过物品锁定
					Player exporter = Main.player[whoAmI];
					long exported = MEStorageBusEntity.ExportNetworkToChests(
						position.X, position.Y, exporter?.name);
					SendNetworkLedgers(position.X, position.Y);

					Notify(whoAmI, exported > 0
						? ModText.Format("MsgExported", "已导出 {0} 件物品到箱子", exported)
						: ModText.Get("MsgExportNothing",
							"没有可导出的箱子(需要有导出模式的存储总线贴着箱子)"));
					break;
				}

				case ClientRequestSecurity:
					SyncSecurity(position, whoAmI);
					break;

				case ClientSetLocked:
				{
					int itemType = reader.ReadInt32();
					bool locked = reader.ReadBoolean();

					if (SavedItemGuard.IsValidFilterEntry(itemType))
					{
						NetworkRegistry.SetLocked(AnchorOf(position), itemType, locked);
						SyncSecurity(position);
					}

					break;
				}

				case ClientSetLockedList:
				{
					int count = ReadCount(reader);
					var types = new List<int>();

					for (int i = 0; i < count; i++)
					{
						int type = reader.ReadInt32();

						if (SavedItemGuard.IsValidFilterEntry(type))
						{
							types.Add(type);
						}
					}

					NetworkRegistry.SetLockedList(AnchorOf(position), types);
					SyncSecurity(position);
					break;
				}

				case ClientSetPermission:
				{
					string playerName = reader.ReadString();
					int level = reader.ReadInt32();

					// 只有所有者 / 管理者能改(CheckPermission 已经按 LevelManage 拦过一次)
					NetworkRegistry.SetLevel(AnchorOf(position), playerName, System.Math.Clamp(level, 0, 3));
					SyncSecurity(position);
					break;
				}

				case ClientClaimNetwork:
				{
					Player player = Main.player[whoAmI];

					if (player != null && player.active
						&& NetworkRegistry.TryClaim(AnchorOf(position), player.name))
					{
						SyncSecurity(position);
					}

					break;
				}

				case ClientCpuOutput:
				{
					bool toInventory = reader.ReadBoolean();

					if (TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
						&& entity is MECraftingCpuEntity cpu)
					{
						cpu.OutputToInventory = toInventory;
						SendCpu(position);
					}

					break;
				}

				case ClientTransferNetwork:
				{
					// position = 来源网络,第二个坐标在包里
					var target = new Point16(reader.ReadInt16(), reader.ReadInt16());

					// 目标网络也要有管理权限(来源那个已经被 CheckPermission 拦过了)
					Player player = Main.player[whoAmI];
					Point16 targetAnchor = AnchorOf(target);

					if (player == null || !player.active
						|| NetworkRegistry.GetLevel(targetAnchor, player.name)
							< NetworkRegistry.LevelManage)
					{
						Deny(whoAmI);
						break;
					}

					long moved = MENetwork.TransferBetweenNetworks(
						position.X, position.Y, target.X, target.Y);

					NetworkRegistry.RecordAudit(AnchorOf(position), player.name,
						ModText.Get("AuditTransfer", "网络转移"), ModText.Get("AuditBatch", "整批"), moved);

					SendNetworkLedgers(position.X, position.Y);
					SendNetworkLedgers(target.X, target.Y);

					if (moved > 0)
					{
						AchievementTriggers.Trigger<TransferNetworkAchievement>();
					}

					Notify(whoAmI, ModText.Format("MsgTransferred", "已转移 {0} 件物品", moved));

					break;
				}
			}
		}

	}
}
