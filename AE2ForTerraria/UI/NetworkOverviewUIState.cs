using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 网络概览:列出世界里的每一个 ME 网络 ——
	/// 名字、坐标、方块数、容量占用、物品类型数,并且可以就地改名。
	///
	/// 以前只能跑到方块前逐个看,装了大型模组包之后根本不知道自己在哪铺过网络。
	/// </summary>
	public sealed class NetworkOverviewUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = new WindowGeometry(760f, 520f, 560f, 380f);

		private sealed class Row
		{
			public Point16 Anchor;
			public List<Point16> Blocks;
			public int Storages;
			public long UsedBytes;
			public long MaxBytes;
			public long Types;
			public long Items;

			/// <summary>方块构成:驱动器 / 存储单元 / 终端 / 装配室 / CPU / 收集器 / 总线 / 桥接器 / 线缆。</summary>
			public int Drives;
			public int Units;
			public int Terminals;
			public int Assemblers;
			public int Cpus;
			public int Collectors;
			public int Buses;
			public int Bridges;
			public int Cables;

			public bool Named => !string.IsNullOrEmpty(NetworkRegistry.GetName(Anchor));
		}

		private readonly List<Row> _rows = new List<Row>();
		private readonly List<UIElement> _spawned = new List<UIElement>();

		private UIText _status;
		private UITextBox _nameBox;
		private UITextPanel<string> _renameButton;
		private UITextPanel<string> _refreshButton;
		private UITextPanel<string> _openTerminalButton;
		private UITextPanel<string> _bindButton;
		private UITextPanel<string> _sourceButton;
		private UITextPanel<string> _transferButton;
		private Point16 _transferSource = new Point16(-1, -1);
		private UITextPanel<string> _prevButton;
		private UITextPanel<string> _nextButton;
		private UIText _pageLabel;

		private int _selected = -1;
		private int _page;
		/// <summary>每行现在有两行文字(信息 + 方块构成),所以行高加大了。</summary>
		private const int RowHeight = 46;
		private bool _showPermissions;
		private UITextPanel<string> _tabButton;
		private UIPartitionEditor _lockEditor;
		private UITextPanel<string> _lockTabButton;
		private bool _showLocks;

		/// <summary>
		/// 把「锁定清单」伪装成一份分区清单,好复用 <see cref="UIPartitionEditor"/> ——
		/// 搜索框、勾选网格、清空、导入导出全都是现成的。
		/// </summary>
		private sealed class LockListHost : IPartitionedStorage
		{
			private readonly Point16 _anchor;

			public LockListHost(Point16 anchor)
			{
				_anchor = anchor;
			}

			public List<int> Filter
			{
				get => NetworkRegistry.GetLocked(_anchor);
				set
				{
					if (Main.netMode == NetmodeID.MultiplayerClient)
					{
						MENet.SetLockedList(_anchor, value);
					}
					else
					{
						NetworkRegistry.SetLockedList(_anchor, value);
					}
				}
			}

			// 锁定清单没有白 / 黑名单的概念
			public bool IsWhitelist
			{
				get => true;
				set { }
			}

			public void MarkFilterDirty()
			{
			}
		}

		private LockListHost _lockHost;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("TitleNetworks", "ME 网络概览"));

			_hint = new UIText(ModText.Get("HintNetworks",
				"点一行选中它,然后在下面输入名字改名。名字存在世界存档里。"), 0.72f, false);
			Window.Append(_hint);

			_status = new UIText(string.Empty, 0.75f, false);
			_status.TextColor = UITheme.TextDim;
			Window.Append(_status);

			_nameBox = new UITextBox(string.Empty, 0.8f, false);
			_nameBox.BackgroundColor = UITheme.Field;
			Window.Append(_nameBox);

			_renameButton = MakeButton(ModText.Get("BtnRename", "重命名"), () =>
			{
				if (_selected < 0 || _selected >= _rows.Count)
				{
					return;
				}

				Row row = _rows[_selected];

				// 多人:改名要走服务器,否则只写本地字典,重连就丢
				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.RenameNetwork(row.Anchor, _nameBox.Text);
				}
				else
				{
					NetworkRegistry.SetName(row.Anchor, _nameBox.Text);
				}

				UIStatus.Toast(ModText.Get("MsgNetworkRenamed", "网络名字已更新"));
				RefreshNetworks();
				_nameBox.SetText(NetworkRegistry.GetName(_rows[_selected].Anchor) ?? string.Empty);
			});

			_refreshButton = MakeButton(ModText.Get("BtnRefresh", "刷新"), RefreshNetworks);

			// 省掉「跑过去」的两个动作
			_openTerminalButton = MakeButton(ModText.Get("BtnOpenNetworkTerminal", "打开该网络的终端"), () =>
			{
				if (!TryGetSelected(out Row row))
				{
					return;
				}

				// 从网络里任意一格都能打开(终端会自己找驱动器 / 装配室 / CPU)
				Point16 spot = FindUsableBlock(row);
				UISystem.OpenTerminal(spot.X, spot.Y, TerminalViewMode.Access);
			});

			_bindButton = MakeButton(ModText.Get("BtnBindNetwork", "设为远程绑定"), () =>
			{
				if (!TryGetSelected(out Row row))
				{
					return;
				}

				Point16 spot = FindUsableBlock(row);

				// 绑定到玩家身上,之后按键(默认 P)就能远程打开
				Main.LocalPlayer.GetModPlayer<Systems.MEPlayer>().BindTerminal(spot.X, spot.Y);
				UIStatus.Toast(ModText.Get("MsgBoundNetwork", "已把这个网络设为远程终端绑定"));
			});

			// 网络之间直接搬东西:先「作为来源」,再选中目标点「接收转移」
			_sourceButton = MakeButton(ModText.Get("BtnAsSource", "作为来源"), () =>
			{
				if (!TryGetSelected(out Row row))
				{
					return;
				}

				_transferSource = row.Anchor;
				UpdateTransferLabel();

				UIStatus.Toast(ModText.Get("MsgSourceSet",
					"已把选中的网络设为转移来源,再选中目标网络点「接收转移」"));
			});

			_transferButton = MakeButton(ModText.Get("BtnReceiveTransfer", "接收转移"), () =>
			{
				if (!TryGetSelected(out Row row))
				{
					return;
				}

				if (_transferSource.X < 0)
				{
					UIStatus.Toast(ModText.Get("MsgNoSource",
						"先选一个网络点「作为来源」"), new Color(255, 200, 150));
					return;
				}

				if (_transferSource.Equals(row.Anchor))
				{
					UIStatus.Toast(ModText.Get("MsgSameNetwork",
						"来源和目标不能是同一个网络"), new Color(255, 200, 150));
					return;
				}

				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.TransferNetwork(_transferSource, row.Anchor);
					UIStatus.Toast(ModText.Get("MsgTransferSent", "已把转移请求发给服务器"));
					return;
				}

				long moved = MENetwork.TransferBetweenNetworks(
					_transferSource.X, _transferSource.Y, row.Anchor.X, row.Anchor.Y);

				Content.Achievements.AchievementTriggers
					.Trigger<Content.Achievements.TransferNetworkAchievement>();

				UIStatus.Toast(ModText.Format("MsgTransferred", "已转移 {0} 件物品", moved),
					moved > 0 ? new Color(150, 220, 255) : new Color(255, 200, 150));

				RefreshNetworks();
			});

			_prevButton = MakeButton(ModText.Get("PrevPage", "◀ 上一页"), () =>
			{
				_page = System.Math.Max(0, _page - 1);
				Refresh();
			});

			_nextButton = MakeButton(ModText.Get("NextPage", "下一页 ▶"), () =>
			{
				_page++;
				Refresh();
			});

			_pageLabel = new UIText(string.Empty, 0.72f, false);
			Window.Append(_pageLabel);

			// 权限设置页(和网络列表共用同一个窗口,切换显示)
			_tabButton = new UIButton(ModText.Get("BtnPermissions", "权限设置"), 0.78f, false);
			_tabButton.OnLeftClick += (evt, element) =>
			{
				_showPermissions = !_showPermissions;
				_showLocks = false;
				_tabButton.SetText(_showPermissions
					? ModText.Get("BtnNetworkList", "返回网络列表")
					: ModText.Get("BtnPermissions", "权限设置"));
				Refresh();
			};
			Window.Append(_tabButton);

			// 锁定清单:复用同一个勾选网格编辑器
			_lockEditor = new UIPartitionEditor
			{
				ShowModeButton = false,
				HintKey = "HintLockedList",
				HintFallback = "锁定清单:被锁的物品只有所有者 / 管理者能取;点格子勾选 / 取消"
			};
			Window.Append(_lockEditor);

			_lockTabButton = new UIButton(ModText.Get("BtnLocks", "锁定清单"), 0.78f, false);
			_lockTabButton.OnLeftClick += (evt, element) =>
			{
				_showLocks = !_showLocks;
				_showPermissions = false;
				_tabButton.SetText(ModText.Get("BtnPermissions", "权限设置"));
				_lockTabButton.SetText(_showLocks
					? ModText.Get("BtnNetworkList", "返回网络列表")
					: ModText.Get("BtnLocks", "锁定清单"));

				if (_showLocks && TryGetSelectedQuiet(out Row row))
				{
					_lockHost = new LockListHost(row.Anchor);
					_lockEditor.Bind(_lockHost, row.Anchor);
				}

				Refresh();
			};
			Window.Append(_lockTabButton);

			ApplyLayout();
		}

		private UIText _hint;

		private bool TryGetSelected(out Row row)
		{
			if (_selected >= 0 && _selected < _rows.Count)
			{
				row = _rows[_selected];
				return true;
			}

			row = null;
			UIStatus.Toast(ModText.Get("MsgSelectNetwork", "先点一行选中一个网络"),
				new Color(255, 200, 150));
			return false;
		}

		/// <summary>挑一格能用开终端的位置:优先存储方块,其次第一格。</summary>
		/// <summary>
		/// 健康检查:只报「少了什么会导致什么不能用」。
		/// 正常时返回空串(不占地方)。
		/// </summary>
		private static string Diagnose(Row row)
		{
			if (row.Storages == 0)
			{
				return ModText.Get("DiagNoStorage", "没有存储方块 —— 只能看,存不进东西");
			}

			if (row.MaxBytes > 0 && row.MaxBytes != long.MaxValue)
			{
				float ratio = (float)row.UsedBytes / row.MaxBytes;

				if (ratio >= 0.95f)
				{
					return ModText.Get("DiagFull", "容量已满 —— 再存会存不进去");
				}

				if (ratio >= 0.8f)
				{
					return ModText.Format("DiagAlmostFull", "容量已用 {0}% —— 该换更大的磁盘了",
						(int)(ratio * 100f));
				}
			}

			if (row.Assemblers == 0)
			{
				return ModText.Get("DiagNoAssembler", "没有分子装配室 —— 无法自动合成");
			}

			if (row.Cpus == 0)
			{
				return ModText.Get("DiagNoCpu", "没有合成 CPU —— 下单只能一件一件做");
			}

			if (row.Terminals == 0)
			{
				return ModText.Get("DiagNoTerminal", "没有终端方块 —— 只能右键方块打开界面");
			}

			return string.Empty;
		}

		private static Point16 FindUsableBlock(Row row)
		{
			foreach (Point16 point in row.Blocks)
			{
				if (TileEntity.ByPosition.TryGetValue(point, out TileEntity entity)
					&& entity is IMEStorage)
				{
					return point;
				}
			}

			return row.Blocks.Count > 0 ? row.Blocks[0] : row.Anchor;
		}

		private UITextPanel<string> MakeButton(string text, System.Action onClick)
		{
			var button = new UIButton(text, 0.78f, false);
			button.OnLeftClick += (evt, element) => onClick();
			Window.Append(button);
			return button;
		}

		/// <summary>重新扫一遍世界里的网络。</summary>
		public void RefreshNetworks()
		{
			_rows.Clear();
			_selected = -1;

			foreach (List<Point16> blocks in MENetwork.CollectAllNetworks())
			{
				var row = new Row
				{
					Anchor = MENetwork.CanonicalAnchor(blocks),
					Blocks = blocks
				};

				foreach (Point16 point in blocks)
				{
					// 方块构成:排查「为什么没容量 / 为什么不能合成」时最有用
					switch (Main.tile[point.X, point.Y].TileType)
					{
						case var t when t == MEBlocks.DiskDrive:
							row.Drives++;
							break;
						case var t when t == ModContent.TileType<MEStorageUnitTile>():
							row.Units++;
							break;
						case var t when t == ModContent.TileType<MECraftingTerminalTile>()
							|| t == ModContent.TileType<MEAccessTerminalTile>():
							row.Terminals++;
							break;
						case var t when t == ModContent.TileType<MEMolecularAssemblerTile>():
							row.Assemblers++;
							break;
						case var t when t == ModContent.TileType<MECraftingCpuTile>():
							row.Cpus++;
							break;
						case var t when t == ModContent.TileType<MEDropCollectorTile>():
							row.Collectors++;
							break;
						case var t when t == ModContent.TileType<MEStorageBusTile>():
							row.Buses++;
							break;
						case var t when t == MEBlocks.WirelessBridge:
							row.Bridges++;
							break;
						case var t when t == MEBlocks.Cable:
							row.Cables++;
							break;
					}

					if (TileEntity.ByPosition.TryGetValue(point, out TileEntity entity)
						&& entity is IMEStorage storage)
					{
						row.Storages++;
						row.UsedBytes = StorageAmounts.Add(row.UsedBytes, storage.Ledger.UsedBytes);
						row.MaxBytes = StorageAmounts.Add(row.MaxBytes, storage.Ledger.MaxBytes);
						row.Types = StorageAmounts.Add(row.Types, storage.Ledger.StoredTypeCount);
						row.Items = StorageAmounts.Add(row.Items, storage.Ledger.StoredTotalAmount);
					}
				}

				_rows.Add(row);
			}

			// 成就进度提示:还有哪些没做完,点出来给玩家看
			UITextCache.Set(_hint, ModText.Get("HintNetworks",
					"点一行选中它,然后在下面输入名字改名。名字存在世界存档里。")
				+ "    |    " + Systems.AchievementHint.Progress());

			// 有名字的排前面,然后按坐标
			_rows.Sort((a, b) =>
			{
				int byName = (b.Named ? 1 : 0).CompareTo(a.Named ? 1 : 0);
				if (byName != 0)
				{
					return byName;
				}

				int byY = a.Anchor.Y.CompareTo(b.Anchor.Y);
				return byY != 0 ? byY : a.Anchor.X.CompareTo(b.Anchor.X);
			});

			Refresh();
		}

		private void Refresh()
		{
			foreach (UIElement element in _spawned)
			{
				element.Remove();
			}

			_spawned.Clear();

			int perPage = System.Math.Max(1, (int)((Geo.H - 212f) / RowHeight));
			int pages = System.Math.Max(1, (_rows.Count + perPage - 1) / perPage);
			_page = System.Math.Clamp(_page, 0, pages - 1);

			_pageLabel.SetText(ModText.Format("StatusPageOf", "第 {0}/{1} 页", _page + 1, pages));

			if (_showPermissions)
			{
				BuildPermissionRows();
				return;
			}

			if (_showLocks)
			{
				// 编辑器自己画列表;这里只更新标题行
				if (TryGetSelectedQuiet(out Row lockRow))
				{
					if (_lockHost == null)
					{
						_lockHost = new LockListHost(lockRow.Anchor);
						_lockEditor.Bind(_lockHost, lockRow.Anchor);
					}

					_status.SetText(ModText.Get("BtnLocks", "锁定清单") + ": "
						+ NetworkRegistry.GetLocked(lockRow.Anchor).Count + " "
						+ ModText.Get("StatusItemsSuffix", "项")
						+ "    —    " + ModText.Get("HintLocked",
							"锁定物品只有所有者 / 管理者能取"));
				}
				else
				{
					_status.SetText(ModText.Get("MsgSelectNetwork", "先点一行选中一个网络"));
				}

				return;
			}

			if (_rows.Count == 0)
			{
				_status.SetText(ModText.Get("MsgNoNetworks", "这个世界里还没找到 ME 网络"));
				return;
			}

			_status.SetText(ModText.Format("MsgNetworkCount", "共 {0} 个网络", _rows.Count));

			int start = _page * perPage;
			int shown = System.Math.Min(perPage, _rows.Count - start);

			for (int i = 0; i < shown; i++)
			{
				int index = start + i;
				Row row = _rows[index];
				float top = 162f + i * RowHeight;

				string name = NetworkRegistry.GetName(row.Anchor)
					?? ModText.Get("LblUnnamed", "(未命名)");

				// 健康检查:直接说「缺什么导致什么不能用」,不用玩家自己点开终端试
				string warning = Diagnose(row);

				// 趋势:最近一段时间的增量(每分钟采一次样)
				bool hasTrend = NetworkHistory.TryGetDelta(row.Anchor, out long deltaBytes,
					out long deltaItems, out int minutes) && minutes > 0;

				float ratio = row.MaxBytes > 0 && row.MaxBytes != long.MaxValue
					? Math.Clamp((float)row.UsedBytes / row.MaxBytes, 0f, 1f)
					: 0f;

				var rowElement = new UIOverviewRow
				{
					Name = name,
					Position = "(" + row.Anchor.X + ", " + row.Anchor.Y + ")",
					Blocks = row.Blocks.Count,
					Types = row.Types,
					Items = row.Items,
					UsedBytes = row.UsedBytes,
					MaxBytes = row.MaxBytes,
					Composition = new[]
					{
						row.Drives, row.Units, row.Terminals, row.Assemblers, row.Cpus,
						row.Collectors, row.Buses, row.Bridges, row.Cables
					},
					Warning = warning,
					Severity = UIOverviewRow.SeverityOf(warning, ratio, row.MaxBytes),
					TrendBytes = hasTrend ? deltaBytes : 0,
					TrendItems = hasTrend ? deltaItems : 0,
					TrendMinutes = hasTrend ? minutes : 0,
					Selected = index == _selected,
					Striped = i % 2 == 1,
					OnClick = () =>
					{
						_selected = index;
						_nameBox.SetText(NetworkRegistry.GetName(row.Anchor) ?? string.Empty);
						Refresh();
					}
				};

				rowElement.Left.Set(Pad + 4f, 0f);
				rowElement.Top.Set(top - 4f, 0f);
				rowElement.Width.Set(System.Math.Max(120f, Geo.W - Pad * 2f - 8f), 0f);
				rowElement.Height.Set(RowHeight - 2f, 0f);
				Window.Append(rowElement);
				_spawned.Add(rowElement);
			}
		}

		/// <summary>
		/// 权限设置页:所有者 + 每个在线玩家的权限等级(点按钮循环切换)。
		/// 只有所有者 / 管理者能改,其他人在 MP 里只能看。
		/// </summary>
		private void BuildPermissionRows()
		{
			if (!TryGetSelectedQuiet(out Row row))
			{
				_status.SetText(ModText.Get("MsgSelectNetwork", "先点一行选中一个网络"));
				return;
			}

			string owner = NetworkRegistry.GetOwner(row.Anchor);
			string myName = Main.LocalPlayer?.name ?? string.Empty;
			int myLevel = NetworkRegistry.GetLevel(row.Anchor, myName);
			bool canManage = myLevel >= Systems.NetworkRegistry.LevelManage;

			if (!canManage && Main.netMode == NetmodeID.MultiplayerClient)
			{
				// 拿服务器上的真实权限
				MENet.RequestSecurity(row.Anchor);
			}

			_status.SetText(ModText.Get("LblOwner", "所有者") + ": "
				+ (string.IsNullOrEmpty(owner) ? ModText.Get("LblUnnamed", "(无主)")
					: owner)
				+ "    |    " + ModText.Get("LblMyLevel", "我的权限") + ": "
				+ LevelName(myLevel)
				+ (canManage ? string.Empty
					: "    " + ModText.Get("HintNeedManage", "(只有所有者能改)")));

			float top = 162f;

			foreach (Player player in Main.player)
			{
				if (player == null || !player.active || string.IsNullOrEmpty(player.name))
				{
					continue;
				}

				if (top > Geo.H - 44f)
				{
					break;
				}

				int level = NetworkRegistry.GetLevel(row.Anchor, player.name);

				var label = new UIText(player.name + "   " + LevelName(level), 0.78f, false);
				label.Left.Set(Pad + 8f, 0f);
				label.Top.Set(top, 0f);
				Window.Append(label);
				_spawned.Add(label);

				if (canManage)
				{
					string target = player.name;
					var cycle = new UIButton(ModText.Get("BtnCycleLevel", "切换权限"), 0.75f, false);
					cycle.OnLeftClick += (evt, element) =>
					{
						int next = (NetworkRegistry.GetLevel(row.Anchor, target) + 1) % 4;

						if (Main.netMode == NetmodeID.MultiplayerClient)
						{
							MENet.SetPermission(row.Anchor, target, next);
						}
						else
						{
							NetworkRegistry.SetLevel(row.Anchor, target, next);
						}

						Refresh();
					};
					cycle.Left.Set(Geo.W - Pad - 130f, 0f);
					cycle.Top.Set(top - 4f, 0f);
					cycle.Width.Set(122f, 0f);
					cycle.Height.Set(24f, 0f);
					Window.Append(cycle);
					_spawned.Add(cycle);
				}

				top += 28f;
			}

			// 存取日志(审计):最近 8 条,新在前
			// 物品级锁定:把手上的东西加进 / 移出锁定清单
			List<int> locked = NetworkRegistry.GetLocked(row.Anchor);
			int heldType = Main.LocalPlayer?.HeldItem?.type ?? 0;
			bool heldLocked = heldType > 0 && locked.Contains(heldType);

			var lockButton = new UIButton(heldLocked
				? ModText.Get("BtnUnlockHeld", "解锁手上物品")
				: ModText.Get("BtnLockHeld", "锁定手上物品"), 0.75f, false);
			lockButton.OnLeftClick += (evt, element) =>
			{
				if (heldType <= 0)
				{
					UIStatus.Toast(ModText.Get("MsgNoHeldItem", "手上没有物品"),
						new Color(255, 200, 150));
					return;
				}

				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.SetLocked(row.Anchor, heldType, !heldLocked);
				}
				else
				{
					NetworkRegistry.SetLocked(row.Anchor, heldType, !heldLocked);
				}

				Refresh();
			};
			lockButton.Left.Set(Pad + 8f, 0f);
			lockButton.Top.Set(top + 6f, 0f);
			lockButton.Width.Set(150f, 0f);
			lockButton.Height.Set(24f, 0f);
			Window.Append(lockButton);
			_spawned.Add(lockButton);

			var lockCount = new UIText(ModText.Get("LblLocked", "已锁定") + " " + locked.Count + " "
				+ ModText.Get("StatusItemsSuffix", "项")
				+ (locked.Count > 0 ? "  —  " + ModText.Get("HintLocked", "锁定物品只有所有者 / 管理者能取") : string.Empty),
				0.72f, false);
			lockCount.Left.Set(Pad + 166f, 0f);
			lockCount.Top.Set(top + 10f, 0f);
			lockCount.TextColor = locked.Count > 0 ? new Color(255, 210, 140) : new Color(190, 205, 230);
			Window.Append(lockCount);
			_spawned.Add(lockCount);

			top += 32f;

			List<string> audit = NetworkRegistry.GetAudit(row.Anchor);

			var header = new UIText(ModText.Get("LblAudit", "存取日志") + " (" + audit.Count + "):",
				0.75f, false);
			header.Left.Set(Pad + 8f, 0f);
			header.Top.Set(top + 6f, 0f);
			Window.Append(header);
			_spawned.Add(header);

			top += 28f;
			int shown = 0;

			foreach (string line in audit)
			{
				if (shown++ >= 8 || top > Geo.H - 30f)
				{
					break;
				}

				// 存的是 "时间|玩家|动作|物品|数量"
				string[] parts = line.Split('|');
				string text = parts.Length >= 5
					? parts[0] + "  " + parts[1] + "  " + parts[2] + "  "
						+ parts[3] + " x" + parts[4]
					: line;

				var logLine = new UIText(text, 0.72f, false);
				logLine.Left.Set(Pad + 16f, 0f);
				logLine.Top.Set(top, 0f);
				logLine.TextColor = new Color(190, 205, 230);
				Window.Append(logLine);
				_spawned.Add(logLine);

				top += 20f;
			}
		}

		private static string LevelName(int level)
		{
			return level switch
			{
				Systems.NetworkRegistry.LevelReadOnly => ModText.Get("LvlReadOnly", "只读"),
				Systems.NetworkRegistry.LevelUse => ModText.Get("LvlUse", "可存取"),
				Systems.NetworkRegistry.LevelManage => ModText.Get("LvlManage", "可管理"),
				_ => ModText.Get("LvlNone", "无权限")
			};
		}

		/// <summary>和 TryGetSelected 一样,但不弹提示(绑定刷新时用)。</summary>
		private bool TryGetSelectedQuiet(out Row row)
		{
			if (_selected >= 0 && _selected < _rows.Count)
			{
				row = _rows[_selected];
				return true;
			}

			row = null;
			return false;
		}

		private void UpdateTransferLabel()
		{
			if (_sourceButton == null)
			{
				return;
			}

			_sourceButton.SetText(_transferSource.X < 0
				? ModText.Get("BtnAsSource", "作为来源")
				: ModText.Get("BtnSourceIs", "来源已选"));
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			float w = Geo.W;
			float h = Geo.H;

			_hint.Left.Set(Pad + 8f, 0f);
			_hint.Top.Set(38f, 0f);

			_status.Left.Set(Pad + 8f, 0f);
			_status.Top.Set(60f, 0f);

			_nameBox.Left.Set(Pad + 8f, 0f);
			_nameBox.Top.Set(88f, 0f);
			_nameBox.Width.Set(System.Math.Max(160f, w - Pad * 2f - 260f), 0f);
			_nameBox.Height.Set(26f, 0f);

			_renameButton.Left.Set(w - Pad - 240f, 0f);
			_renameButton.Top.Set(88f, 0f);
			_renameButton.Width.Set(110f, 0f);
			_renameButton.Height.Set(26f, 0f);

			_refreshButton.Left.Set(w - Pad - 122f, 0f);
			_refreshButton.Top.Set(88f, 0f);
			_refreshButton.Width.Set(110f, 0f);
			_refreshButton.Height.Set(26f, 0f);

			_openTerminalButton.Left.Set(Pad + 8f, 0f);
			_openTerminalButton.Top.Set(122f, 0f);
			_openTerminalButton.Width.Set(180f, 0f);
			_openTerminalButton.Height.Set(26f, 0f);

			_bindButton.Left.Set(Pad + 196f, 0f);
			_bindButton.Top.Set(122f, 0f);
			_bindButton.Width.Set(140f, 0f);
			_bindButton.Height.Set(26f, 0f);

			_sourceButton.Left.Set(Pad + 344f, 0f);
			_sourceButton.Top.Set(122f, 0f);
			_sourceButton.Width.Set(120f, 0f);
			_sourceButton.Height.Set(26f, 0f);

			_transferButton.Left.Set(Pad + 472f, 0f);
			_transferButton.Top.Set(122f, 0f);
			_transferButton.Width.Set(130f, 0f);
			_transferButton.Height.Set(26f, 0f);

			_prevButton.Left.Set(Pad + 8f, 0f);
			_prevButton.Top.Set(h - 34f, 0f);
			_prevButton.Width.Set(96f, 0f);
			_prevButton.Height.Set(26f, 0f);

			_nextButton.Left.Set(Pad + 112f, 0f);
			_nextButton.Top.Set(h - 34f, 0f);
			_nextButton.Width.Set(96f, 0f);
			_nextButton.Height.Set(26f, 0f);

			_pageLabel.Left.Set(Pad + 220f, 0f);
			_pageLabel.Top.Set(h - 29f, 0f);

			_tabButton.Left.Set(w - Pad - 150f, 0f);
			_tabButton.Top.Set(h - 34f, 0f);
			_tabButton.Width.Set(146f, 0f);
			_tabButton.Height.Set(26f, 0f);

			_lockTabButton.Left.Set(w - Pad - 286f, 0f);
			_lockTabButton.Top.Set(h - 34f, 0f);
			_lockTabButton.Width.Set(130f, 0f);
			_lockTabButton.Height.Set(26f, 0f);

			// 锁定编辑器占据列表区域
			_lockEditor.Place(Pad + 4f, 150f,
				System.Math.Max(160f, w - Pad * 2f - 8f),
				System.Math.Max(120f, h - 150f - 44f));
			_lockEditor.Left.Set(_showLocks ? Pad + 4f : -10000f, 0f);
			_lockEditor.IgnoresMouseInteraction = !_showLocks;

			Window.Recalculate();
			Refresh();
		}
	}
}
