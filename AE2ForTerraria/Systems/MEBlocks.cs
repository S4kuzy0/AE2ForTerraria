using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// ME 方块类型表。
	///
	/// 以前 MENetwork.IsMEBlock 和 MEBlockWatcher 各抄了一份「10 个 ModContent.TileType&lt;T&gt;()」的
	/// 判断链,而 BFS 每走一个邻居格都要过一遍 —— 上限 4096 格就是上万次泛型查找。
	/// 现在集中到这里:只在第一次用时解析一次 id,之后是 HashSet 查表。
	///
	/// 另外,以前 MEBlockWatcher 少了线缆和无线桥接器,铺线缆不会清连通性缓存。
	/// 这份表是唯一的真源,两边都用它,不会再漏。
	/// </summary>
	public static class MEBlocks
	{
		private static HashSet<int> _types;
		private static int _diskDrive;
		private static int _cable;
		private static int _wirelessBridge;

		public static int DiskDrive => Ensure() ? _diskDrive : 0;

		public static int Cable => Ensure() ? _cable : 0;

		public static int WirelessBridge => Ensure() ? _wirelessBridge : 0;

		private static bool Ensure()
		{
			if (_types != null)
			{
				return true;
			}

			// 模组还没装载完的时候 TileType 会返回 0,这时候不要缓存,
			// 否则会把「0 号方块」当成 ME 方块吃进去。
			int drive = ModContent.TileType<MEDiskDriveTile>();
			if (drive <= 0)
			{
				return false;
			}

			_diskDrive = drive;
			_cable = ModContent.TileType<MECableTile>();
			_wirelessBridge = ModContent.TileType<MEWirelessBridgeTile>();

			_types = new HashSet<int>
			{
				_diskDrive,
				ModContent.TileType<MEStorageUnitTile>(),
				ModContent.TileType<MECraftingTerminalTile>(),
				ModContent.TileType<MEMolecularAssemblerTile>(),
				ModContent.TileType<MECraftingCpuTile>(),
				ModContent.TileType<MEAccessTerminalTile>(),
				ModContent.TileType<MEDropCollectorTile>(),
				ModContent.TileType<MEStorageBusTile>(),
				_cable,
				_wirelessBridge,
			};

			return true;
		}

		public static bool IsMEBlockType(int tileType)
		{
			return tileType > 0 && Ensure() && _types.Contains(MachineTiles.CanonicalType(tileType));
		}

		/// <summary>模组卸载时清掉缓存,免得重载后留着上一轮的 id。</summary>
		public static void Reset()
		{
			_types = null; MachineTiles.Reset();
			_diskDrive = 0;
			_cable = 0;
			_wirelessBridge = 0;
		}
	}
}
