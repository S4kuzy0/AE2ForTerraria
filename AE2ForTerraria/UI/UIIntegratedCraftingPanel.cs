using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace AE2ForTerraria.UI;

/// <summary>Terminal task/station pages. All multiplayer changes are server requests.</summary>
internal sealed class UIIntegratedCraftingPanel : UIElement
{
    private readonly UIList _list=new() { ListPadding=5 };
    private readonly UIScrollbar _scroll=new();
    private readonly UIButton _once=new("一次任务",.78f), _keep=new("保持库存",.78f), _output=new("产出：进网络",.78f);
    private readonly UIText _status=new("",.75f);
    private readonly Action<MECraftingCpuEntity,ItemKey,long> _edit;
    private bool _stations,_showKeep;
    private int _signature=int.MinValue;
    private float _width,_height;

    public UIIntegratedCraftingPanel(Action<MECraftingCpuEntity,ItemKey,long> edit)
    {
        _edit=edit;
        _scroll.SetView(100,1000);
        _list.SetScrollbar(_scroll);
        _once.OnLeftClick+=(_,_)=>{_showKeep=false;Invalidate();};
        _keep.OnLeftClick+=(_,_)=>{_showKeep=true;Invalidate();};
        _output.OnLeftClick+=(_,_)=>
        {
            var cpu=UISystem.ActiveCraftingCpu;
            if (cpu==null || !CanManage()) return;
            if (Main.netMode==NetmodeID.MultiplayerClient) MENet.CpuSetOutput(cpu.Position,!cpu.OutputToInventory);
            else cpu.OutputToInventory=!cpu.OutputToInventory;
            Invalidate();
        };
        Append(_list); Append(_scroll); Append(_status);
    }

    public void Show(bool stations)
    { _stations=stations; _scroll.ViewPosition=0; Invalidate(); }
    public void Invalidate() => _signature=int.MinValue;
    private static void Place(UIElement element,float x,float y,float w,float h)
    { element.Left.Set(x,0);element.Top.Set(y,0);element.Width.Set(w,0);element.Height.Set(h,0); }

    public void SetBounds(float x,float y,float w,float h)
    {
        Place(this,x,y,w,h);
        if (_width==w && _height==h) return;
        _width=w;_height=h; Invalidate();
    }

    public override void Update(GameTime time)
    {
        base.Update(time);
        _output.Enabled=!_showKeep && UISystem.TerminalCanManage;
        int signature=ComputeSignature();
        if (signature!=_signature) { _signature=signature; Refresh(); }
        if (ContainsPoint(Main.MouseScreen)) Main.LocalPlayer.mouseInterface=true;
    }

    private int ComputeSignature()
    {
        var hash=new HashCode(); hash.Add(_stations);hash.Add(_showKeep);hash.Add(_width);hash.Add(_height);
        hash.Add(UISystem.TerminalCanManage);hash.Add(UISystem.TerminalReadable);
        hash.Add(UISystem.ActiveCraftingCpu?.OutputToInventory);
        if (_stations)
            foreach (var host in UISystem.GetStationHosts())
            {
                hash.Add(host.Position);hash.Add(host.Stations.Length);
                foreach (var item in host.Stations) { hash.Add(item?.type);hash.Add(item?.stack); }
            }
        else foreach (var cpu in UISystem.GetCraftingHosts())
        {
            hash.Add(cpu.Position);hash.Add(cpu.Paused);hash.Add(cpu.Jobs.Count);hash.Add(cpu.Keep.Count);
            if (_showKeep) foreach (var entry in cpu.Keep) {hash.Add(entry.Key);hash.Add(entry.Target);}
            else foreach (var job in cpu.Jobs) {hash.Add(job.Id);hash.Add(job.Remaining);hash.Add(job.Paused);hash.Add(job.LastStatus);}
        }
        return hash.ToHashCode();
    }

