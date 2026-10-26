using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Terraria.ModLoader;

namespace AE2ForTerraria.Progression;

/// <summary>
/// Evidence for individual routes which cannot be reduced to a Boolean predicate.
/// Canonical method shapes include operands/signatures but exclude package versions
/// and metadata tokens. Only the affected route is withheld when its evidence changes.
/// </summary>
internal static class CalamitySourceCompatibility
{
    internal sealed record MethodSpec(string Type, string Method, string Hash);
    private static readonly ConditionalWeakTable<Assembly, Dictionary<string, bool>> Results = new();
    private static readonly ConditionalWeakTable<Assembly, HashSet<string>> Warnings = new();
    private static readonly Dictionary<string, MethodSpec[]> Profiles = new(StringComparer.Ordinal)
    {
        ["npc/WulfrumAmplifier"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.NormalNPCs.WulfrumAmplifier", "SpawnChance", "18C63872A590DBB60960B67CAC38DCD6664DBCCBBF385A1039028BB1ABBF3781"),
        },
        ["npc/Stormlion"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.NormalNPCs.Stormlion", "SpawnChance", "B02858E21BD3FB42F92C4E8188BD33D998CF8686D4C5B32EB5C165E3AC067E1F"),
        },
        ["npc/LuminousCorvina"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.Abyss.LuminousCorvina", "SpawnChance", "FE4E0B7361952C85689C764CB2B95D1B2DC1FE78D5006321064AE0D3024BA160"),
        },
        ["npc/DevilFish"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.Abyss.DevilFish", "SpawnChance", "799EA1338DDB98698C94EFEB9B5F41A549E4BED0AC77952917EE64DBA1E1AD8C"),
        },
        ["npc/ChaoticPuffer"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.Abyss.ChaoticPuffer", "SpawnChance", "0A11833520D1B95C239973F7CF476CF28A98778E78E05A4A8A4F32B1D852E87F"),
        },
        ["npc/AeroSlime"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.NormalNPCs.AeroSlime", "SpawnChance", "12FAD0A9673492F67E3EC2726AAC3E4361F3F3119D4AA8899965513E4E54A1AE"),
        },
        ["npc/CryoSlime"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.NormalNPCs.CryoSlime", "SpawnChance", "50F228C91569CDB8F0A8D2EA54DE87094B11D522467D75A79638F013C1302856"),
        },
        ["rod-harmony"] = new MethodSpec[]
        {
            new("CalamityMod.ILEditing.ILChanges", "ApplyEdits", "5DE4CCCDBE54EFE05A4A8171C237B238FF76733BFD43C01835B274A377B96BE6"),
            new("CalamityMod.ILEditing.ILChanges", "AdjustShimmerRequirements", "EAB505A2C61BBB86FBC0DF40750021F07B2AFF63949A0FE8AA0EF8B95B3F21B9"),
        },
        ["angler-sundial"] = new MethodSpec[]
        {
            new("CalamityMod.ILEditing.ILChanges", "ApplyEdits", "5DE4CCCDBE54EFE05A4A8171C237B238FF76733BFD43C01835B274A377B96BE6"),
            new("CalamityMod.ILEditing.ILChanges", "AddMoreGuaranteedAnglerRewards", "6A1BEC8E9B8CAA76318F442F89002D876370DF99741510E7D307A810C23C9F5B"),
        },
        ["blood-orb"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.NormalNPCs.Piggy", "SpawnChance", "71270AF0D60214F5BE3B1C88E24575D365765533C492E108AAEE375D2B63BC0B"),
            new("CalamityMod.NPCs.NormalNPCs.Piggy", "AI", "40F8F02F13350614C09C33C7CB58B5E9937332E6CF5C52E25F064CDCEDAD414E"),
            new("CalamityMod.NPCs.NormalNPCs.Piggy", "TransformIntoVariant", "B2F7CB171C1BF1E66B0486E99C1034DAADDE8E23D4DC630A6E61BCFE76D3EBF0"),
            new("CalamityMod.NPCs.NormalNPCs.Piggy", "TryTransformingIntoVariant", "5525AE18A60C1DE449438FFBEDF3B10F80E9A88AA2072A743AE83B1DBB61C0E3"),
            new("CalamityMod.Projectiles.PowderTransformationsProjectile", "AI", "53A9C0CD243B2E699D45DE0C10D9C9085BEF53095391B7BF7378A6A53D9AE622"),
        },
        ["sunken-sea"] = new MethodSpec[]
        {
            new("CalamityMod.Systems.WorldgenManagementSystem", "ModifyWorldGenTasks", "612EB59C1FF0A3C57275AB40CB5619093E1341CDDF1FCA964BE99838AD78FC57"),
            new("CalamityMod.Systems.WorldgenManagementSystem+<>c", "<ModifyWorldGenTasks>b__4_23", "5BAC6562F48BA55CFA63A5D40289E3C3F2B6C93AA3B99F215CDE8764F797D6ED"),
            new("CalamityMod.World.SunkenSea", "Place", "95CA089D985C4CC5E9BC68763A4927D1AFC60FEF900C1639ED040F2EA45F3E6E"),
            new("CalamityMod.World.SunkenSea", "PlaceClusters", "6D83233E96604B3842939821CB63E6C4C1026151B551D6347F14B905AEF131F0"),
            new("CalamityMod.World.SunkenSea", "AddGeodes", "EAAC04B15F85920D58BD55F6513302086CAD9C8D875DAB1D4AE692AAD28E66FB"),
            new("CalamityMod.World.SunkenSea", "AddTileVariance", "FBE74C8F99A5538FA52480C15A7A94BB1B9A3FA88F53A68A65401875AD2CBECE"),
            new("CalamityMod.World.SunkenSea+ClusterGroup", "Generate", "A540C8D82E64248DA7A976B60A891C5B9DA2B9A8662E7A1143AB12489DA6BEA2"),
            new("CalamityMod.World.SunkenSea+ClusterGroup", "SearchForCluster", "87995B5C7D8ED04F7D5F0BAAD20169EDADB311636276BE0D7C174D67F8F2DA43"),
            new("CalamityMod.World.SunkenSea+ClusterGroup", "AttemptClaim", "406924B6561EF28A36E79B5850B088C2001E2FACB07B559DCFEDACAF4F4D5ACF"),
        },
        ["aerialite-generation"] = new MethodSpec[]
        {
            new("CalamityMod.Systems.WorldgenManagementSystem", "ModifyWorldGenTasks", "612EB59C1FF0A3C57275AB40CB5619093E1341CDDF1FCA964BE99838AD78FC57"),
            new("CalamityMod.Systems.WorldgenManagementSystem+<>c", "<ModifyWorldGenTasks>b__4_31", "B00CE15387481182B04A14EE04FA1CA8ED1D61A703397FB0C935E107CBC0DF33"),
            new("CalamityMod.World.AerialiteOreGen", "Generate", "48DEE70D470E2683276508457021DE335DADBE5D703444370F992B2100198415"),
            new("CalamityMod.World.AerialiteOreGen", "Enchant", "5CB4DC21D3C534F8F5BF1BA533A05287030A8EC2C66EC8D619EB863F1063F648"),
        },
        ["aerialite-hive"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.HiveMind.HiveMind", "OnKill", "F170BC2B8218552F470EF78819D2ABE127AF15D3C52BEBD8CF4CD2C15293E9BF"),
        },
        ["aerialite-perforator"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.Perforator.PerforatorHive", "OnKill", "A29B7C737B12A08DF685C2A1C27B4E3313E0F6D8CF74918877339CA291FDBC8B"),
        },
        ["cryonic-ore"] = new MethodSpec[]
        {
            new("CalamityMod.NPCs.Cryogen.Cryogen", "OnKill", "D5AC64765BC00CD3D7C6338A448A4DAF11902A743AFC570207ED945B8870B54E"),
            new("CalamityMod.CalamityUtils", "SpawnOre", "7A53692FBBCAAC4C0504BFB06B17E242930222121D68E1FBB472B4C4B59E18DE"),
            new("CalamityMod.CalamityUtils", "SpawnOre", "7FDF9468E2BAEF918148B18E3529E46150F96AA555467048E870EA1440F9CD6B"),
        },
        ["scoria-ore"] = new MethodSpec[]
        {
            new("CalamityMod.Systems.WorldgenManagementSystem", "ModifyWorldGenTasks", "612EB59C1FF0A3C57275AB40CB5619093E1341CDDF1FCA964BE99838AD78FC57"),
            new("CalamityMod.Systems.WorldgenManagementSystem+<>c", "<ModifyWorldGenTasks>b__4_33", "5C312BD3ED4E6AD794B3D45E46231A2CF9A16B32AA28056F5ED802E164AA38A5"),
            new("CalamityMod.World.Abyss", "PlaceAbyss", "3BC21493E3D8F3D9860DEF7DAC8C49B13F4D97D7F740796A1BD3631BF1DCA2C3"),
            new("CalamityMod.World.Abyss", "AbyssIsland", "E8A22CA4A0A3E3BBD17D894858E82E5613D143958C584999F1ECF48B5CF9C311"),
        },
    };

