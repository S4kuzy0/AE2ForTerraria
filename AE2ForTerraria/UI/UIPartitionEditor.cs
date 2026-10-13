using System;
using System.Collections.Generic;
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
	/// 存储方块的分区清单编辑:驱动器 / 存储单元共用。
	///
	/// 「这块盘只放矿石」就是把它设成**白名单**再勾上矿石;
	/// 空清单 = 不过滤(和存储总线一致)。
	/// 按物品 ID 判断,不看词缀。
	/// </summary>
	public sealed class UIPartitionEditor : UIElement
	{
		private IPartitionedStorage _storage;
		private Point16 _position;

		private UIText _hint;
		private UITextPanel<string> _modeButton;
		private UITextPanel<string> _clearButton;
		private UITextBox _search;
		private UITextPanel<string> _exportButton;
		private UITextPanel<string> _importButton;
		private UIItemGrid _grid;

		private readonly List<ItemKey> _candidates = new List<ItemKey>();
		private string _lastSearch;
		private double _clearConfirmUntil;

		/// <summary>
		/// 锁定清单这类「没有白/黑名单概念」的清单,可以把模式按钮藏掉。
		/// 复用同一个编辑器,不用再写第二份勾选网格。
		/// </summary>
		public bool ShowModeButton = true;

		/// <summary>提示文案的键名(分区 / 锁定共用同一套网格)。</summary>
		public string HintKey = "HintPartition";
		public string HintFallback = "分区:空清单 = 不过滤;点格子勾选 / 取消,搜索支持中文 / 拼音";

		/// <summary>控件是否已经建好。见 <see cref="OnInitialize"/> 的说明。</summary>
		private bool _built;

		public override void OnInitialize()
		{
			// 和 UIStorageContents 同理:宿主可能在它自己的 OnInitialize 里
			// 先调 Place(),那时本编辑器的控件还没建。标记保证只建一遍。
			if (_built)
			{
				return;
			}

			_built = true;

			_hint = new UIText(string.Empty, 0.72f, false);
			Append(_hint);

			_modeButton = new UIButton(string.Empty, 0.78f, false);
			_modeButton.OnLeftClick += (evt, element) =>
			{
				if (_storage == null)
				{
					return;
				}

				_storage.IsWhitelist = !_storage.IsWhitelist;
				_storage.MarkFilterDirty();
				Push();
				Refresh();
			};
			Append(_modeButton);

			_clearButton = new UIButton(ModText.Get("ClearList", "清空清单"), 0.78f, false);
			_clearButton.OnLeftClick += (evt, element) =>
			{
				if (_storage == null)
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
				_storage.Filter = new List<int>();
				_storage.MarkFilterDirty();
				Push();
				RebuildCandidates();
				Refresh();
			};
			Append(_clearButton);

			_search = new UITextBox(string.Empty, 0.8f, false);
			Append(_search);

			// 导入 / 导出:换存档 / 换电脑不用重新点一遍
			_exportButton = new UIButton(ModText.Get("BtnExportList", "导出"), 0.78f, false);
			_exportButton.OnLeftClick += (evt, element) =>
			{
				if (_storage == null)
				{
					return;
				}

				int count = ListIO.Save("partition", _storage.Filter ?? new List<int>());
				UIStatus.Toast(count >= 0
					? ModText.Format("MsgListExported", "已导出 {0} 项到 {1}", count, ListIO.Folder)
					: ModText.Get("MsgListExportFailed", "导出失败(看日志)"),
					count >= 0 ? new Color(150, 220, 255) : new Color(255, 180, 150));
			};
			Append(_exportButton);

			_importButton = new UIButton(ModText.Get("BtnImportList", "导入"), 0.78f, false);
			_importButton.OnLeftClick += (evt, element) =>
			{
				if (_storage == null)
				{
					return;
				}

				var loaded = new List<int>();
				int count = ListIO.TryLoad("partition", loaded);

				if (count < 0)
				{
					UIStatus.Toast(ModText.Get("MsgListMissing",
						"没找到清单文件(先导出一次试试)"), new Color(255, 200, 150));
					return;
				}

				_storage.Filter = loaded;
				_storage.MarkFilterDirty();
				Push();
				RebuildCandidates();
				Refresh();
				UIStatus.Toast(ModText.Format("MsgListImported", "已导入 {0} 项", count));
			};
			Append(_importButton);

			_grid = new UIItemGrid
			{
				IsMarked = key => _storage?.Filter != null && _storage.Filter.Contains(key.Type)
			};
			_grid.OnLeftClick += (evt, element) =>
			{
				if (_grid.HoveredType > 0)
				{
					Toggle(_grid.HoveredType);
				}
			};
			Append(_grid);
		}

		public void Bind(IPartitionedStorage storage, Point16 position)
		{
			_storage = storage;
			_position = position;
			_lastSearch = null;
			_search?.SetText(string.Empty);
			RebuildCandidates();
			Refresh();
		}

		public void Place(float x, float y, float width, float height)
		{
			// 和 UIStorageContents.Place 同理:宿主在 OnInitialize 里就调过来了,
			// 这时候本编辑器的控件还没建出来。按需补一次(只建一遍)。
			if (!_built)
			{
				OnInitialize();
			}

			Left.Set(x, 0f);
			Top.Set(y, 0f);
			Width.Set(width, 0f);
			Height.Set(height, 0f);

			_hint.Left.Set(0f, 0f);
			_hint.Top.Set(0f, 0f);
			_hint.Width.Set(width, 0f);
			_hint.Height.Set(20f, 0f);

            _hint.DynamicallyScaleDownToWidth=true;
            UIElement[] buttons=ShowModeButton ? new UIElement[] {_modeButton,_clearButton,_exportButton,_importButton}
                : new UIElement[] {_clearButton,_exportButton,_importButton};
            _modeButton.Left.Set(-10000f,0f);
            int columns=Math.Max(1,Math.Min(buttons.Length,(int)(width/124f)));
            int rows=(buttons.Length+columns-1)/columns;
            float buttonWidth=(width-(columns-1)*6f)/columns;
            for (int i=0;i<buttons.Length;i++)
            {
                buttons[i].Left.Set((i%columns)*(buttonWidth+6f),0f);
                buttons[i].Top.Set(24f+(i/columns)*30f,0f);
                buttons[i].Width.Set(buttonWidth,0f); buttons[i].Height.Set(26f,0f);
            }
            float searchTop=24f+rows*30f;
            _search.Left.Set(0f,0f); _search.Top.Set(searchTop,0f);
            _search.Width.Set(width,0f); _search.Height.Set(24f,0f);
            _grid.Left.Set(0f,0f); _grid.Top.Set(searchTop+32f,0f);
            _grid.Width.Set(width,0f); _grid.Height.Set(Math.Max(20f,height-searchTop-32f),0f);

			Recalculate();
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			string query = _search?.Text?.Trim() ?? string.Empty;
			if (query != _lastSearch)
			{
				RebuildCandidates();
				Refresh();
			}
		}

		private void Toggle(int type)
		{
			if (_storage == null || type <= 0)
			{
				return;
			}

			_storage.Filter ??= new List<int>();

			if (_storage.Filter.Contains(type))
			{
				_storage.Filter.Remove(type);
			}
			else
			{
				_storage.Filter.Add(type);
			}

			_storage.MarkFilterDirty();
			Push();
			RebuildCandidates();
			Refresh();
		}

		/// <summary>改动落盘 + 多人转发。</summary>
		private void Push()
		{
			if (_storage == null)
			{
				return;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.SendStorageFilter(_position, _storage.IsWhitelist, _storage.Filter);
			}
		}

		private void RebuildCandidates()
		{
			_candidates.Clear();

			if (_storage == null)
			{
				return;
			}

			string query = _search?.Text?.Trim() ?? string.Empty;
			_lastSearch = query;

			if (query.Length == 0)
			{
				foreach (int type in _storage.Filter ?? new List<int>())
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
			if (_storage == null || _hint == null)
			{
				return;
			}

			int count = _storage.Filter?.Count ?? 0;

			_hint.SetText(ModText.Get(HintKey, HintFallback)
				+ "   [" + (count == 0
					? ModText.Get("StatusNoFilter", "不过滤")
					: _storage.IsWhitelist
						? ModText.Get("StatusWhitelist", "白名单(只收清单里的)")
						: ModText.Get("StatusBlacklist", "黑名单(清单里的不收)"))
				+ "]");

			_modeButton.SetText(_storage.IsWhitelist
				? ModText.Get("BtnToBlacklist", "改为黑名单")
				: ModText.Get("BtnToWhitelist", "改为白名单"));
		}
	}
}
