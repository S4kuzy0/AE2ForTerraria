using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Explicit menu regression evidence; never runs during ordinary play.</summary>
internal static class AcquisitionAudit
{
    internal static void Export()
    {
        var graph=StorageSystem.Progression;
        if(graph==null) return;
        var items=ContentSamples.ItemsByType.Values.Where(item=>!item.IsAir).Select(item=>new
        {
            type=item.type, id=item.ModItem?.FullName ?? "Terraria/"+ItemID.Search.GetName(item.type),
            name=item.Name, tier=graph.GetTierIndex(ItemKey.Crafted(item.type)),
            reason=graph.GetExplanation(item.type),
            sources=StorageSystem.Acquisition.GetSources(ItemKey.Crafted(item.type)).Take(3).ToArray()
        }).ToArray();
        var drops=Enumerable.Range(1,NPCLoader.NPCCount-1).Concat(ContentSamples.NpcsByNetId.Keys.Where(id=>id<0))
            .SelectMany(npc=>GameDataIndex.GetNpcDropRecords(npc).Select(drop=>new
        { npc, item=drop.ItemType, conditions=drop.Conditions.Select(c=>c.GetType().FullName).ToArray() })).ToArray();
        var shops=NPCShopDatabase.AllShops.OfType<NPCShop>().Select(shop=>new
        { npc=shop.NpcType, entries=shop.Entries.Where(entry=>!entry.Disabled).Select(entry=>new
            { item=entry.Item.type, conditions=entry.Conditions.Select(c=>c.Description.Key).ToArray() }).ToArray() }).ToArray();
        var tiers=graph.Tiers.Select(t=>new { index=t.Index,id=t.Id,name=t.DisplayName,npcs=t.NpcIds }).ToArray();
        string directory=Path.Combine(Main.SavePath,"AE2-Validation"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"acquisition-audit.json"),JsonSerializer.Serialize(new
        { version=AE2ForTerraria.Instance.Version.ToString(),items,drops,shops,tiers }));
        AE2ForTerraria.Instance.Logger.Info("AE2 ACQUISITION AUDIT: exported "+items.Length+" item stages without world/player I/O");
    }
}
