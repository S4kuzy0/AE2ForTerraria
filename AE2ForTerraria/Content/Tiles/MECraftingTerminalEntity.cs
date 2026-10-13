using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Content.Tiles;

/// <summary>Durable terminal queue and station inventory. Legacy CPUs remain queue hosts.</summary>
public sealed class MECraftingTerminalEntity : MECraftingCpuEntity
{
    public Item[] Stations = { new Item() };
    private readonly TerminalCraftingExecutor _executor = new();
    private readonly HashSet<int> _conditionErrors = new();

    public override bool IsTileValidForEntity(int x, int y)
        => MachineTiles.IsEntityAnchor(x,y,ModContent.TileType<MECraftingTerminalTile>());

    public override int Hook_AfterPlacement(int i,int j,int type,int style,int direction,int alternate)
    {
        var origin=MachineTiles.Origin(i,j);i=origin.X;j=origin.Y;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendTileSquare(Main.myPlayer,i,j,2);
            NetMessage.SendData(MessageID.TileEntityPlacement,-1,-1,null,i,j,Type);
            return -1;
        }
        return Place(i,j);
    }

    public void EnsureStationSlots()
    {
        Stations ??= Array.Empty<Item>();
        bool empty=false;
        for (int i=0;i<Stations.Length;i++)
        {
            Stations[i] ??= new Item();
            empty |= Stations[i].IsAir;
        }
        if (empty) return;
        Array.Resize(ref Stations,checked(Stations.Length+1));
        Stations[^1]=new Item();
    }

    public void InvalidateStations()
    { EnsureStationSlots(); _conditionErrors.Clear(); }

    public List<Item> ConnectedStations()
    {
        var stations=new List<Item>();
        if (Main.netMode==NetmodeID.MultiplayerClient)
        {
            foreach(var host in UISystem.GetStationHosts()) stations.AddRange(host.Stations);
            return stations;
        }
        foreach (var point in MENetwork.Collect(Position.X,Position.Y))
        {
            if (!TileEntity.ByPosition.TryGetValue(point,out var entity)) continue;
            if (entity is MECraftingTerminalEntity terminal) stations.AddRange(terminal.Stations);
            else if (entity is MEMolecularAssemblerEntity assembler) stations.AddRange(assembler.Stations);
        }
        return stations;
    }

    public bool HasStation(int tileType) => TerminalCraftingExecutor.HasStation(ConnectedStations(),tileType);

    public bool CanUseRecipe(Recipe recipe) => CanUseRecipe(recipe,ConnectedStations(),out _);

    internal bool CanUseRecipe(Recipe recipe,IReadOnlyList<Item> stations,out string reason,string requester=null)
    {
        reason=null;
        if (recipe==null || recipe.Disabled) { reason="配方不可用"; return false; }
        foreach (int tile in RecipeStation.GetRequiredStations(recipe))
            if (!TerminalCraftingExecutor.HasStation(stations,tile))
            { reason="等待工作站："+RecipeStation.DescribeStation(tile); return false; }
        int previousPlayer=Main.myPlayer;
        bool scoped=false;
        try
        {
            if (Main.netMode==NetmodeID.Server && recipe.Conditions.Count>0)
            {
                // Vanilla and mod conditions commonly read Main.LocalPlayer. Evaluate
                // on the main thread under the requesting player's actual environment,
                // then restore the server context even when a mod condition throws.
                string name=requester;
                if (string.IsNullOrEmpty(name)) name=NetworkRegistry.GetOwner(MENetwork.CanonicalAnchor(MENetwork.Collect(Position.X,Position.Y)));
                int index=Array.FindIndex(Main.player,p=>p!=null && p.active && p.name==name);
                if (index<0) { reason="等待下单玩家上线以检查环境";return false; }
                Main.myPlayer=index;scoped=true;
            }
            foreach (var condition in recipe.Conditions)
                if (!condition.IsMet()) { reason="等待环境："+condition.Description.Value; return false; }
            return true;
        }
        catch (Exception ex)
        {
            if (_conditionErrors.Add(recipe.RecipeIndex))
                Mod?.Logger.Warn("ME terminal recipe condition failed: "+recipe.RecipeIndex+" "+ex.Message);
            reason="配方条件暂时无法检查";
            return false;
        }
        finally { if(scoped) Main.myPlayer=previousPlayer; }
    }

    public override void Update()
    {
        base.Update();
        if (Main.netMode==NetmodeID.MultiplayerClient) return;
        // The canonical terminal executes once per component. Additional terminals
        // keep their own durable settings and add no artificial speed multiplier.
        if (ReferenceEquals(MENetwork.FindCraftingTerminal(Position.X,Position.Y),this)) _executor.Update(this);
    }

    public override void SaveData(TagCompound tag)
    {
        base.SaveData(tag);
        tag["terminalStations"]=new List<Item>(Stations);
    }

    public override void LoadData(TagCompound tag)
    {
        base.LoadData(tag);
        Stations=tag.ContainsKey("terminalStations") ? new List<Item>(tag.GetList<Item>("terminalStations")).ToArray() : Array.Empty<Item>();
        EnsureStationSlots();
        _conditionErrors.Clear();
        _executor.Reset();
    }
}
