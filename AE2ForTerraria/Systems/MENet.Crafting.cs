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
	/// ME 网络协议 —— Crafting 域(从 MENet.cs 拆出来的 partial 分片)。
	/// </summary>
	public static partial class MENet
	{

		public static void SendCpu(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MECraftingCpuEntity cpu)
			{
				return;
			}

			using var buffer=new MemoryStream();
            using var packet=new BinaryWriter(buffer);
			packet.Write(ServerSyncCpu);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(cpu is MECraftingTerminalEntity);
			packet.Write(cpu.Jobs.Count);

			foreach (MECraftingCpuEntity.Job job in cpu.Jobs)
			{
				WriteKey(packet, job.Key);
				packet.Write(job.Remaining);
				packet.Write(job.Total);
				packet.Write(job.RequestedBy ?? string.Empty);
                packet.Write(job.Id);
                packet.Write(job.Paused);
                packet.Write(job.LastStatus ?? string.Empty);
			}

			// 暂停状态与「保持库存」清单一起同步,否则客户端看不到 / 改不了
			packet.Write(cpu.LastStatus ?? string.Empty);
            packet.Write(cpu.ActiveJob == null ? -1 : cpu.Jobs.IndexOf(cpu.ActiveJob));
            packet.Write(cpu.Paused);
			packet.Write(cpu.OutputToInventory);
			packet.Write(cpu.Keep.Count);

			foreach (MECraftingCpuEntity.KeepEntry entry in cpu.Keep)
			{
				WriteKey(packet, entry.Key);
				packet.Write(entry.Target);
			}

			SendAuthorizedPayload(buffer.ToArray(),position,toClient,ignoreClient);
		}

        public static void SendTerminalStations(Point16 position,int toClient=-1,int ignoreClient=-1)
        {
            if (!IsServer || !TileEntity.ByPosition.TryGetValue(position,out var entity)
                || entity is not MECraftingTerminalEntity terminal) return;
            terminal.EnsureStationSlots();
            using var buffer=new MemoryStream();
            using var packet=new BinaryWriter(buffer);
            packet.Write(ServerTerminalStations);
            packet.Write((short)position.X); packet.Write((short)position.Y);
            packet.Write(terminal.Stations.Length);
            foreach (var station in terminal.Stations) ItemIO.Send(station,packet,true,true);
            SendAuthorizedPayload(buffer.ToArray(),position,toClient,ignoreClient);
        }

        public static void SetJobPaused(Point16 position,string jobId,bool paused)
        {
            if (!IsClient) return;
            ModPacket packet=CreatePacket();
            packet.Write(ClientJobPause);
            packet.Write((short)position.X); packet.Write((short)position.Y);
            packet.Write(jobId ?? string.Empty); packet.Write(paused);
            packet.Send();
        }


		public static void SendAssembler(Point16 position, int toClient = -1, int ignoreClient = -1)
		{
			if (!IsServer
				|| !TileEntity.ByPosition.TryGetValue(position, out TileEntity entity)
				|| entity is not MEMolecularAssemblerEntity assembler)
			{
				return;
			}

			using var buffer=new MemoryStream();
            using var packet=new BinaryWriter(buffer);
			packet.Write(ServerSyncAssembler);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(assembler.Stations.Length);

			foreach (Item station in assembler.Stations)
			{
				ItemIO.Send(station, packet, true, true);
			}

			// 逐件合成的进度也要带过去,否则客户端只能看到一个不动的状态栏
			WriteKey(packet, assembler.ActiveKey);
			packet.Write(assembler.ProgressTicks);
			packet.Write(assembler.Parallel);
            WriteKey(packet,assembler.Target);
            packet.Write(assembler.LastStatus ?? string.Empty);

			SendAuthorizedPayload(buffer.ToArray(),position,toClient,ignoreClient);
		}


		public static void Order(Point16 position, ItemKey key, long amount)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientOrder);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			WriteKey(packet, key);
			packet.Write(amount);
			packet.Send();
		}


		public static void CpuRemove(Point16 position, string jobId)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientCpuRemove);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(jobId ?? string.Empty);
			packet.Send();
		}


		public static void CpuClear(Point16 position)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientCpuClear);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Send();
		}


		/// <summary>调整队列顺序(delta = -1 上移,+1 下移,-999 置顶)。</summary>
		public static void CpuMove(Point16 position, string jobId, int delta)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientCpuMove);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(jobId ?? string.Empty);
			packet.Write(delta);
			packet.Send();
		}


		/// <summary>切换「产出到玩家背包」。</summary>
		public static void CpuSetOutput(Point16 position, bool toInventory)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientCpuOutput);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(toInventory);
			packet.Send();
		}


		/// <summary>切换合成队列的暂停状态(服务器执行)。</summary>
		public static void CpuPause(Point16 position)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientCpuPause);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Send();
		}


		/// <summary>设置 / 移除「保持库存」。target &lt;= 0 表示移除。</summary>
		public static void CpuKeep(Point16 position, ItemKey key, long target)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientCpuKeep);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			WriteKey(packet, key);
			packet.Write(target);
			packet.Send();
		}


		/// <summary>装配室工作站槽位:整份发过去,省得处理增删两种情形的边界。</summary>
		public static void SendStations(Point16 position, Item[] stations)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientStations);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			packet.Write(stations.Length);

			foreach (Item station in stations)
			{
				ItemIO.Send(station, packet, true, true);
			}

			packet.Send();
		}


		public static void SetTarget(Point16 position, ItemKey key)
		{
			if (!IsClient)
			{
				return;
			}

			ModPacket packet = CreatePacket();
			packet.Write(ClientSetTarget);
			packet.Write((short)position.X);
			packet.Write((short)position.Y);
			WriteKey(packet, key);
			packet.Send();
		}

	}
}
