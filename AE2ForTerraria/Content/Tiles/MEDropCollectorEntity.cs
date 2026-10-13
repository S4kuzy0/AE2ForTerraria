using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// 掉落收集器:把半径内的掉落物直接吸进网络,被过滤掉的留在原地。
	/// </summary>
	public class MEDropCollectorEntity : ModTileEntity
	{
		private const int IntervalTicks = 15;

		private int _timer;

		/// <summary>这块收集器自己的开关(初值取自模组配置,之后各自独立)。</summary>
		public bool Enabled = true;

		/// <summary>true = 黑名单(清单里的不收),false = 白名单(只收清单里的)。</summary>
		public bool IsBlacklist = true;

		/// <summary>这块收集器自己的半径(格)。</summary>
		public float Radius = 24f;

		/// <summary>这块收集器自己的过滤清单。</summary>
		public List<int> Filter = new List<int>();

		// 过滤清单的查表缓存 —— 每 15 tick 都要过一遍地上的掉落物,
		// 不能每次都线性扫清单。
		// 靠**显式失效**刷新(MarkFilterDirty),而不是每次调用都重算签名:
		// 以前那种写法等于每 15 tick 对地面每个掉落物都遍历一遍清单。
		private HashSet<int> _filterSet;

		public void MarkFilterDirty()
		{
			_filterSet = null;
		}

		/// <summary>按加载时的模组配置给这块收集器设一份初值。</summary>
		public void ApplyDefaultsFromConfig()
		{
			MEConfig config = MEConfig.Instance;
			if (config == null)
			{
				Enabled = true;
				IsBlacklist = true;
				Radius = 24f;
				Filter = new List<int>();
				MarkFilterDirty();
				return;
			}

			Enabled = config.DropCollectorEnabled;
			IsBlacklist = config.FilterIsBlacklist;
			Radius = config.DropCollectorRadius;
			Filter = new List<int>();

			if (config.FilteredItems != null)
			{
				foreach (Terraria.ModLoader.Config.ItemDefinition definition in config.FilteredItems)
				{
					if (definition != null && definition.Type > 0)
					{
						Filter.Add(definition.Type);
					}
				}
			}

			MarkFilterDirty();
		}

		public bool Allows(int itemType)
		{
			if (Filter == null)
			{
				return true;
			}

			if (_filterSet == null)
			{
				_filterSet = new HashSet<int>(Filter);
			}

			bool listed = _filterSet.Contains(itemType);
			return IsBlacklist ? !listed : listed;
		}

		public override bool IsTileValidForEntity(int x, int y)
		{
			Tile tile = Main.tile[x, y];
			return MachineTiles.IsEntityAnchor(x, y, ModContent.TileType<MEDropCollectorTile>());
		}

		public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				NetMessage.SendTileSquare(Main.myPlayer, i, j, 1);
				NetMessage.SendData(MessageID.TileEntityPlacement, -1, -1, null, i, j, Type);
				return -1;
			}

			int placed = Place(i, j);

			// 服务器放下的新收集器:拿模组配置当默认值
			if (placed >= 0
				&& TileEntity.ByPosition.TryGetValue(new Point16(i, j), out TileEntity entity)
				&& entity is MEDropCollectorEntity collector)
			{
				collector.ApplyDefaultsFromConfig();
			}

			return placed;
		}

		public override void Update()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				return;
			}

			if (++_timer < IntervalTicks)
			{
				return;
			}

			_timer = 0;
			CollectDrops();
		}

		private void CollectDrops()
		{
			if (!Enabled)
			{
				return;
			}

			List<IMEStorage> units = MENetwork.CollectStorage(Position.X, Position.Y);
			if (units.Count == 0)
			{
				return;
			}

			var center = new Vector2(Position.X * 16f + 8f, Position.Y * 16f + 8f);
			float radius = System.Math.Max(2f, Radius) * 16f;
			float radiusSquared = radius * radius;

			for (int i = 0; i < Main.maxItems; i++)
			{
				Item item = Main.item[i];
				if (item == null || !item.active || item.type <= 0 || item.stack <= 0 || item.noGrabDelay > 0)
				{
					continue;
				}

				if (Vector2.DistanceSquared(item.Center, center) > radiusSquared)
				{
					continue;
				}

				ItemKey key = ItemKey.FromItem(item);
				if (!Allows(key.Type))
				{
					continue;
				}

				long stored = MENetwork.Deposit(units, item);

				if (stored > 0 && Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncItem, -1, -1, null, i);
                if (stored > 0 && item.stack <= 0)
				{
					item.active = false;
                    if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.SyncItem, -1, -1, null, i);
				}
				else if (stored <= 0 && MEConfig.VoidOverflowDropsOn)
				{
					// 网络满了:配置打开时直接销毁(默认留在原地)
					item.active = false;
					NetMessage.SendData(MessageID.SyncItem, -1, -1, null, i);
				}
			}
		}

		public override void SaveData(TagCompound tag)
		{
			tag["enabled"] = Enabled;
			tag["blacklist"] = IsBlacklist;
			tag["radius"] = Radius;
			ItemPersistence.SaveFilter(tag,"filter",Filter);
		}

		public override void LoadData(TagCompound tag)
		{
			Enabled = !tag.ContainsKey("enabled") || tag.GetBool("enabled");
			IsBlacklist = !tag.ContainsKey("blacklist") || tag.GetBool("blacklist");
			Radius = TagCompat.ReadFloat(tag, "radius", 24f);

			Filter = ItemPersistence.LoadFilter(tag,"filter");

			MarkFilterDirty();
		}
	}
}
