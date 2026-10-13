using System.Collections.Generic;
using System.IO;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    private const byte ServerCraftingHosts = (MessageTag << ProtocolShift) | 50;
    private static void SendCraftingHosts(Point16 position, int who)
    {
        var cpus = new List<MECraftingCpuEntity>(); var assemblers = new List<MEMolecularAssemblerEntity>();
        foreach (var p in MENetwork.Collect(position.X, position.Y))
        {
            if (!TileEntity.ByPosition.TryGetValue(p, out var entity)) continue;
            if (entity is MECraftingCpuEntity cpu) cpus.Add(cpu);
            else if (entity is MEMolecularAssemblerEntity assembler) assemblers.Add(assembler);
        }
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        writer.Write(ServerCraftingHosts); writer.Write(position.X); writer.Write(position.Y);
        writer.Write(ReadSessions.TryGetValue(who, out var session) && session.Position == position ? session.Epoch : 0);
        writer.Write(cpus.Count);
        foreach (var cpu in cpus) { writer.Write(cpu.Position.X); writer.Write(cpu.Position.Y); writer.Write(cpu is MECraftingTerminalEntity); }
        writer.Write(assemblers.Count);
        foreach (var assembler in assemblers) { writer.Write(assembler.Position.X); writer.Write(assembler.Position.Y); }
        SendAuthorizedPayload(buffer.ToArray(), position, who, -1);
        foreach (var cpu in cpus)
        { SendCpu(cpu.Position, who); if (cpu is MECraftingTerminalEntity) SendTerminalStations(cpu.Position, who); }
        foreach (var assembler in assemblers) SendAssembler(assembler.Position, who);
    }
    private static void ReceiveCraftingHosts(Point16 position, BinaryReader reader)
    {
        int epoch = reader.ReadInt32();
        if (!UISystem.AcceptTerminalSnapshot(position, epoch)) return;
        int count = ReadCount(reader); var cpus = new List<MECraftingCpuEntity>();
        for (int i = 0; i < count; i++)
        {
            var p = new Point16(reader.ReadInt16(), reader.ReadInt16()); bool terminal = reader.ReadBoolean();
            cpus.Add(GetCpuMirror(p, terminal));
        }
        count = ReadCount(reader); var assemblers = new List<MEMolecularAssemblerEntity>();
        for (int i = 0; i < count; i++)
        {
            var p = new Point16(reader.ReadInt16(), reader.ReadInt16());
            assemblers.Add(TileEntity.ByPosition.TryGetValue(p, out var entity) && entity is MEMolecularAssemblerEntity local ? local : UISystem.RemoteAssembler(p));
        }
        UISystem.ApplyCraftingHosts(cpus, assemblers);
    }
    private static MECraftingCpuEntity GetCpuMirror(Point16 position, bool terminal)
    {
        if (TileEntity.ByPosition.TryGetValue(position, out var entity) && entity is MECraftingCpuEntity local
            && (local is MECraftingTerminalEntity) == terminal) return local;
        return terminal ? UISystem.RemoteTerminal(position) : UISystem.RemoteCpu(position);
    }
    private static void ReceiveCpu(Point16 position, BinaryReader reader)
    {
        bool terminal = reader.ReadBoolean(); int count = ReadCount(reader);
        var jobs = new List<MECraftingCpuEntity.Job>();
        for (int i = 0; i < count; i++)
        {
            var key = ReadKey(reader); long remaining = reader.ReadInt64(), total = reader.ReadInt64();
            string requested = reader.ReadString(), id = reader.ReadString();
            bool paused = reader.ReadBoolean(); string status = reader.ReadString();
            if (remaining < 0 || total < 0 || remaining > total || key.IsAir) throw new InvalidDataException("Invalid crafting job");
            jobs.Add(new MECraftingCpuEntity.Job { Key = key, Remaining = remaining, Total = total,
                RequestedBy = string.IsNullOrEmpty(requested) ? null : requested, Id = id, Paused = paused, LastStatus = status });
        }
        string lastStatus = reader.ReadString(); int active = reader.ReadInt32();
        bool allPaused = reader.ReadBoolean(), output = reader.ReadBoolean(); count = ReadCount(reader);
        var keep = new List<MECraftingCpuEntity.KeepEntry>();
        for (int i = 0; i < count; i++)
        {
            var key = ReadKey(reader); long amount = reader.ReadInt64();
            if (key.IsAir || amount <= 0) throw new InvalidDataException("Invalid keep target");
            keep.Add(new MECraftingCpuEntity.KeepEntry { Key = key, Target = amount });
        }
        var cpu = GetCpuMirror(position, terminal);
        cpu.Jobs.Clear(); cpu.Jobs.AddRange(jobs); cpu.Keep.Clear(); cpu.Keep.AddRange(keep);
        cpu.LastStatus = lastStatus; cpu.ActiveJob = active >= 0 && active < jobs.Count ? jobs[active] : null;
        cpu.Paused = allPaused; cpu.OutputToInventory = output;
        if (terminal && UISystem.ActiveCraftingCpu?.Position == position) UISystem.SetRemoteTerminalPosition(position);
    }
    private static void ReceiveTerminalStations(Point16 position, BinaryReader reader)
    {
        int count = ReadCount(reader); var items = new Item[count];
        for (int i = 0; i < count; i++) items[i] = ItemIO.Receive(reader, true, true);
        var terminal = (MECraftingTerminalEntity)GetCpuMirror(position, true);
        terminal.Stations = items; terminal.EnsureStationSlots(); terminal.InvalidateStations();
    }
}