    internal static IEnumerable<string> Routes => Profiles.Keys;
    internal static bool Supports(Mod mod, string route)
    {
        if (mod?.Code == null) return false;
        var cache = Results.GetValue(mod.Code, _ => new Dictionary<string, bool>(StringComparer.Ordinal));
        if (cache.TryGetValue(route, out bool previous)) return previous;
        bool supported = Profiles.TryGetValue(route, out var profile) && Matches(profile, spec =>
        {
            var type = mod.Code.GetType(spec.Type, false);
            return type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(method => method.Name == spec.Method) ?? Enumerable.Empty<MethodInfo>();
        });
        cache[route] = supported;
        if (!supported) WarnUnsupported(mod, route, "current method shapes differ from the established route");
        return supported;
    }

    internal static void WarnUnsupported(Mod mod, string route, string reason)
    {
        if (mod?.Code != null && Warnings.GetValue(mod.Code, _ => new HashSet<string>(StringComparer.Ordinal)).Add(route))
            AE2ForTerraria.Instance?.Logger.Warn("Calamity acquisition route " + route + " is unavailable: " + reason + "; other routes remain available.");
    }

    internal static bool Matches(IEnumerable<MethodSpec> profile, Func<MethodSpec, IEnumerable<MethodInfo>> resolve)
    {
        foreach (var spec in profile)
        {
            try
            {
                if (!resolve(spec).Any(method => AdaptiveMethodShape.TryHash(method, out string hash) && hash == spec.Hash)) return false;
            }
            catch (Exception) { return false; }
        }
        return true;
    }
}
