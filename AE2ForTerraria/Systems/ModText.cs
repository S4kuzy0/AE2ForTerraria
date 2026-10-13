using Terraria.Localization;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 界面文字取词。键写在 Localization 的 `UI:` 段里,
	/// 找不到就回退到代码里写的中文(所以漏翻也不会显示成键名)。
	/// </summary>
	public static class ModText
	{
		private const string Prefix = "Mods.AE2ForTerraria.UI.";

		public static string Get(string key, string fallback)
		{
			string value = Language.GetTextValue(Prefix + key);

			// tModLoader 找不到键时会原样返回键名,靠这个判断
			return string.IsNullOrEmpty(value) || value == Prefix + key ? fallback : value;
		}

		/// <summary>
		/// 带参数的取词。hjson 里用 {0} {1} 占位。
		/// 例如 Format("LabelSlots", "{0} 格({1} 排)", visible, rows)。
		/// 找不到键就用 fallback,参数照样套上去,所以漏翻也不会显示成键名。
		/// </summary>
		public static string Format(string key, string fallback, params object[] args)
		{
			string template = Get(key, fallback);

			try
			{
				return string.Format(template, args);
			}
			catch
			{
				// 占位符和参数对不上时不要炸,退回原文
				return template;
			}
		}

		/// <summary>
		/// 构造阶段(语言表可能还没装载)用的取词。取不到就回退,
		/// 不会因为「还没加载本地化」把整个内容加载搞崩。
		/// </summary>
		public static string Safe(string key, string fallback)
		{
			try
			{
				return Get(key, fallback);
			}
			catch
			{
				return fallback;
			}
		}
	}
}
