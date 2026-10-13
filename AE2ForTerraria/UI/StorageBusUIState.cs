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
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 存储总线的分区清单界面。
	///
	/// AE2 的存储总线是有分区的(只搬指定的几类东西),
	/// 之前这边只有导入/导出两个模式。现在:
	///   - 空清单 = 不过滤(和 AE2 默认一致)
	///   - 白名单 = 只搬清单里的;黑名单 = 清单里的不搬
	///   - 导入和导出**都**过这份清单
	/// </summary>
	public sealed class StorageBusUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = new WindowGeometry(620f, 500f, 500f, 380f);

		private MEStorageBusEntity _bus;

		private UIText _status;
		private UIText _hint;
		private UITextPanel<string> _modeButton;
		private UITextPanel<string> _listModeButton;
		private UITextPanel<string> _addHeldButton;
		private UITextPanel<string> _clearButton;
		private UITextPanel<string> _copyToAllButton;
		private UITextPanel<string> _exportListButton;
		private UITextPanel<string> _importListButton;
		private UITextBox _search;
		private UIItemGrid _grid;

		private readonly List<ItemKey> _candidates = new List<ItemKey>();
		private string _lastSearch;
		private double _clearConfirmUntil;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("TitleStorageBus", "ME 存储总线"));

			_status = new UIText(string.Empty, 0.8f, false);
			Window.Append(_status);

			_hint = new UIText(ModText.Get("HintStorageBus",
				"贴着原版箱子放置。空清单 = 不过滤;点格子勾选 / 取消。"), 0.72f, false);
			Window.Append(_hint);

			_modeButton = MakeButton("?", () =>
			{
				if (_bus == null)
				{
					return;
				}

				// 直接改模式(要先把 private set 拆开,所以走统一提交)
				_bus.SetExportMode(!_bus.ExportMode);
				Push();
			});

			_listModeButton = MakeButton("?", () =>
			{
				if (_bus == null)
				{
					return;
				}

				_bus.IsWhitelist = !_bus.IsWhitelist;
				_bus.MarkFilterDirty();
				Push();
			});

			_addHeldButton = MakeButton(ModText.Get("AddHeld", "加入手上物品"), () =>
			{
				if (_bus == null)
				{
					return;
				}

				Item held = Main.LocalPlayer?.HeldItem;
				if (held == null || held.type <= 0)
				{
					Main.NewText(ModText.Get("MsgNoHeldItem", "手上没有物品"), 255, 180, 180);
					return;
				}

				Toggle(held.type);
			});

			_clearButton = MakeButton(ModText.Get("ClearList", "清空清单"), () =>
			{
				if (_bus == null)
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
				_bus.Filter = new List<int>();
				_bus.MarkFilterDirty();
				Push();
			});

			// 批量:把这套分区清单复制到世界上所有存储总线
			_copyToAllButton = MakeButton(ModText.Get("BtnCopyToAllBuses", "复制到所有总线"), () =>
			{
				if (_bus == null)
				{
					return;
				}

				int count = CopyToAllBuses();
				UIStatus.Toast(ModText.Format("MsgCopiedToAllBuses",
					"已把分区清单复制到 {0} 个存储总线", count));
			});

			_exportListButton = MakeButton(ModText.Get("BtnExportList", "导出清单"), () =>
			{
				if (_bus == null)
				{
					return;
				}

				int written = ListIO.Save("bus", _bus.Filter ?? new List<int>());
				UIStatus.Toast(written >= 0
					? ModText.Format("MsgListExported", "已导出 {0} 项到 {1}", written, ListIO.Folder)
					: ModText.Get("MsgListExportFailed", "导出失败(看日志)"));
			});

			_importListButton = MakeButton(ModText.Get("BtnImportList", "导入清单"), () =>
			{
				if (_bus == null)
				{
					return;
				}

				var loaded = new List<int>();
				int read = ListIO.TryLoad("bus", loaded);

				if (read < 0)
				{
					UIStatus.Toast(ModText.Get("MsgListMissing",
						"没找到清单文件(先导出一次,或确认文件名是 bus.txt)"),
						new Color(255, 200, 150));
					return;
				}

				_bus.Filter = loaded;
				_bus.MarkFilterDirty();
				Push();
				RebuildCandidates();
				Refresh();
				UIStatus.Toast(ModText.Format("MsgListImported", "已导入 {0} 项", read));
			});

			_search = new UITextBox(string.Empty, 0.8f, false);
			Window.Append(_search);

			_grid = new UIItemGrid
			{
				IsMarked = key => _bus != null && _bus.Filter != null && _bus.Filter.Contains(key.Type)
			};
			_grid.OnLeftClick += (evt, element) =>
			{
				if (_grid.HoveredType > 0)
				{
					Toggle(_grid.HoveredType);
				}
			};
			Window.Append(_grid);

			ApplyLayout();
		}

		private UITextPanel<string> MakeButton(string text, Action onClick)
		{
			var button = new UIButton(text, 0.78f, false);
			button.OnLeftClick += (evt, element) => onClick();
			Window.Append(button);
			return button;
		}

		/// <summary>勾选 / 取消一个物品。</summary>
		private void Toggle(int type)
		{
			if (_bus == null || type <= 0)
			{
				return;
			}

			_bus.Filter ??= new List<int>();

			if (_bus.Filter.Contains(type))
			{
				_bus.Filter.Remove(type);
			}
			else
			{
				_bus.Filter.Add(type);
			}

			_bus.MarkFilterDirty();
			Push();
			RebuildCandidates();
			Refresh();
		}

		/// <summary>提交到服务器(多人)并刷新界面。</summary>
		/// <summary>把这套分区清单复制到世界上所有存储总线。</summary>
		private int CopyToAllBuses()
		{
			if (_bus == null)
			{
				return 0;
			}

			var targets = new List<MEStorageBusEntity>();

			foreach (KeyValuePair<Terraria.DataStructures.Point16, TileEntity> pair in TileEntity.ByPosition)
			{
				if (pair.Value is MEStorageBusEntity bus && !ReferenceEquals(bus, _bus))
				{
					targets.Add(bus);
				}
			}

			foreach (MEStorageBusEntity target in targets)
			{
				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.SendBusConfig(target.Position, _bus.ExportMode, _bus.IsWhitelist, _bus.Filter);
					continue;
				}

				target.SetExportMode(_bus.ExportMode);
				target.IsWhitelist = _bus.IsWhitelist;
				target.Filter = new List<int>(_bus.Filter ?? new List<int>());
				target.MarkFilterDirty();

				if (Main.netMode == NetmodeID.Server)
				{
					MENet.SyncBusConfig(target.Position);
				}
			}

			return targets.Count;
		}

		private void Push()
		{
			if (_bus == null)
			{
				return;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.SendBusConfig(_bus.Position, _bus.ExportMode, _bus.IsWhitelist, _bus.Filter);
			}

			Refresh();
		}

		public void SetBus(MEStorageBusEntity bus)
		{
			_bus = bus;
			RebuildCandidates();
			Refresh();
		}

		protected override void UpdateWindow(GameTime gameTime)
		{
			string query = _search?.Text?.Trim() ?? string.Empty;
			if (query != _lastSearch)
			{
				RebuildCandidates();
			}

			Refresh();
		}

		private void RebuildCandidates()
		{
			_candidates.Clear();

			if (_bus == null)
			{
				return;
			}

			string query = _search?.Text?.Trim() ?? string.Empty;
			_lastSearch = query;

			if (query.Length == 0)
			{
				foreach (int type in _bus.Filter ?? new List<int>())
				{
					if (type > 0)
					{
						_candidates.Add(ItemKey.Crafted(type));
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
			_grid?.SetKeys(_candidates, resetPage: false);
		}

		private void Refresh()
		{
			if (_bus == null || _status == null)
			{
				_status?.SetText(ModText.Get("StatusNoBus", "没有绑定存储总线"));
				return;
			}

			int count = _bus.Filter?.Count ?? 0;

			UITextCache.Set(_status, ModText.Get("StatusMode", "模式") + ": "
				+ (_bus.ExportMode
					? ModText.Get("MsgBusExport", "存储总线:导出(网络 → 箱子)")
					: ModText.Get("MsgBusImport", "存储总线:导入(箱子 → 网络)"))
				+ "    |    " + ModText.Get("StatusList", "清单") + ": " + count + " "
				+ ModText.Get("StatusItemsSuffix", "项")
				+ " (" + (count == 0
					? ModText.Get("StatusNoFilter", "不过滤")
					: _bus.IsWhitelist
						? ModText.Get("StatusWhitelist", "白名单(只收清单里的)")
						: ModText.Get("StatusBlacklist", "黑名单(清单里的不收)"))
				+ ")");

			_modeButton.SetText(_bus.ExportMode
				? ModText.Get("BtnSwitchToImport", "改为导入")
				: ModText.Get("BtnSwitchToExport", "改为导出"));

			_listModeButton.SetText(_bus.IsWhitelist
				? ModText.Get("BtnToBlacklist", "改为黑名单")
				: ModText.Get("BtnToWhitelist", "改为白名单"));
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			_status.Left.Set(Pad + 8f, 0f);
			_status.Top.Set(38f, 0f);

			_hint.Left.Set(Pad + 8f, 0f);
			_hint.Top.Set(60f, 0f);

			float x = Pad + 8f;
			float y = 86f;
			Place(_modeButton, ref x, y, 110f);
			Place(_listModeButton, ref x, y, 130f);

			x = Pad + 8f;
			y += 34f;
			Place(_addHeldButton, ref x, y, 172f);
			Place(_clearButton, ref x, y, 132f);

			x = Pad + 8f;
			y += 34f;
			Place(_copyToAllButton, ref x, y, 170f);
			Place(_exportListButton, ref x, y, 110f);
			Place(_importListButton, ref x, y, 110f);

			_search.Left.Set(Pad + 8f, 0f);
			_search.Top.Set(192f, 0f);
			_search.Width.Set(Math.Max(160f, Geo.W - Pad * 2f - 16f), 0f);
			_search.Height.Set(24f, 0f);

			_grid.Left.Set(Pad + 4f, 0f);
			_grid.Top.Set(224f, 0f);
			_grid.Width.Set(Math.Max(120f, Geo.W - Pad * 2f - 8f), 0f);
			_grid.Height.Set(Math.Max(80f, Geo.H - 224f - 16f), 0f);

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
	}
}
