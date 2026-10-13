using System.Collections.Generic;
using System.IO;
using AE2ForTerraria.Core;
using Terraria;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 清单(过滤 / 分区)的导入导出。
	///
	/// 存成**存档目录下的文本文件**,一行一个物品:
	///     74 # 铁矿石
	/// 换存档 / 换电脑时可以直接把文件拷过去,不用一个个重新点。
	/// 读取时照样过 <see cref="SavedItemGuard"/>,别人给的清单里有失效 ID 也不会写进去。
	/// </summary>
	public static class ListIO
	{
		private static string Dir
		{
			get
			{
				string root = Main.SavePath;
				if (string.IsNullOrEmpty(root))
				{
					root = Path.GetTempPath();
				}

				return Path.Combine(root, "AE2ForTerraria-Lists");
			}
		}

		private static string PathOf(string name)
		{
			// 文件名只保留安全字符,免得玩家在名字里带路径
			var safe = new System.Text.StringBuilder();

			foreach (char c in name ?? "list")
			{
				safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
			}

			return Path.Combine(Dir, safe + ".txt");
		}

		/// <summary>导出。返回写出的条目数;-1 表示写失败。</summary>
		public static int Save(string name, IEnumerable<int> ids)
		{
			try
			{
				Directory.CreateDirectory(Dir);

				var lines = new List<string>
				{
					"# AE2 for Terraria 清单导出:一行一个物品身份(原版 ID 或 ModName/ItemName)，后面是显示名"
				};

				int count = 0;

				foreach (int id in ids ?? new List<int>())
				{
					if (!SavedItemGuard.IsValidFilterEntry(id))
					{
						continue;
					}

					string itemName = Lang.GetItemNameValue(id);
					string identity = Terraria.ModLoader.ItemLoader.GetItem(id)?.FullName ?? id.ToString();
                    lines.Add(identity + " # " + (string.IsNullOrWhiteSpace(itemName) ? "?" : itemName));
					count++;
				}

				File.WriteAllLines(PathOf(name), lines);
				return count;
			}
			catch (System.Exception ex)
			{
                AE2ForTerraria.Instance.Logger.Warn("ME list file operation failed: " + ex.Message);
				return -1;
			}
		}

		/// <summary>导入。返回读到的条目数;-1 表示文件不存在或读失败。</summary>
		public static int TryLoad(string name, List<int> into)
		{
			try
			{
				string path = PathOf(name);
				if (!File.Exists(path))
				{
					return -1;
				}

				into.Clear();
				int count = 0;

				foreach (string rawLine in File.ReadAllLines(path))
				{
					// 支持 "123" 和 "123 # 铁矿石" 两种写法
					string line = rawLine.Trim();
					int comment = line.IndexOf('#');

					if (comment >= 0)
					{
						line = line.Substring(0, comment).Trim();
					}

					if (line.Length == 0)
					{
						continue;
					}

					int id;
                    if (!int.TryParse(line,out id))
                        id=Terraria.ModLoader.ModContent.TryFind<Terraria.ModLoader.ModItem>(line,out var modItem) ? modItem.Type : 0;
                    if (!SavedItemGuard.IsValidFilterEntry(id))
					{
						continue;
					}

					if (!into.Contains(id))
					{
						into.Add(id);
						count++;
					}
				}

				return count;
			}
			catch (System.Exception ex)
			{
                AE2ForTerraria.Instance.Logger.Warn("ME list file operation failed: " + ex.Message);
				return -1;
			}
		}

		/// <summary>清单文件所在目录(提示里显示给玩家)。</summary>
		public static string Folder => Dir;
	}
}
