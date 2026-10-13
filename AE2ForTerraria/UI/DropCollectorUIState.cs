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
	/// 掉落收集器的过滤界面。
	///
	/// 关键变化:设置保存在**这一块**收集器的方块实体里,不再所有收集器共用一份全局配置。
	/// 多人下由服务器持有真值,客户端改完发给服务器,服务器再广播回来。
	/// </summary>
	public sealed class DropCollectorUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = new WindowGeometry(620f, 520f, 500f, 400f);
		// 注意:窗口高度按新的排布改过一次(内容网格往下挪了 38px),
		// 所以最小高度也跟着提一点,避免网格被压扁。

		private UIText _status;
		private UIText _list;
		private UITextPanel<string> _enableButton;
		private UITextPanel<string> _modeButton;
		private UITextPanel<string> _radiusDownButton;
		private UITextPanel<string> _radiusUpButton;
		private UITextPanel<string> _addHeldButton;
		private UITextPanel<string> _removeLastButton;
		private UITextPanel<string> _clearButton;
		private UITextPanel<string> _copyGlobalButton;
		private UITextPanel<string> _copyToAllButton;
		private UITextPanel<string> _exportListButton;
		private UITextPanel<string> _importListButton;
		private UITextPanel<string> _pageButton;

		private MEDropCollectorEntity _collector;
		private int _listPage;
		private double _clearConfirmUntil;

		/// <summary>
		/// 可搜索的勾选网格。
		/// 以前只能「手持物品 → 加入/移出」,清单长了根本没法维护。
		/// 搜索框留空时列出清单本身,输入后列出所有匹配的物品,点一下切换。
		/// </summary>
		private UITextBox _filterSearch;
		private UIItemGrid _filterGrid;
		private readonly List<ItemKey> _candidates = new List<ItemKey>();
		private string _lastSearch;

		/// <summary>超过多少项就分页显示。以前只显示前 10 项。</summary>
		private const int PerPage = 14;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("TitleCollector", "ME 掉落收集器"));

			_status = new UIText(string.Empty, 0.8f, false);
			Window.Append(_status);

			_enableButton = MakeButton(ModText.Get("StatusCollector", "开关") + ": ?", () =>
			{
				if (_collector == null)
				{
					return;
				}

				_collector.Enabled = !_collector.Enabled;
				Push();
			});

			_modeButton = MakeButton("?", () =>
			{
				if (_collector == null)
				{
					return;
				}

				_collector.IsBlacklist = !_collector.IsBlacklist;
				Push();
			});

			_radiusDownButton = MakeButton(ModText.Get("BtnRadiusDown", "半径 -4"), () =>
			{
				if (_collector == null)
				{
					return;
				}

				_collector.Radius = System.Math.Max(2f, _collector.Radius - 4f);
				Push();
			});

			_radiusUpButton = MakeButton(ModText.Get("BtnRadiusUp", "半径 +4"), () =>
			{
				if (_collector == null)
				{
					return;
				}

				_collector.Radius = System.Math.Min(128f, _collector.Radius + 4f);
				Push();
			});

			_addHeldButton = MakeButton(ModText.Get("AddHeld", "加入手上物品"), () =>
			{
				if (_collector == null)
				{
					return;
				}

				Item held = Main.LocalPlayer?.HeldItem;
				if (held == null || held.type <= 0)
				{
					Main.NewText(ModText.Get("MsgNoHeldItem", "手上没有物品"), 255, 180, 180);
					return;
				}

				_collector.Filter ??= new List<int>();

				if (_collector.Filter.Contains(held.type))
				{
					_collector.Filter.Remove(held.type);
				}
				else
				{
					_collector.Filter.Add(held.type);
				}

				Push();
			});

			_removeLastButton = MakeButton(ModText.Get("RemoveLast", "移出清单最后一项"), () =>
			{
				if (_collector?.Filter == null || _collector.Filter.Count == 0)
				{
					return;
				}

				_collector.Filter.RemoveAt(_collector.Filter.Count - 1);
				Push();
			});

			_clearButton = MakeButton(ModText.Get("ClearList", "清空清单"), () =>
			{
				if (_collector == null)
				{
					return;
				}

				if (UIStatus.Now > _clearConfirmUntil)
				{
					_clearConfirmUntil = UIStatus.Now + 2.0;
					_clearButton.SetText(ModText.Get("ConfirmClearList", "再点一次确认清空清单"));
					return;
				}

				_clearConfirmUntil = 0;
				_clearButton.SetText(ModText.Get("ClearList", "清空清单"));
				_collector.Filter = new List<int>();
				Push();
			});

			_copyGlobalButton = MakeButton(ModText.Get("BtnCopyGlobal", "复制全局清单"), () =>
			{
				_collector?.ApplyDefaultsFromConfig();
				Push();
			});

			// 批量:把这套设置复制到世界上所有收集器
			// (在几十个模组的环境里一个个点太累了)
			_copyToAllButton = MakeButton(ModText.Get("BtnCopyToAll", "复制到所有收集器"), () =>
			{
				if (_collector == null)
				{
					return;
				}

				int count = CopyToAllCollectors();
				UIStatus.Toast(ModText.Format("MsgCopiedToAll", "已把设置复制到 {0} 个收集器", count));
			});

			_exportListButton = MakeButton(ModText.Get("BtnExportList", "导出清单"), () =>
			{
				if (_collector == null)
				{
					return;
				}

				int written = ListIO.Save("collector", _collector.Filter ?? new List<int>());
				UIStatus.Toast(written >= 0
					? ModText.Format("MsgListExported", "已导出 {0} 项到 {1}", written, ListIO.Folder)
					: ModText.Get("MsgListExportFailed", "导出失败(看日志)"));
			});

			_importListButton = MakeButton(ModText.Get("BtnImportList", "导入清单"), () =>
			{
				if (_collector == null)
				{
					return;
				}

				var loaded = new List<int>();
				int read = ListIO.TryLoad("collector", loaded);

				if (read < 0)
				{
					UIStatus.Toast(ModText.Get("MsgListMissing",
						"没找到清单文件(先导出一次,或确认文件名是 collector.txt)"),
						new Color(255, 200, 150));
					return;
				}

				_collector.Filter = loaded;
				_collector.MarkFilterDirty();
				Push();
				RebuildCandidates();
				Refresh();
				UIStatus.Toast(ModText.Format("MsgListImported", "已导入 {0} 项", read));
			});

			_pageButton = MakeButton(ModText.Get("BtnNextDrivePage", "内容翻页"), () =>
			{
				_listPage++;
				Refresh();
			});

			_list = new UIText(string.Empty, 0.75f, false);
			Window.Append(_list);

			_filterSearch = new UITextBox(string.Empty, 0.8f, false);
			Window.Append(_filterSearch);

			_filterGrid = new UIItemGrid
			{
				// 绿边 = 已经在清单里
				IsMarked = key => _collector != null
					&& _collector.Filter != null
					&& _collector.Filter.Contains(key.Type)
			};
			_filterGrid.OnLeftClick += (evt, element) => ToggleHovered();
			Window.Append(_filterGrid);

			ApplyLayout();
		}

		private UITextPanel<string> MakeButton(string text, System.Action onClick)
		{
			var button = new UIButton(text, 0.78f, false);
			button.OnLeftClick += (evt, element) => onClick();
			Window.Append(button);
			return button;
		}

		/// <summary>把改动落到实体上,并在多人下发给服务器。</summary>
		private void Push()
		{
			if (_collector == null)
			{
				return;
			}

			_collector.MarkFilterDirty();

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.SendCollectorConfig(_collector.Position, _collector.Enabled, _collector.Radius,
					_collector.IsBlacklist, _collector.Filter);
			}

			Refresh();
		}

		/// <summary>点一下网格里的格子:在清单里就移出,不在就加进去。</summary>
		/// <summary>
		/// 把这套设置(开关 / 半径 / 白黑名单 / 清单)复制到世界上**所有**收集器。
		/// 多人下逐个发给服务器。
		/// </summary>
		private int CopyToAllCollectors()
		{
			if (_collector == null)
			{
				return 0;
			}

			var targets = new List<MEDropCollectorEntity>();

			foreach (KeyValuePair<Terraria.DataStructures.Point16, TileEntity> pair in TileEntity.ByPosition)
			{
				if (pair.Value is MEDropCollectorEntity collector && !ReferenceEquals(collector, _collector))
				{
					targets.Add(collector);
				}
			}

			foreach (MEDropCollectorEntity target in targets)
			{
				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.SendCollectorConfig(target.Position, _collector.Enabled, _collector.Radius,
						_collector.IsBlacklist, _collector.Filter);
					continue;
				}

				target.Enabled = _collector.Enabled;
				target.Radius = _collector.Radius;
				target.IsBlacklist = _collector.IsBlacklist;
				target.Filter = new List<int>(_collector.Filter ?? new List<int>());
				target.MarkFilterDirty();

				if (Main.netMode == NetmodeID.Server)
				{
					MENet.SyncCollector(target.Position);
				}
			}

			return targets.Count;
		}

		private void ToggleHovered()
		{
			if (_collector == null || _filterGrid == null)
			{
				return;
			}

			ItemKey key = _filterGrid.HoveredKey;
			if (key.IsAir)
			{
				return;
			}

			_collector.Filter ??= new List<int>();

			if (_collector.Filter.Contains(key.Type))
			{
				_collector.Filter.Remove(key.Type);
			}
			else
			{
				_collector.Filter.Add(key.Type);
			}

			Push();
			RebuildCandidates();
			Refresh();
		}

		/// <summary>
		/// 重建候选列表:搜索框为空时列出清单本身,有搜索词时列出所有匹配的物品。
		/// </summary>
		private void RebuildCandidates()
		{
			_candidates.Clear();

			if (_collector == null)
			{
				return;
			}

			string query = _filterSearch?.Text?.Trim() ?? string.Empty;
			_lastSearch = query;

			if (query.Length == 0)
			{
				// 没搜索:显示当前清单(方便逐项点掉)
				if (_collector.Filter != null)
				{
					foreach (int type in _collector.Filter)
					{
						if (type > 0)
						{
							_candidates.Add(ItemKey.Crafted(type));
						}
					}
				}
			}
			else
			{
				int total = Terraria.ModLoader.ItemLoader.ItemCount;
				for (int type = 1; type < total; type++)
				{
					if (string.IsNullOrWhiteSpace(Lang.GetItemNameValue(type)))
					{
						continue;
					}

					if (PinyinIndex.Matches(type, query))
					{
						_candidates.Add(ItemKey.Crafted(type));
					}
				}
			}

			_candidates.Sort((a, b) => a.Type.CompareTo(b.Type));
			_filterGrid?.SetKeys(_candidates, resetPage: false);
		}

		public void SetCollector(MEDropCollectorEntity collector)
		{
			_collector = collector;
			_listPage = 0;
			RebuildCandidates();
			Refresh();
		}

		protected override void UpdateWindow(GameTime gameTime)
		{
			string query = _filterSearch?.Text?.Trim() ?? string.Empty;
			if (query != _lastSearch)
			{
				RebuildCandidates();
			}

			Refresh();
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			_status.Left.Set(Pad + 8f, 0f);
			_status.Top.Set(36f, 0f);

			float y = 74f;
			float x = Pad + 8f;
			float bw = 132f;

			Place(_enableButton, ref x, y, bw);
			Place(_modeButton, ref x, y, bw);
			Place(_radiusDownButton, ref x, y, bw);
			Place(_radiusUpButton, ref x, y, bw);

			x = Pad + 8f;
			y += 36f;
			Place(_addHeldButton, ref x, y, 172f);
			Place(_removeLastButton, ref x, y, 190f);

			x = Pad + 8f;
			y += 36f;
			Place(_clearButton, ref x, y, 132f);
			Place(_copyGlobalButton, ref x, y, 150f);
			Place(_pageButton, ref x, y, 110f);

			x = Pad + 8f;
			y += 36f;
			Place(_copyToAllButton, ref x, y, 170f);
			Place(_exportListButton, ref x, y, 110f);
			Place(_importListButton, ref x, y, 110f);

			_list.Left.Set(Pad + 8f, 0f);
			_list.Top.Set(228f, 0f);

			// 搜索框 + 勾选网格占住下半部分
			_filterSearch.Left.Set(Pad + 8f, 0f);
			_filterSearch.Top.Set(290f, 0f);
			_filterSearch.Width.Set(Math.Max(160f, Geo.W - Pad * 2f - 16f), 0f);
			_filterSearch.Height.Set(24f, 0f);

			_filterGrid.Left.Set(Pad + 4f, 0f);
			_filterGrid.Top.Set(322f, 0f);
			_filterGrid.Width.Set(Math.Max(120f, Geo.W - Pad * 2f - 8f), 0f);
			_filterGrid.Height.Set(Math.Max(80f, Geo.H - 322f - 16f), 0f);

			Window.Recalculate();
		}

		private static void Place(UIElement element, ref float x, float y, float width)
		{
			element.Left.Set(x, 0f);
			element.Top.Set(y, 0f);
			element.Width.Set(width, 0f);
			element.Height.Set(28f, 0f);
			x += width + 8f;
		}

		private void Refresh()
		{
			if (_collector == null || _status == null)
			{
				_status?.SetText(ModText.Get("StatusNoCollector", "没有绑定收集器"));
				return;
			}

			UITextCache.Set(_status, ModText.Get("StatusCollector", "开关") + ": "
				+ (_collector.Enabled ? ModText.Get("StatusCollectorOn", "开") : ModText.Get("StatusCollectorOff", "关"))
				+ "    |    " + ModText.Get("StatusRadius", "半径") + ": "
				+ _collector.Radius.ToString("0") + " " + ModText.Get("StatusTiles", "格")
				+ "    |    " + ModText.Get("StatusMode", "模式") + ": "
				+ (_collector.IsBlacklist
					? ModText.Get("StatusBlacklist", "黑名单(清单里的不收)")
					: ModText.Get("StatusWhitelist", "白名单(只收清单里的)")));

			_enableButton.SetText(_collector.Enabled
				? ModText.Get("BtnTurnOff", "点击关闭")
				: ModText.Get("BtnTurnOn", "点击启用"));
			_modeButton.SetText(_collector.IsBlacklist
				? ModText.Get("BtnToWhitelist", "改为白名单")
				: ModText.Get("BtnToBlacklist", "改为黑名单"));

			List<int> filter = _collector.Filter ?? new List<int>();
			var lines = new List<string>();

			lines.Add(ModText.Get("StatusList", "清单") + "(" + filter.Count + " "
				+ ModText.Get("StatusItemsSuffix", "项") + "):");

			lines.Add(_lastSearch != null && _lastSearch.Length > 0
				? ModText.Format("StatusSearchResult", "搜索:「{0}」,命中 {1} 项,点格子加入 / 移出",
					_lastSearch, _candidates.Count)
				: ModText.Get("HintFilterGrid", "下面就是清单本身,点格子可以移出;在上方输入名字 / 拼音搜索其它物品"));

			lines.Add(string.Empty);
			lines.Add(ModText.Get("HintCollector",
				"手持物品后点「加入手上物品」,可以把它加进 / 移出这块收集器自己的清单。"));

			_list.SetText(string.Join("\n", lines));
		}
	}
}
