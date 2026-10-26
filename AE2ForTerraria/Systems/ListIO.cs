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
		/// 模组物品使用稳定名称；未加载项保留，无法识别的数字身份拒绝整次导入。
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

				var lines = ExportLines(ids);
				File.WriteAllLines(PathOf(name), lines);
				return lines.Count - 1;
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

                var parsed = ImportLines(File.ReadAllLines(path));
                ItemPersistence.ReplaceFilterContents(into, parsed);
				return ItemPersistence.FilterIdentities(parsed).Count;
			}
			catch (System.Exception ex)
			{
                AE2ForTerraria.Instance.Logger.Warn("ME list file operation failed: " + ex.Message);
				return -1;
			}
		}

        internal static List<string> ExportLines(IEnumerable<int> ids)
        {
            var lines = new List<string> { "# AE2 for Terraria 清单导出:原版 ID 或 ModName/ItemName；未加载物品会保留" };
            foreach (string identity in ItemPersistence.FilterIdentities(ids))
            {
                var resolved = ItemPersistence.FilterFromIdentities(new[] { identity });
                string name = resolved.Count > 0 ? Lang.GetItemNameValue(resolved[0]) : "模组未加载，保留此项";
                lines.Add(identity + " # " + name);
            }
            return lines;
        }

        internal static List<int> ImportLines(IEnumerable<string> lines)
        {
            var identities = new List<string>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line.Substring(0, comment).Trim();
                if (line.Length > 0) identities.Add(line);
            }
            return ItemPersistence.FilterFromIdentities(identities);
        }

		/// <summary>清单文件所在目录(提示里显示给玩家)。</summary>
		public static string Folder => Dir;
	}
}
