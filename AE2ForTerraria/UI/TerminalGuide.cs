using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.UI;

internal static class TerminalGuide
{
    internal static List<GuideRow> Storage()
    {
        var rows = new List<GuideRow> { new("存取失败 · 逐项排查", Heading: true) };
        bool open = PinnedInventory.HasOpenTerminal;
        if (!open)
        {
            rows.Add(new("先打开要检查的终端，再点状态文字进入这里；当前没有正在读取的终端。"));
            rows.Add(new("查看网络与设备", Link: GuideLink.Network));
            rows.Add(new("查看第一个终端教程", Link: GuideLink.Tutorial)); return rows;
        }
        rows.Add(new("网络读取：" + (UISystem.TerminalReadable ? "已就绪" : UISystem.TerminalAccessStatus), Satisfied: UISystem.TerminalReadable));
        rows.Add(new("存取权限：" + (UISystem.TerminalCanUse ? "允许使用" : "需要网络所有者授予使用权限"), Satisfied: UISystem.TerminalCanUse));
        if (!UISystem.TerminalReadable) return rows;
        var storage = UISystem.ActiveNetwork;
        rows.Add(new("存储连接：" + (storage?.Count > 0 ? storage.Count + " 个设备" : "未连接；将终端与驱动器用电缆连通"), Satisfied: storage?.Count > 0));
        if (storage != null)
            foreach (var unit in storage)
            {
                var ledger = unit.Ledger;
                rows.Add(new("类别占用 " + ledger.StoredTypeCount + " / " + UIFormat.Count(ledger.MaxTypes)
                    + (ledger.StoredTypeCount >= ledger.MaxTypes ? "：新增类别需扩容；已有类别仍需检查数量额度和其他限制" : "：仍有类别位"),
                    Link: GuideLink.Network, Satisfied: ledger.StoredTypeCount < ledger.MaxTypes));
                if (ledger.MaxBytes != long.MaxValue)
                    rows.Add(new("字节占用 " + UIFormat.Count(ledger.UsedBytes) + " / " + UIFormat.Count(ledger.MaxBytes)
                        + "；此类存储还受字节预算限制。", Link: GuideLink.Network, Satisfied: ledger.UsedBytes < ledger.MaxBytes));
            }
        if (Main.mouseItem is { IsAir: false } item && Main.netMode != NetmodeID.MultiplayerClient)
            rows.Add(new("手持物品预检：" + StorageOperations.PreviewDeposit(storage, item).Message));
        rows.Add(new("过滤：检查驱动器 / 存储设备的黑白名单；物品未通过过滤时，空余容量也不能接收。"));
        rows.Add(new("锁定：被锁定的材料不能由普通使用者取出或用于自动制作，请由管理者检查。"));
        rows.Add(new("打开网络概览，定位驱动器、容量和权限", Link: GuideLink.Network));
        rows.Add(new("右键实际驱动器可继续加盘；盘只提供容量，物品仍保存在驱动器中。"));
        rows.Add(WikiGuidance.ItemLink(ModContent.ItemType<MEDiskDrive>(), "扩容设备："));
        rows.Add(new("查看终端入门", Link: GuideLink.Tutorial)); return rows;
    }

    internal static List<GuideRow> Tutorial()
    {
        bool open = PinnedInventory.HasOpenTerminal && UISystem.TerminalReadable;
        var terminal = open ? UISystem.ActiveCraftingTerminal : null;
        var storage = open ? UISystem.ActiveNetwork : null;
        var drives = storage?.OfType<MEDiskDriveEntity>().ToArray() ?? System.Array.Empty<MEDiskDriveEntity>();
        var rows = new List<GuideRow> { new("第一个终端 · 跟着做", Heading: true),
            new("可以边玩边打开本页。状态反映当前打开的终端；关闭终端后显示待检查，不会冒充已完成。"),
            new("准备：电缆、驱动器、磁盘、终端", Heading: true) };
        foreach (int type in new[] { ModContent.ItemType<MECable>(), ModContent.ItemType<MEDiskDrive>(),
            ModContent.ItemType<MEDisk1K>(), ModContent.ItemType<MECraftingTerminal>() })
            rows.Add(WikiGuidance.ItemLink(type, "查看当前配方与获取方法："));
        rows.Add(new("第一步：放置并连接", Heading: true, Satisfied: open ? storage?.Count > 0 : null));
        rows.Add(new("把驱动器与终端相邻放置，或用连续电缆连接。右键终端，确认读取到存储设备。"));
        rows.Add(new("连接示意：驱动器 — 电缆 — 终端。可直接相邻；本模组不要求另接 MC 的电力或频道设备。"));
        rows.Add(new(open ? "当前连接：" + (storage?.Count ?? 0) + " 个存储设备" : "待检查：请打开准备使用的终端。", Link: GuideLink.Storage));
        rows.Add(new("第二步：给驱动器装盘", Heading: true, Satisfied: open ? drives.Any(d => d.Disks.Any(i => i != null && WikiGuidance.Loaded(i.type))) : null));
        rows.Add(new("右键驱动器，将磁盘放入槽位。换盘前检查剩余容量；物品存在驱动器中，盘只增加容量。"));
        rows.Add(new("查看连接与容量", Link: GuideLink.Storage));
        rows.Add(new("第三步：存入一些材料", Heading: true, Satisfied: open ? UISystem.ActiveNetworkContents?.Any(p => p.Value > 0) == true : null));
        rows.Add(new("打开终端，Shift＋左键背包物品存入；也可手持物品点击终端目录。目录左键拿起，放回背包。"));
        rows.Add(new("第四步：安装工作站", Heading: true, Satisfied: terminal == null ? null : terminal.ConnectedStations().Any(i => i != null && WikiGuidance.Loaded(i.type) && i.createTile >= 0)));
        rows.Add(new("将工作台、熔炉等物品放进终端工作站槽；所需工作站随配方变化，查看配方检查页确认。"));
        rows.Add(WikiGuidance.ItemLink(ItemID.WorkBench, "从工作台开始："));
        rows.Add(new("第五步：检查并制作", Heading: true));
        rows.Add(new("鼠标悬停物品按 R 打开详情，选择数量。点击制作状态可查看缺料、工作站和环境；满足后再下单。"));
        rows.Add(new("练习：检查当前火把配方", ItemID.Torch, Link: GuideLink.Recipe));
        rows.Add(new("第六步：确认产出并取用", Heading: true));
        rows.Add(new("查看合成完成提示与终端中的新增产物；左键拿到鼠标，再放入背包。材料不够时，Alt＋左键收藏目标，打开“下一步”继续准备。"));
        rows.Add(new("遇到问题：检查容量、过滤和权限", Link: GuideLink.Storage));
        return rows;
    }
}
