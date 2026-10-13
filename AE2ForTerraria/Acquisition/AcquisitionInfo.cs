using System.Text;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;

namespace AE2ForTerraria.Acquisition
{
	public enum AcquisitionKind
	{
		Unknown,
		Craft,
		Shimmer,
		TreasureBag,
		NpcDrop,
		Shop,
		Event,
		Biome
	}

	public sealed class AcquisitionInfo
	{
		public ItemKey Key;
		public AcquisitionKind Kind = AcquisitionKind.Unknown;
		public string Detail = string.Empty;
		public int TierIndex = -1;

		public string Describe()
		{
			string text;
			switch (Kind)
			{
				case AcquisitionKind.Craft:
					text = ModText.Get("AcqCraft", "合成获得");
					break;
				case AcquisitionKind.Shimmer:
					text = ModText.Get("AcqShimmer", "微光转化");
					break;
				case AcquisitionKind.TreasureBag:
					text = ModText.Get("AcqBag", "宝藏袋开出");
					break;
				case AcquisitionKind.NpcDrop:
					text = ModText.Get("AcqDrop", "怪物掉落");
					break;
				case AcquisitionKind.Shop:
					text = ModText.Get("AcqShop", "商店购买");
					break;
				case AcquisitionKind.Event:
					text = ModText.Get("AcqEvent", "事件掉落");
					break;
				case AcquisitionKind.Biome:
					text = ModText.Get("AcqBiome", "环境产出");
					break;
				default:
					text = ModText.Get("AcqUnknown", "获取途径未知");
					break;
			}

			if (!string.IsNullOrEmpty(Detail))
			{
				text = text + " — " + Detail;
			}

			return text;
		}
	}
}
