using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AE2ForTerraria.UI;

/// <summary>Personal query order; never writes world defeat flags or server configuration.</summary>
internal static class BossOrderPreferences
{
    private static string[] _order;
    internal const string FileName = "AE2-BossOrder.json";
    internal static IReadOnlyList<string> Order
    {
        get
        {
            if (_order != null) return _order;
            _order = Array.Empty<string>();
            try
            {
                using var document = BrowserPreferences.Read(FileName);
                if (document?.RootElement.ValueKind == JsonValueKind.Array)
                    _order = document.RootElement.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Take(4096).ToArray();
            }
            catch (Exception ex) { AE2ForTerraria.Instance?.Logger.Warn("Cannot read boss order: " + ex.Message); }
            return _order;
        }
    }
    internal static bool Save(IEnumerable<string> order)
    {
        var values = order.Where(v => !string.IsNullOrWhiteSpace(v) && v != "start").Distinct().Take(4096).ToArray();
        if (!BrowserPreferences.Write(FileName, values)) return false;
        _order = values; return true;
    }
    internal static void Reset() => _order = null;
}
