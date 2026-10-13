using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using AE2ForTerraria.Systems;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 分子装配室界面。工作站槽位模仿魔法存储:一排放满就自动长出下一排。
	/// 窗口可拖动、可缩放。
	/// </summary>
	public sealed class AssemblerUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = new WindowGeometry(640f, 600f, 560f, 500f);

		private readonly List<UIItemSlotElement> _slots = new List<UIItemSlotElement>();
		private int _visibleSlots;

		private UIText _hint;
        private UIText _status;
		private UIText _target;
		private UIText _rows;
		private UIText _progressText;
		private UIPanel _progressBack;
		private UIPanel _progressFill;
		private UITextPanel<string> _clearButton;
		private MEMolecularAssemblerEntity _assembler;
		private double _clearConfirmUntil;
        private UIPlayerInventoryPanel _inventory;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("TitleAssembler", "ME 分子装配室"));

			_hint = new UIText("放入工作站；槽位随数量自动增加", 0.72f, false);
			_hint.Left.Set(Pad + 8f, 0f);
			_hint.Top.Set(40f, 0f);
			Window.Append(_hint);

			_target = new UIText(string.Empty, 0.85f, false);
			Window.Append(_target);

			_rows = new UIText(string.Empty, 0.78f, false);
			Window.Append(_rows);

			// 逐件合成的进度条
			_progressBack = new UIFlatPanel();
			_progressBack.BackgroundColor = new Color(18, 22, 38);
			Window.Append(_progressBack);

			_progressFill = new UIFlatPanel();
			_progressFill.BackgroundColor = new Color(90, 175, 240);
			_progressBack.Append(_progressFill);

			_progressText = new UIText(string.Empty, 0.72f, false);
			Window.Append(_progressText);

			_status = new UIText(string.Empty, 0.78f, false);
			Window.Append(_status);

			_clearButton = new UIButton(ModText.Get("ClearTarget", "清除目标"), 0.85f, false);
			_clearButton.OnLeftClick += (evt, element) =>
			{
				if (_assembler != null)
				{
					if (UIStatus.Now > _clearConfirmUntil)
					{
						_clearConfirmUntil = UIStatus.Now + 2.0;
						_clearButton.SetText(ModText.Get("ConfirmClearTarget", "再点一次确认清除目标"));
						return;
					}

					_clearConfirmUntil = 0;
					_clearButton.SetText(ModText.Get("ClearTarget", "清除目标"));
					if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) MENet.SetTarget(_assembler.Position, ItemKey.None);
                    else _assembler.Target = ItemKey.None;
					_assembler.LastStatus = ModText.Get("StatusTargetCleared", "已清除目标");
					UIStatus.Toast(ModText.Get("StatusTargetCleared", "已清除目标"));
					Refresh();
				}
			};
			Window.Append(_clearButton);
            _inventory = new UIPlayerInventoryPanel();
            Window.Append(_inventory);

			ApplyLayout();
		}

		public void SetAssembler(MEMolecularAssemblerEntity assembler)
		{
			_assembler = assembler;
			_visibleSlots = -1; // 强制重建槽位
			EnsureSlots();
			Refresh();
		}

		protected override void UpdateWindow(GameTime gameTime)
		{
			EnsureSlots();
			Refresh();
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			const float slotStartX = Pad + 10f;
			const float slotStartY = 96f;
			float inventoryTop = Geo.H - 224f;
            int stationRows = Math.Max(1, (_slots.Count + MEMolecularAssemblerEntity.SlotsPerRow - 1)
                / MEMolecularAssemblerEntity.SlotsPerRow);
            float step = Math.Min(51f, Math.Max(20f, (inventoryTop - slotStartY - 30f) / stationRows));

			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].Left.Set(slotStartX + (i % MEMolecularAssemblerEntity.SlotsPerRow) * step, 0f);
				_slots[i].Top.Set(slotStartY + (i / MEMolecularAssemblerEntity.SlotsPerRow) * step, 0f);
                _slots[i].Width.Set(step - 4f, 0f);
                _slots[i].Height.Set(step - 4f, 0f);
			}

			float textLeft = slotStartX + MEMolecularAssemblerEntity.SlotsPerRow * 51f + 16f;
			_target.Left.Set(textLeft, 0f);
			_target.Top.Set(100f, 0f);
			_rows.Left.Set(textLeft, 0f);
			_rows.Top.Set(130f, 0f);

			float barWidth = Math.Max(100f,Geo.W-textLeft-24f);
            foreach (var text in new[] {_target,_rows,_progressText}) { text.Width.Set(barWidth,0f); text.DynamicallyScaleDownToWidth=true; }
            _status.Width.Set(Geo.W-48f,0f); _status.DynamicallyScaleDownToWidth=true;
            _hint.Width.Set(Geo.W-200f,0f); _hint.DynamicallyScaleDownToWidth=true;
			_progressBack.Left.Set(textLeft, 0f);
			_progressBack.Top.Set(160f, 0f);
			_progressBack.Width.Set(barWidth, 0f);
			_progressBack.Height.Set(14f, 0f);

			float fraction = _assembler?.ProgressFraction ?? 0f;
			_progressFill.Left.Set(2f, 0f);
			_progressFill.Top.Set(2f, 0f);
			_progressFill.Width.Set(Math.Max(0f, (barWidth - 4f) * fraction), 0f);
			_progressFill.Height.Set(10f, 0f);

			_progressText.Left.Set(textLeft, 0f);
			_progressText.Top.Set(176f, 0f);

			_clearButton.Left.Set(Geo.W - 160f, 0f);
			_clearButton.Top.Set(40f, 0f);
			_clearButton.Width.Set(140f, 0f);
			_clearButton.Height.Set(30f, 0f);

			_status.Left.Set(Pad + 8f, 0f);
			_status.Top.Set(inventoryTop - 24f, 0f);
            _inventory.SetBounds(Pad + 8f, inventoryTop, Geo.W - Pad * 2f - 16f, 208f);

			Window.Recalculate();
		}

		/// <summary>按当前需要显示的行数重建槽位元素,并调整窗口高度。</summary>
		private void EnsureSlots()
		{
			if (_assembler == null)
			{
				return;
			}

			int wanted = _assembler.VisibleStationSlots();
			if (wanted == _visibleSlots)
			{
				return;
			}

			foreach (UIItemSlotElement slot in _slots)
			{
				slot.Remove();
			}

			_slots.Clear();

			for (int i = 0; i < wanted; i++)
			{
				int index = i;
				UIItemSlotElement slot = UIItemSlotElement.Create(
					() => _assembler != null ? _assembler.Stations[index] : new Item(),
					item =>
					{
						if (_assembler != null)
						{
							_assembler.Stations[index] = item;
						}
					},
					item=>item==null || item.IsAir || item.createTile>=0,
					null,
					OnSlotChanged);

                slot.InteractionHandler = right =>
                {
                    if (_assembler == null) return true;
                    if (MENet.SlotAction(_assembler.Position, index, true, right)) return true;
                    Item cursor = Main.mouseItem;
                    if (ContainerSlotOperations.TryChange(_assembler.Stations, index, ref cursor, right, null, out _))
                    { Main.mouseItem = cursor; OnSlotChanged(); }
                    return true;
                };
				Window.Append(slot);
				_slots.Add(slot);
			}

			_visibleSlots = wanted;

			ApplyLayout();
		}

		/// <summary>工作站槽位改动:本地刷新 + 多人下把整份槽位发给服务器。</summary>
		private void OnSlotChanged()
		{
			Refresh();

			if (_assembler != null)
			{
				MENet.SendStations(_assembler.Position, _assembler.Stations);
			}
		}

		private void Refresh()
		{
		
			if (_assembler == null || _status == null)
			{
				return;
			}

			UITextCache.Set(_target, _assembler.Target.IsAir
				? ModText.Get("LabelTarget", "当前目标") + ":" + ModText.Get("LabelTargetNone", "无")
				: ModText.Get("LabelTarget", "当前目标") + ":" + Lang.GetItemNameValue(_assembler.Target.Type));

			int rows = _visibleSlots / MEMolecularAssemblerEntity.SlotsPerRow;
			UITextCache.Set(_rows, ModText.Format("LabelSlots", "{0} 格({1} 排,最多 {2} 排)",
				_visibleSlots, rows, MEMolecularAssemblerEntity.MaxRows));

			bool crafting = !_assembler.ActiveKey.IsAir;
			string active = crafting
				? Lang.GetItemNameValue(_assembler.ActiveKey.Type)
				: ModText.Get("LabelIdle", "空闲");

			// 成就:亲眼看到装配室在自动干活(多人下客户端只能这样观测)
			if (crafting)
			{
				Content.Achievements.AchievementTriggers.Trigger<Content.Achievements.AutomateFirstItemAchievement>();
			}

			UITextCache.Set(_progressText, ModText.Format("LabelCraftingLine", "正在合成: {0}   进度 {1}%   并行数 {2}   每件 {3} tick",
				active,
				(int)(_assembler.ProgressFraction * 100f),
				Math.Max(1, _assembler.Parallel),
				MEMolecularAssemblerEntity.TicksPerItem));

			UITextCache.Set(_status, ModText.Get("LabelStatus", "状态") + ": " + _assembler.LastStatus);

			// 进度条的长度每次都按最新进度重算
			float barWidth = _progressBack.Width.Pixels;
			_progressFill.Width.Set(Math.Max(0f, (barWidth - 4f) * _assembler.ProgressFraction), 0f);
			_progressBack.Recalculate();
		}
	}
}

