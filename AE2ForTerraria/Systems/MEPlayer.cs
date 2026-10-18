using System.Collections.Generic;
using AE2ForTerraria.Content.Achievements;
using AE2ForTerraria.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 玩家身上的 ME 数据:绑定的访问终端列表。
	/// 以前只能绑一个,再绑就覆盖;现在最多存 <see cref="MaxBindings"/> 个,
	/// 最近绑定的排在最前(远程按键打开的就是它)。
	/// </summary>
	public class MEPlayer : ModPlayer
	{
        public override void SetControls()
        {
            if (Player.whoAmI == Main.myPlayer && Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.R)
                && AEUiSession.FocusedInterface?.CurrentState is global::AE2ForTerraria.UI.TerminalUIState terminal
                && terminal.RecipeShortcutHovered)
                Player.controlMount = false;
        }

        public override bool ShiftClickSlot(Item[] inventory, int context, int slot)
        {
            if (Player.whoAmI != Main.myPlayer || !ReferenceEquals(inventory, Player.inventory) || slot < 0 || slot >= 50
                || AEUiSession.CapturesInput || MENet.InventoryTransactionPending || MENet.CursorPending
                || UISystem.Interface?.CurrentState != UISystem.TerminalState)
                return false;
            return UISystem.TerminalState.DepositInventorySlot(slot);
        }
		public const int MaxBindings = 5;
        private readonly Dictionary<string,List<Point16>> _worldBindings=new();
        private string _bindingWorld;
        private static string WorldKey => Main.ActiveWorldFileData?.UniqueId.ToString() ?? "";

		/// <summary>已绑定的访问终端坐标,索引 0 是"主绑定"。</summary>
		public readonly List<Point16> BoundTerminals = new List<Point16>();

		public bool HasTerminalBinding => BoundTerminals.Count > 0;

		public Point16 PrimaryTerminal => BoundTerminals.Count > 0 ? BoundTerminals[0] : default;

		/// <summary>绑定(或提升)一个访问终端。返回绑定后一共有几个。</summary>
		public int BindTerminal(int x, int y)
		{
			var position = new Point16(x, y);

			BoundTerminals.RemoveAll(p => p.X == position.X && p.Y == position.Y);
			BoundTerminals.Insert(0, position);

			while (BoundTerminals.Count > MaxBindings)
			{
				BoundTerminals.RemoveAt(BoundTerminals.Count - 1);
			}

			// 成就「随身网络」也下沉到数据层:不管从哪绑的都会算
			AchievementTriggers.Trigger<BindTerminalAchievement>();

			return BoundTerminals.Count;
		}

		private int _magnetTimer;

		/// <summary>
		/// 「附近没有 ME 方块」的负缓存(按 tick 计)。
		/// FindNearbyStorage 最坏要查 25×25=625 格,而绝大多数时候玩家身边根本没网络 ——
		/// 一次没找到就安静 1.5 秒,多个玩家时这部分开销也跟着降下来。
		/// </summary>
		private int _noStorageCooldown;

		/// <summary>
		/// 这个玩家自己的磁铁开关(默认开)。
		/// 模组配置里的 <c>PersonalMagnetEnabled</c> 是**服务器级总开关**;
		/// 这里是玩家级的临时开关,按键(默认 M)随时切,会同步给服务器。
		/// </summary>
		public bool MagnetEnabled = true;

		/// <summary>
		/// 随身掉落磁铁:站在自己的 ME 网络旁边时,附近的掉落物直接进网。
		///
		/// 在**服务器 / 单人**执行(多人下由服务器判定,客户端不用管),
		/// 每 15 tick 跑一次,只扫玩家周围那一小片格子找网络,
		/// 找不到网络就直接返回,所以开着也不会有负担。
		/// </summary>
		public override void PostUpdate()
		{
			if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
			{
				return;
			}

			MEConfig config = MEConfig.Instance;
			if (config == null || !config.PersonalMagnetEnabled || Player == null || !Player.active
				|| !MagnetEnabled)
			{
				return;
			}

			if (++_magnetTimer < 15)
			{
				return;
			}

			_magnetTimer = 0;

			if (_noStorageCooldown > 0)
			{
				_noStorageCooldown -= 15;
				return;
			}

			List<IMEStorage> storage = FindNearbyStorage(Player);
			if (storage.Count == 0)
			{
				_noStorageCooldown = 90; // 1.5 秒内不再扫
				return;
			}

			float radius = config.PersonalMagnetRadius * 16f;
			float radiusSquared = radius * radius;

			for (int i = 0; i < Main.maxItems; i++)
			{
				Item item = Main.item[i];
				if (item == null || !item.active || item.type <= 0 || item.stack <= 0
					|| item.noGrabDelay > 0)
				{
					continue;
				}

				if (Vector2.DistanceSquared(item.Center, Player.Center) > radiusSquared)
				{
					continue;
				}

				// 磁铁用自己的清单(和掉落收集器的默认清单分开)
				if (!config.AllowsMagnet(item.type))
				{
					continue;
				}

				long stored = MENetwork.Deposit(storage, item);

				if (stored > 0 && item.stack > 0 && Main.netMode == Terraria.ID.NetmodeID.Server)
                    NetMessage.SendData(Terraria.ID.MessageID.SyncItem, -1, -1, null, i);
                if (stored > 0 && item.stack <= 0)
				{
					item.active = false;
					NetMessage.SendData(Terraria.ID.MessageID.SyncItem, -1, -1, null, i);
				}
				else if (stored <= 0 && config.VoidOverflowDrops)
				{
					// 网络满了:按配置直接销毁,免得地上堆一片
					item.active = false;
					NetMessage.SendData(Terraria.ID.MessageID.SyncItem, -1, -1, null, i);
				}
			}
		}

		/// <summary>在玩家周围找第一个 ME 网络(找到就返回它的全部存储方块)。</summary>
		private static List<IMEStorage> FindNearbyStorage(Player player)
		{
			int cx = (int)(player.Center.X / 16f);
			int cy = (int)(player.Center.Y / 16f);
			const int ScanRadius = 12;

			for (int dy = -ScanRadius; dy <= ScanRadius; dy++)
			{
				for (int dx = -ScanRadius; dx <= ScanRadius; dx++)
				{
					int x = cx + dx;
					int y = cy + dy;

					if (!MENetwork.IsMEBlock(x, y))
					{
						continue;
					}

					List<IMEStorage> storage = MENetwork.CollectStorage(x, y);
					if (storage.Count > 0 && NetworkRegistry.GetLevel(MENetwork.CanonicalAnchor(MENetwork.Collect(x,y)),player.name)>=NetworkRegistry.LevelUse)
					{
						return storage;
					}
				}
			}

			return EmptyStorage;
		}

		private static readonly List<IMEStorage> EmptyStorage = new List<IMEStorage>();

		/// <summary>移除失效的绑定(方块被拆 / 换世界)。</summary>
		public void PruneBindings()
		{
			if (Main.netMode!=Terraria.ID.NetmodeID.MultiplayerClient)
                BoundTerminals.RemoveAll(p => !MENetwork.IsMEBlock(p.X,p.Y)
                    || MachineTiles.CanonicalType(Main.tile[p.X,p.Y].TileType)!=ModContent.TileType<Content.Tiles.MEAccessTerminalTile>());
		}

		public override void SaveData(TagCompound tag)
		{
			tag["magnetEnabled"] = MagnetEnabled;
            if (!string.IsNullOrEmpty(_bindingWorld)) _worldBindings[_bindingWorld]=new(BoundTerminals);
            var worlds=new List<TagCompound>();
            foreach (var pair in _worldBindings)
            {
                var points=new List<TagCompound>();
                foreach (var p in pair.Value) points.Add(new TagCompound { ["x"]=(int)p.X,["y"]=(int)p.Y });
                worlds.Add(new TagCompound { ["world"]=pair.Key,["points"]=points });
            }
            tag["worldBindings"]=worlds;

			if (BoundTerminals.Count == 0)
			{
				return;
			}

			var list = new List<TagCompound>();
			foreach (Point16 point in BoundTerminals)
			{
				var entry = new TagCompound();
				entry["x"] = (int)point.X;
				entry["y"] = (int)point.Y;
				list.Add(entry);
			}

			tag["boundTerminals"] = list;
		}

		public override void LoadData(TagCompound tag)
		{
			// LoadData 抛异常 = 人物直接加载不出来,所以这里整体兜一层:
			// 读不出来的数据当默认值处理,最多丢一次绑定,不能让玩家进不了游戏。
			try
			{
				BoundTerminals.Clear();
                _worldBindings.Clear(); _bindingWorld=null;
                if (tag.ContainsKey("worldBindings"))
                    foreach (var world in tag.GetList<TagCompound>("worldBindings"))
                    {
                        var points=new List<Point16>();
                        foreach (var p in world.GetList<TagCompound>("points"))
                            if (points.Count<MaxBindings) points.Add(new Point16(TagCompat.ReadInt(p,"x"),TagCompat.ReadInt(p,"y")));
                        _worldBindings[world.GetString("world")]=points;
                    }
                MagnetEnabled = !tag.ContainsKey("magnetEnabled") || tag.GetBool("magnetEnabled");

				if (!tag.ContainsKey("boundTerminals"))
				{
					// 兼容旧存档(单绑定)。
					// 注意:老版本把坐标写成 **short**,直接 GetInt 会抛 InvalidCastException ——
					// 这正是 0.16.0 那个「加载人物自定义数据时遇到问题」的原因。
					if (tag.ContainsKey("boundX"))
					{
						BoundTerminals.Add(new Point16(
							TagCompat.ReadInt(tag, "boundX"),
							TagCompat.ReadInt(tag, "boundY")));
					}

					return;
				}

				foreach (TagCompound entry in tag.GetList<TagCompound>("boundTerminals"))
				{
					BoundTerminals.Add(new Point16(
						TagCompat.ReadInt(entry, "x"),
						TagCompat.ReadInt(entry, "y")));
				}
			}
			catch (System.Exception ex)
			{
				BoundTerminals.Clear();
				MagnetEnabled = true;

				Terraria.ModLoader.ModContent.GetInstance<AE2ForTerraria>()?.Logger.Warn(
					"读取玩家 ME 数据失败,已按默认值处理(不会影响进游戏):" + ex.Message);
			}
		}

		public override void OnEnterWorld()
		{
			string world=WorldKey;
            if (!string.IsNullOrEmpty(_bindingWorld)) _worldBindings[_bindingWorld]=new(BoundTerminals);
            if (_worldBindings.TryGetValue(world,out var saved)) { BoundTerminals.Clear(); BoundTerminals.AddRange(saved); }
            else if (_worldBindings.Count>0) BoundTerminals.Clear(); // old single-world saves migrate on first entry only
            _bindingWorld=world;
            PruneBindings();
            MENet.SendPlayerState(this);
		}

		// —— 多人同步(标准 ModPlayer 模式) ——

		/// <summary>把状态复制到"客户端旧状态"对象,供变化比较用。</summary>
        public override void SyncPlayer(int toWho,int fromWho,bool newPlayer) => MENet.SendPlayerState(this);
		public override void CopyClientState(ModPlayer targetCopy)
		{
			var clone = (MEPlayer)targetCopy;
			clone.BoundTerminals.Clear();
			clone.BoundTerminals.AddRange(BoundTerminals);
			clone.MagnetEnabled = MagnetEnabled;
		}

		/// <summary>绑定发生变化时把状态发给服务器/其它玩家。</summary>
		public override void SendClientChanges(ModPlayer clientPlayer)
		{
			var old = (MEPlayer)clientPlayer;

			// 玩家级磁铁开关也要同步,否则服务端那边判定的还是旧值
			if (old.MagnetEnabled != MagnetEnabled)
			{
				SyncPlayer(-1, Main.myPlayer, false);
				return;
			}

			if (old.BoundTerminals.Count != BoundTerminals.Count)
			{
				SyncPlayer(-1, Main.myPlayer, false);
				return;
			}

			for (int i = 0; i < BoundTerminals.Count; i++)
			{
				if (old.BoundTerminals[i] != BoundTerminals[i])
				{
					SyncPlayer(-1, Main.myPlayer, false);
					return;
				}
			}
		}
	}
}
