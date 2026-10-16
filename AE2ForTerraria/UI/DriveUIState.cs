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
	/// <summary>磁盘驱动器界面:排列磁盘槽位、显示容量占用,并列出已存了什么。</summary>
	public sealed class DriveUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = WindowGeometry.Compact();

		private readonly List<UIItemSlotElement> _slots = new List<UIItemSlotElement>();
		private UIText _capacity;
		private UIText _hint;
		private UIStorageContents _contents;
		private UIPartitionEditor _partition;
		private UITextPanel<string> _tabButton;
		private bool _showPartition;
		private float _panelX;
		private MEDiskDriveEntity _drive;
		private UIPanel _capacityBar;
		private UIPanel _capacityFill;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("TitleDrive", "ME 磁盘驱动器"));

			_hint = new UIText("左键取放磁盘，右键一个", 0.72f, false);
			Window.Append(_hint);

			_capacity = new UIText(string.Empty, 0.82f, false);
			Window.Append(_capacity);

			// 容量进度条:以前只有一串数字,看不出快满了
			_capacityBar = new UIFlatPanel();
			_capacityBar.BackgroundColor = new Color(18, 22, 38);
			Window.Append(_capacityBar);

			_capacityFill = new UIFlatPanel();
			_capacityFill.BackgroundColor = new Color(110, 200, 255);
			_capacityBar.Append(_capacityFill);

			// 内容面板和存储单元共用同一块(搜索 / 网格 / 翻页 / 全部取出)
			_contents = new UIStorageContents { OnChanged = Refresh };
			Window.Append(_contents);

			// 分区清单(「这块盘只放矿石」)
			_partition = new UIPartitionEditor();
			Window.Append(_partition);

			_tabButton = new UIButton(ModText.Get("BtnShowPartition", "分区清单"), 0.75f, false);
			_tabButton.OnLeftClick += (evt, element) =>
			{
				_showPartition = !_showPartition;
				_tabButton.SetText(_showPartition
					? ModText.Get("BtnShowContents", "查看内容")
					: ModText.Get("BtnShowPartition", "分区清单"));
				ApplyPanelVisibility();
			};
			Window.Append(_tabButton);

			for (int i = 0; i < MEDiskDriveEntity.DiskSlots; i++)
			{
				int index = i;
				UIItemSlotElement slot = UIItemSlotElement.Create(
					() => _drive != null ? _drive.Disks[index] : new Item(),
					item =>
					{
						if (_drive != null)
						{
							_drive.Disks[index] = item;
						}
					},
					MEDiskDriveEntity.IsDisk,
					item => _drive != null && _drive.CanRemoveDisk(index, out _),
					OnDisksChanged);

                slot.InteractionHandler = right =>
                {
                    if (_drive == null) return true;
                    if (MENet.SlotAction(_drive.Position, index, false, right)) return true;
                    Item cursor = Main.mouseItem;
                    if (ContainerSlotOperations.TryChange(_drive.Disks, index, ref cursor, right, _drive, out string reason))
                    { Main.mouseItem = cursor; OnDisksChanged(); }
                    else if (reason != null) UIStatus.Toast(reason, UITheme.Warn);
                    return true;
                };
				Window.Append(slot);
				_slots.Add(slot);
			}

			RequestLayout();
		}

		protected override void ApplyLayout()
		{
            base.ApplyLayout();

            float diskStep = Geo.W < 600 ? 34 : 52;
            float leftWidth = diskStep * 5;
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].Left.Set(Pad + i % 5 * diskStep, 0);
                _slots[i].Top.Set(62 + i / 5 * diskStep, 0);
                _slots[i].Width.Set(diskStep - 4, 0); _slots[i].Height.Set(diskStep - 4, 0);
            }
            _hint.Left.Set(Pad, 0); _hint.Top.Set(36, 0);
            _hint.Width.Set(leftWidth, 0); _hint.DynamicallyScaleDownToWidth = true;
            _capacity.Left.Set(Pad, 0); _capacity.Top.Set(70 + diskStep * 2, 0);
            _capacity.Width.Set(leftWidth, 0); _capacity.Height.Set(44, 0);
            _capacity.DynamicallyScaleDownToWidth = true;
            _capacityBar.Left.Set(Pad, 0); _capacityBar.Top.Set(122 + diskStep * 2, 0);
            _capacityBar.Width.Set(leftWidth, 0); _capacityBar.Height.Set(8, 0);
            _panelX = Pad + leftWidth + 10;
            float width = Geo.W - _panelX - Pad;
            _contents.Place(_panelX, 32, width, Geo.H - 72);
            _partition.Place(_panelX, 32, width, Geo.H - 72);
            _tabButton.Left.Set(_panelX, 0); _tabButton.Top.Set(Geo.H - 32, 0);
            _tabButton.Width.Set(100, 0); _tabButton.Height.Set(22, 0);
            Window.Recalculate(); ApplyPanelVisibility();
        }

		/// <summary>内容面板和分区编辑同一时间只显示一个。</summary>
		private void ApplyPanelVisibility()
		{
			if (_contents == null || _partition == null)
			{
				return;
			}

			_contents.IgnoresMouseInteraction = _showPartition;
			_partition.IgnoresMouseInteraction = !_showPartition;

			// 用「挪出屏幕」而不是删除,免得反复 Append/Remove 造成状态丢失
			_contents.Left.Set(_showPartition ? -10000f : _panelX, 0f);
			_partition.Left.Set(_showPartition ? _panelX : -10000f, 0f);
            _contents.Recalculate(); _partition.Recalculate();
		}

		public void SetDrive(MEDiskDriveEntity drive)
		{
			_drive = drive;
			_contents.Bind(drive, drive.Position);
			_partition.Bind(drive, drive.Position);
			Refresh();
		}

		protected override void UpdateWindow(GameTime gameTime)
		{
			Refresh();
		}

		/// <summary>
		/// 磁盘槽位改动:单人直接刷新；多人由槽位意图请求处理。
		/// 服务器会重算容量并广播快照(以前插拔磁盘服务器完全不知道)。
		/// </summary>
		private void OnDisksChanged()
		{
			if (_drive != null)
			{
				_drive.RefreshCapacity();
				MENet.SendDisks(_drive.Position, _drive.Disks);
			}

			Refresh();
		}

		private void Refresh()
		{
		
			if (_drive == null || _capacity == null)
			{
				return;
			}

			_drive.RefreshCapacity();

            var ledger = _drive.Ledger;
            string capacity = ledger.MaxTypes == 0 ? "未安装磁盘"
                : UIFormat.Count(ledger.StoredTypeCount) + " / " + UIFormat.Count(ledger.MaxTypes) + " 类";
            UITextCache.Set(_capacity, capacity + "\n已存 " + UIFormat.Count(ledger.StoredTotalAmount) + " 件物品");

			// 进度条
			float ratio = ledger.MaxTypes > 0
					? Math.Clamp((float)ledger.StoredTypeCount / ledger.MaxTypes, 0f, 1f)
				: 0f;

			_capacityFill.Left.Set(2f, 0f);
			_capacityFill.Top.Set(2f, 0f);
			_capacityFill.Width.Set(Math.Max(0f, 256f * ratio), 0f);
			_capacityFill.Height.Set(6f, 0f);
			_capacityFill.BackgroundColor = ratio > 0.9f
				? new Color(255, 140, 120)
				: ratio > 0.7f ? new Color(255, 215, 120) : new Color(110, 200, 255);
			_capacityBar.Recalculate();

			// 内容面板自己会重建列表
			_contents.Refresh();
		}

        protected override void DrawHoverTooltip(Microsoft.Xna.Framework.Graphics.SpriteBatch batch)
        {
            if (_drive == null || _capacity == null || (!_capacity.ContainsPoint(Main.MouseScreen)
                && !_capacityBar.ContainsPoint(Main.MouseScreen))) return;
            var ledger = _drive.Ledger;
            string detail = ModText.Get("LabelItems", "件物品") + ": " + UIFormat.Exact(ledger.StoredTotalAmount);
            detail += "\n类别：" + UIFormat.Exact(ledger.StoredTypeCount) + " / " + UIFormat.Exact(ledger.MaxTypes)
                + "\n每种物品最多：" + UIFormat.Exact(ledger.MaxAmountPerType) + "\n不按物品总数消耗容量";
            UITooltip.Draw(batch, ledger.MaxAmountPerType == long.MaxValue ? ModText.Get("DigitalSingularityCapacity", "数字奇点")
                : ModText.Get("LabelCapacity", "容量"), detail);
        }
	}
}