    private void Refresh()
    {
        float scroll=_scroll.ViewPosition;
        _list.Clear(); _once.Remove();_keep.Remove();_output.Remove();
        if (!_stations)
        {
            Append(_once);Append(_keep);Append(_output);
            Place(_once,0,0,84,28);Place(_keep,90,0,92,28);
            Place(_output,Math.Max(188,_width-156),0,156,28);
            _once.Selected=!_showKeep;_keep.Selected=_showKeep;
            _output.SetText(_showKeep ? "补货：进网络" : UISystem.ActiveCraftingCpu?.OutputToInventory==true ? "产出：玩家背包" : "产出：进网络");
            _output.IgnoresMouseInteraction=_showKeep;
        }
        float top=_stations ? 0 : 36;
        Place(_list,0,top,Math.Max(40,_width-20),Math.Max(40,_height-top-30));
        Place(_scroll,_width-16,top,16,Math.Max(40,_height-top-30));
        Place(_status,0,_height-23,_width,22); _status.DynamicallyScaleDownToWidth=true;
        if (_stations) BuildStations(); else BuildTasks();
        Recalculate(); _scroll.ViewPosition=scroll;
    }

    private UIFlatPanel Row(float height)
    {
        var row=new UIFlatPanel { BackgroundColor=new Color(30,40,67), BorderColor=new Color(65,79,112) };
        row.SetPadding(0);row.Width.Set(0,1);row.Height.Set(height,0);_list.Add(row);return row;
    }
    private static UIText Label(UIElement row,string text,float x,float y,float width,float scale=.78f)
    {
        var label=new UIText(text,scale) { DynamicallyScaleDownToWidth=true };
        Place(label,x,y,Math.Max(20,width),24);row.Append(label);return label;
    }
    private static UIButton Button(UIElement row,string text,float x,float y,float width,Action action)
    {
        var button=new UIButton(text,.72f) {Enabled=UISystem.TerminalCanManage};Place(button,x,y,width,26);button.OnLeftClick+=(_,_)=>action();row.Append(button);return button;
    }
    private static bool CanManage()
    {
        if (Main.netMode==NetmodeID.MultiplayerClient)
        {
            if(UISystem.TerminalCanManage) return true; // Server still rechecks the request.
            UIStatus.Toast(UISystem.TerminalReadable ? "需要网络管理权限" : UISystem.TerminalAccessStatus);return false;
        }
        var point=UISystem.ActiveTerminalPos;
        var anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(point.X,point.Y));
        if (NetworkRegistry.GetOwner(anchor)==null) NetworkRegistry.TryClaim(anchor,Main.LocalPlayer?.name);
        if (NetworkRegistry.GetLevel(anchor,Main.LocalPlayer?.name)>=NetworkRegistry.LevelManage) return true;
        UIStatus.Toast("需要网络管理权限");return false;
    }

    private void BuildTasks()
    {
        int count=0;
        float width=_width-20;
        foreach (var cpu in UISystem.GetCraftingHosts())
        {
            if (_showKeep)
            {
                foreach (var entry in cpu.Keep)
                {
                    count++;var row=Row(68);
                    var icon=UIItemIcon.Create(entry.Key.Type,32,null);Place(icon,9,9,32,32);row.Append(icon);
                    Label(row,UIItemNames.Display(entry.Key),49,8,width-226);
                    Label(row,"当前 "+UISystem.GetStoredAmount(entry.Key)+" / 目标 "+entry.Target,49,36,width-65,.72f);
                    var key=entry.Key;long target=entry.Target;
                    Button(row,"修改",width-132,8,58,()=>_edit(cpu,key,target));
                    Button(row,"移除",width-68,8,58,()=>
                    {
                        if (!CanManage()) return;
                        if (Main.netMode==NetmodeID.MultiplayerClient) MENet.CpuKeep(cpu.Position,key,0); else cpu.SetKeep(key,0);
                        Invalidate();
                    });
                }
            }
            else foreach (var job in cpu.Jobs)
            {
                count++; var row=Row(86);var target=job;
                var icon=UIItemIcon.Create(job.Key.Type,32,null);Place(icon,9,9,32,32);row.Append(icon);
                Label(row,UIItemNames.Display(job.Key),49,8,width-230);
                Label(row,"已完成 "+job.Done+" / "+job.Total,49,34,width-65,.72f);
                Label(row,cpu.Paused || job.Paused ? "已暂停" : job.LastStatus ?? "等待处理",49,58,width-65,.72f);
                Button(row,cpu.Paused || job.Paused ? "继续" : "暂停",width-132,8,58,()=>
                {
                    if (!CanManage()) return;
                    if (cpu.Paused)
                    { if (Main.netMode==NetmodeID.MultiplayerClient) MENet.CpuPause(cpu.Position);else cpu.Paused=false; }
                    else if (Main.netMode==NetmodeID.MultiplayerClient) MENet.SetJobPaused(cpu.Position,target.Id,!target.Paused);
                    else target.Paused=!target.Paused;
                    Invalidate();
                });
                Button(row,"取消",width-68,8,58,()=>
                {
                    if (!CanManage()) return;
                    if (Main.netMode==NetmodeID.MultiplayerClient) MENet.CpuRemove(cpu.Position,target.Id);else cpu.DropJob(target);
                    Invalidate();
                });
            }
        }
        if (count==0) {var row=Row(56);Label(row,_showKeep ? "在配方页选择物品和数量，点击保持库存。" : "暂无任务，在配方页选择数量后下单。",12,16,width-24);}
        _status.SetText(_showKeep ? "共 "+count+" 项库存目标 · 补货产出进入网络" : "共 "+count+" 项任务 · "+(UISystem.ActiveCraftingCpu?.LastStatus ?? "等待网络数据"));
        if(!UISystem.TerminalReadable) _status.SetText(UISystem.TerminalAccessStatus);
    }

    private void BuildStations()
    {
        int occupied=0;
        float width=_width-20;
        foreach (var host in UISystem.GetStationHosts())
        {
            var position=host.Position;var slots=host.Stations;
            for (int i=0;i<slots.Length;i++)
            {
                int index=i; var item=slots[index];
                if ((item==null || item.IsAir) && position!=UISystem.ActiveCraftingTerminal?.Position) continue;
                if (item!=null && !item.IsAir) occupied++;
                var row=Row(60);
                var slot=CreateStationSlot(position,index,Invalidate);
                Place(slot,7,7,44,44);row.Append(slot);
                Label(row,item==null || item.IsAir ? "放入工作站" : item.Name,61,10,width-78);
                Label(row,item==null || item.IsAir ? "手持工作站后点击；自动增加空槽" : "点击取回 · 此网络内的终端共用",61,33,width-78,.7f);
            }
        }
        _status.SetText("已接入 "+occupied+" 个工作站 · 保留原配方环境条件");
        if(!UISystem.TerminalReadable) _status.SetText(UISystem.TerminalAccessStatus);
    }

    private static bool TryStationSlots(Point16 position,int index,out Item[] slots)
    {
        // Both local entities and remote mirrors can replace their Item[] after growth
        // or a snapshot. Resolve the current network membership and array for each use.
        foreach(var host in UISystem.GetStationHosts())
            if(host.Position==position && index>=0 && index<host.Stations.Length)
            {slots=host.Stations;return true;}
        slots=null;return false;
    }

    internal static UIItemSlotElement CreateStationSlot(Point16 position,int index,Action changed=null)
    {
        var slot=UIItemSlotElement.Create(
            ()=>TryStationSlots(position,index,out var current) ? current[index] : new Item(),
            next=>{if(CanManage() && TryStationSlots(position,index,out var current)) current[index]=next;},
            canAccept:value=>value.createTile>=0);
        slot.InteractionHandler=right=>
        {
            if(!CanManage()) return true;
            if(!TryStationSlots(position,index,out var current))
            {UIStatus.Toast("工作站已断开或槽位已变化");changed?.Invoke();return true;}
            if(Main.netMode==NetmodeID.MultiplayerClient) {MENet.SlotAction(position,index,true,right);return true;}
            var cursor=Main.mouseItem;
            if(ContainerSlotOperations.TryChange(current,index,ref cursor,right,null,out string reason))
            {
                Main.mouseItem=cursor;
                if(TileEntity.ByPosition.TryGetValue(position,out var entity) && entity is MECraftingTerminalEntity terminal)
                    terminal.EnsureStationSlots();
                changed?.Invoke();
            }
            else if(!string.IsNullOrEmpty(reason)) UIStatus.Toast(reason);
            return true;
        };
        return slot;
    }
}
