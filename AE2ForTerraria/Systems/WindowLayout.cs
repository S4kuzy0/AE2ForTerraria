using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AE2ForTerraria.UI;
using Terraria;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 窗口位置 / 大小的持久化。
	///
	/// 以前 Geo 只是进程内的静态量 —— 关掉游戏再进来,拖好的布局全没了。
	/// 现在写进存档目录下的一个纯文本文件(一行一个窗口):
	///     TerminalUIState=120,60,1080,660
	/// 这文件是「这台机器上的界面偏好」,不进世界存档,也不进 ModConfig
	/// (ModConfig 是要同步给服务器的,界面布局跟服务器没关系)。
	/// </summary>
	public static class WindowLayout
	{
		private sealed class Entry
		{
			public float X;
			public float Y;
			public float W;
			public float H;
		}

		private static readonly Dictionary<string, Entry> Saved = new Dictionary<string, Entry>();

		private static bool _loaded;
		private static bool _dirty;

		private static string FilePath
		{
			get
			{
				string root = Main.SavePath;
				if (string.IsNullOrEmpty(root))
				{
					root = Path.GetTempPath();
				}

				return Path.Combine(root, "AE2ForTerraria-Layout.txt");
			}
		}

		public static void Load()
		{
			if (_loaded)
			{
				return;
			}

			_loaded = true;
			Saved.Clear();

			try
			{
				string path = FilePath;
				if (!File.Exists(path))
				{
					return;
				}

				foreach (string rawLine in File.ReadAllLines(path))
				{
					string line = rawLine.Trim();
					if (line.Length == 0 || line.StartsWith("#"))
					{
						continue;
					}

					int split = line.IndexOf('=');
					if (split <= 0)
					{
						continue;
					}

					string key = line.Substring(0, split).Trim();
					string[] parts = line.Substring(split + 1).Split(',');
					if (parts.Length != 4)
					{
						continue;
					}

					if (!TryParse(parts[0], out float x) || !TryParse(parts[1], out float y)
						|| !TryParse(parts[2], out float w) || !TryParse(parts[3], out float h))
					{
						continue;
					}

					Saved[key] = new Entry { X = x, Y = y, W = w, H = h };
				}
			}
			catch
			{
				// 布局文件读不出来不算错,用默认布局就行
			}
		}

		private static bool TryParse(string text, out float value)
		{
			return float.TryParse(text.Trim(), NumberStyles.Float,
				CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
		}

		/// <summary>把存下来的几何套到某个窗口上(没有记录就保持默认)。</summary>
		public static void Apply(string key, WindowGeometry geometry)
		{
			if (geometry == null || string.IsNullOrEmpty(key))
			{
				return;
			}

			Load();

			if (!Saved.TryGetValue(key, out Entry entry))
			{
				return;
			}

			// 尺寸至少要满足这个窗口自己的最小尺寸,不然会缩成一团
			geometry.W = System.Math.Max(geometry.MinW, entry.W);
			geometry.H = System.Math.Max(geometry.MinH, entry.H);
			geometry.X = entry.X;
			geometry.Y = entry.Y;
		}

		public static void Store(string key, WindowGeometry geometry)
		{
			if (geometry == null || string.IsNullOrEmpty(key))
			{
				return;
			}

			Saved[key] = new Entry
			{
				X = geometry.X,
				Y = geometry.Y,
				W = geometry.W,
				H = geometry.H
			};

			_dirty = true;
		}

		/// <summary>把这个窗口恢复到「设计时的默认位置和大小」。</summary>
		public static void Reset(string key, WindowGeometry geometry)
		{
			if (geometry == null)
			{
				return;
			}

			if (!string.IsNullOrEmpty(key))
			{
				Saved.Remove(key);
			}

			geometry.W = geometry.DefaultW;
			geometry.H = geometry.DefaultH;
			geometry.ResetPosition();
			_dirty = true;
		}

		public static void Save()
		{
			if (!_dirty || Main.dedServ)
			{
				return;
			}

			_dirty = false;

			try
			{
				var lines = new List<string>
				{
					"# AE2 for Terraria 窗口布局(自动生成,删掉这一行及以下内容即可重置全部窗口)"
				};

				foreach (KeyValuePair<string, Entry> pair in Saved)
				{
					lines.Add(pair.Key + "="
						+ pair.Value.X.ToString("0.##", CultureInfo.InvariantCulture) + ","
						+ pair.Value.Y.ToString("0.##", CultureInfo.InvariantCulture) + ","
						+ pair.Value.W.ToString("0.##", CultureInfo.InvariantCulture) + ","
						+ pair.Value.H.ToString("0.##", CultureInfo.InvariantCulture));
				}

				File.WriteAllLines(FilePath, lines);
			}
			catch
			{
				// 写不进去也不该影响游戏
			}
		}

		public static void Clear()
		{
			Saved.Clear();
			_loaded = false;
			_dirty = true;
		}
	}
}
