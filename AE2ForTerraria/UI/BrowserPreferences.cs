using System;
using System.IO;
using System.Text.Json;
using Terraria;

namespace AE2ForTerraria.UI;

/// <summary>Personal browser state, separate from world, character and network data.</summary>
internal static class BrowserPreferences
{
    private static string _directory;
    internal static string FilePath(string name) => Path.Combine(_directory ?? Main.SavePath, name);
    internal static JsonDocument Read(string name)
    {
        string path = FilePath(name);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Browser preference file is too large.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
    internal static bool Write<T>(string name, T value)
    {
        string path = FilePath(name), temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception ex)
        {
            AE2ForTerraria.Instance?.Logger.Warn("Cannot save browser preferences: " + ex.Message);
            Systems.UIStatus.Toast("浏览设置保存失败，请查看日志");
            return false;
        }
    }
    internal static IDisposable Isolate(string directory)
    {
        string previous = _directory;
        _directory = directory; BrowserHistory.Reset(); BrowserFavorites.Reset(); FavoriteTreeHud.Reset();
        return new Restore(() => { _directory = previous; BrowserHistory.Reset(); BrowserFavorites.Reset(); FavoriteTreeHud.Reset(); });
    }
    private sealed class Restore(Action action) : IDisposable
    {
        private Action _action = action;
        public void Dispose() { var action = _action; _action = null; action?.Invoke(); }
    }
}
