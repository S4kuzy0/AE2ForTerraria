using System.Collections.Generic;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 拼音搜索。拼音表放在 Assets/pinyin.dat(每行:一个汉字 + 它的拼音),
	/// 由构建时用 pypinyin 生成,覆盖 CJK 基本区两万多字。
	/// </summary>
	public static class PinyinIndex
	{
		/// <summary>一个物品的搜索预计算:原名(小写)、全拼、首字母。</summary>
		private struct Entry
		{
			public string Name;
			public string Full;
			public string Initials;
		}

		private static Dictionary<char, string> _table;
		private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

		public static bool Ready => _table != null && _table.Count > 0;

		public static int EntryCount => _table?.Count ?? 0;

		public static void Load(Mod mod)
		{
			if (_table != null)
			{
				return;
			}

			_table = new Dictionary<char, string>();

			try
			{
				byte[] data = mod.GetFileBytes("Assets/pinyin.dat");
				string text = Encoding.UTF8.GetString(data);

				foreach (string line in text.Split('\n'))
				{
					string trimmed = line.TrimEnd('\r');
					if (trimmed.Length < 2)
					{
						continue;
					}

					char ch = trimmed[0];
					string pinyin = trimmed.Substring(1).Trim().ToLowerInvariant();
					if (pinyin.Length > 0)
					{
						_table[ch] = pinyin;
					}
				}
			}
			catch
			{
				_table = new Dictionary<char, string>();
			}

			if (MEConfig.DiagnosticsEnabled)
			{
				mod.Logger.Info("拼音表加载完成:" + _table.Count + " 个汉字");
			}

			Cache.Clear();
		}

		public static void Unload()
		{
			_table = null;
			Cache.Clear();
		}

		/// <summary>把文本转成完整拼音(非汉字按原字符小写保留)。</summary>
		public static string ToFull(string text)
		{
			if (string.IsNullOrEmpty(text) || _table == null)
			{
				return string.Empty;
			}

			var sb = new StringBuilder();
			foreach (char ch in text)
			{
				if (_table.TryGetValue(ch, out string pinyin))
				{
					sb.Append(pinyin);
				}
				else if (ch < 128 && char.IsLetterOrDigit(ch))
				{
					sb.Append(char.ToLowerInvariant(ch));
				}
			}

			return sb.ToString();
		}

		/// <summary>把文本转成拼音首字母串。</summary>
		public static string ToInitials(string text)
		{
			if (string.IsNullOrEmpty(text) || _table == null)
			{
				return string.Empty;
			}

			var sb = new StringBuilder();
			foreach (char ch in text)
			{
				if (_table.TryGetValue(ch, out string pinyin))
				{
					if (pinyin.Length > 0)
					{
						sb.Append(pinyin[0]);
					}
				}
				else if (char.IsLetterOrDigit(ch))
				{
					sb.Append(char.ToLowerInvariant(ch));
				}
			}

			return sb.ToString();
		}

		/// <summary>
		/// 预计算一批物品的「名字 / 全拼 / 首字母」。
		/// 以前是第一次搜索时才现场算,于是第一次按键要遍历一万多件物品逐个建缓存,
		/// 会卡一下。现在在构建物品列表时一起算好,搜索时只查表。
		/// </summary>
		public static void Prewarm(IEnumerable<int> itemTypes)
		{
			if (itemTypes == null)
			{
				return;
			}

			foreach (int itemType in itemTypes)
			{
				EnsureCached(itemType);
			}
		}

		public static void Prewarm(int itemType)
		{
			EnsureCached(itemType);
		}

		private static bool EnsureCached(int itemType)
		{
			if (Cache.ContainsKey(itemType))
			{
				return true;
			}

			string name = Lang.GetItemNameValue(itemType);
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

			Cache[itemType] = new Entry
			{
				Name = name.ToLowerInvariant(),
				Full = ToFull(name),
				Initials = ToInitials(name)
			};

			return true;
		}

		/// <summary>
		/// 匹配:原名包含 / 完整拼音包含 / 拼音首字母包含。
		/// </summary>
		public static bool Matches(int itemType, string query)
		{
			if (string.IsNullOrEmpty(query))
			{
				return true;
			}

			string lower = query.ToLowerInvariant();

			// 先按原名匹配 —— 这条不需要拼音表,所以即使拼音表没加载也能用。
			string name = Lang.GetItemNameValue(itemType);
			if (!string.IsNullOrEmpty(name)
				&& name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}

			if (_table == null || _table.Count == 0)
			{
				return false;
			}

			if (!EnsureCached(itemType))
			{
				return false;
			}

			Entry entry = Cache[itemType];
			return entry.Full.Contains(lower) || entry.Initials.Contains(lower);
		}

		/// <summary>对任意名字做拼音匹配(怪物名用)。</summary>
		public static bool MatchesName(string name, string query)
		{
			if (string.IsNullOrEmpty(query))
			{
				return true;
			}

			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

			if (name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}

			if (_table == null || _table.Count == 0)
			{
				return false;
			}

			string lower = query.ToLowerInvariant();
			return ToFull(name).Contains(lower) || ToInitials(name).Contains(lower);
		}

		public static void ClearCache()
		{
			Cache.Clear();
		}
	}
}
