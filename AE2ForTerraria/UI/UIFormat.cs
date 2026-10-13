using System.Globalization;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 数量显示。格子里原来直接画原数,五位数的物品存量会溢出格子。
	/// 现在超过一万就缩写:12000 -> 1.2万 / 1200000 -> 120万;
	/// 英文环境下走 K / M。
	/// </summary>
	public static class UIFormat
	{
		private static bool UseChineseUnits =>
			Terraria.Localization.Language.ActiveCulture != null
			&& Terraria.Localization.Language.ActiveCulture.Name.StartsWith("zh");

		public static string Count(long value)
		{
			if (value < 0)
			{
				return "0";
			}

			if (value < 10000)
			{
				return value.ToString(CultureInfo.InvariantCulture);
			}

			decimal scaled;
			string suffix;

			if (UseChineseUnits)
			{
				if (value >= 10000000000000000L)
				{
					scaled = value / 10000000000000000m;
					suffix = "京";
				}
				else if (value >= 1000000000000L)
				{
					scaled = value / 1000000000000m;
					suffix = "兆";
				}
				else if (value >= 100000000)
				{
					scaled = value / 100000000m;
					suffix = "亿";
				}
				else
				{
					scaled = value / 10000m;
					suffix = "万";
				}
			}
			else
			{
				if (value >= 1000000000000000000L)
				{
					scaled = value / 1000000000000000000m;
					suffix = "E";
				}
				else if (value >= 1000000000000000L)
				{
					scaled = value / 1000000000000000m;
					suffix = "P";
				}
				else if (value >= 1000000000000L)
				{
					scaled = value / 1000000000000m;
					suffix = "T";
				}
				else if (value >= 1000000000)
				{
					scaled = value / 1000000000m;
					suffix = "B";
				}
				else if (value >= 1000000)
				{
					scaled = value / 1000000m;
					suffix = "M";
				}
				else
				{
					scaled = value / 1000m;
					suffix = "K";
				}
			}

			return scaled.ToString(scaled >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture) + suffix;
		}

        public static string Exact(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
	}
}
