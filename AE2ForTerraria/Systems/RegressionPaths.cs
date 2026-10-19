using System;
using System.IO;
using Terraria;

namespace AE2ForTerraria.Systems;

// Used only by explicitly enabled regression fixtures; never enables a fixture itself.
internal static class RegressionPaths
{
    internal static string Root
    {
        get
        {
            string configured = Environment.GetEnvironmentVariable("AE2_VALIDATION_ROOT");
            return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Main.SavePath, "AE2-Validation") : configured);
        }
    }
}
