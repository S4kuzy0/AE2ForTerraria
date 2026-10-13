using System;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// ME 存储单元界面。
	/// 以前它右键只有「拿升级件就升级 / 否则把手上的存进去」两种情况,
	/// 完全看不到里面有什么 —— 现在和驱动器共用同一块内容面板,体验一致。
	/// </summary>
	public sealed class StorageUnitUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = new WindowGeometry(640f, 460f, 520f, 360f);

		private MEStorageUnitEntity _unit;

		private UIText _status;
		private UIText _hint;
		private UITextPanel<string> _upgradeButton;
		private UITextPanel<string> _depositButton;
		private UIStorageContents _contents;
		private UIPartitionEditor _partition;
		private UITextPanel<string> _tabButton;
		private bool _showPartition;
		private float _panelX;
		private UIPanel _capacityBar;
		private UIPanel _capacityFill;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("TitleStorageUnit", "ME 存储单元"));

			_status = new UIText(string.Empty, 0.8f, false);
			Window.Append(_status);

			_hint = new UIText(ModText.Get("HintStorageUnit",
				"手持升级件右键方块可以升级;这里的物品存在方块本身"), 0.72f, false);
			Window.Append(_hint);

			_upgradeButton = new UIButton(ModText.Get("BtnUpgradeUnit", "用升级件升级"), 0.8f, false);
			_upgradeButton.OnLeftClick += (evt, element) => Upgrade();
			Window.Append(_upgradeButton);

			_depositButton = new UIButton(ModText.Get("DepositAll", "存入全部"), 0.8f, false);
			_depositButton.OnLeftClick += (evt, element) => Deposit();
			Window.Append(_depositButton);

			_capacityBar = new UIFlatPanel();
			_capacityBar.BackgroundColor = new Color(18, 22, 38);
			Window.Append(_capacityBar);

			_capacityFill = new UIFlatPanel();
			_capacityFill.BackgroundColor = new Color(110, 200, 255);
			_capacityBar.Append(_capacityFill);

			_contents = new UIStorageContents();
			_contents.OnChanged = Refresh;
			Window.Append(_contents);

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

			ApplyLayout();
		}

		public void SetUnit(MEStorageUnitEntity unit)
		{
			_unit = unit;
			_contents.Bind(unit, unit.Position);
			_partition.Bind(unit, unit.Position);
			Refresh();
		}

		protected override void UpdateWindow(GameTime gameTime)
		{
			// 升级件被拿走 / 换等级之后状态行要跟着变
			Refresh();
		}

		private void Upgrade()
		{
			if (_unit == null)
			{
				return;
			}

			if (_unit.Level >= MEStorageUnitEntity.MaxLevel)
			{
				UIStatus.Toast(ModText.Get("MsgUnitMax", "这个存储单元已经是最高级了"),
					new Color(255, 200, 150));
				return;
			}

			Item held = Main.LocalPlayer?.HeldItem;
			if (held == null || held.type != ModContent.ItemType<MEStorageUpgrade>())
			{
				UIStatus.Toast(ModText.Get("MsgNeedUpgradeItem", "需要手持 ME 存储升级件"),
					new Color(255, 200, 150));
				return;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.UpgradeUnit(_unit.Position);
				UIStatus.Toast(ModText.Get("MsgUpgradeSent", "已把升级请求发给服务器"));
				return;
			}

			if (StorageOperations.TryUpgradeStorageUnit(_unit, Main.LocalPlayer))
			{
				UIStatus.Toast(ModText.Format("MsgUnitUpgradedDetail",
					"存储单元升级到 Lv.{0}(容量 {1} 字节)", _unit.Level, _unit.CurrentCapacity));
			}

			Refresh();
		}

		private void Deposit()
		{
			if (_unit == null)
			{
				return;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.DepositAll(_unit.Position, InventoryScope.IncludeAmmoAndCoinsByDefault);
				UIStatus.Toast(ModText.Get("MsgDepositSent", "已把存入请求发给服务器"));
				return;
			}

            long moved=StorageOperations.DepositInventory(MENetwork.CollectStorage(_unit.Position.X,_unit.Position.Y),
                Main.LocalPlayer,InventoryScope.IncludeAmmoAndCoinsByDefault);

			UIStatus.Toast(ModText.Format("MsgUnitContents",
				"已存入 {0} 件物品(Lv.{1},{2} / {3} 字节,{4} 类)",
				moved, _unit.Level, _unit.Ledger.UsedBytes, _unit.CurrentCapacity,
				_unit.Ledger.StoredTypeCount));

			_contents.Refresh();
			Refresh();
		}

		private void Refresh()
		{
			if (_unit == null || _status == null)
			{
				return;
			}

			UITextCache.Set(_status, ModText.Format("MsgUnitLevel", "等级 Lv.{0} / Lv.{1}",
					_unit.Level, MEStorageUnitEntity.MaxLevel)
				+ "    |    " + ModText.Get("LabelCapacity", "容量") + ": "
				+ UIFormat.Count(_unit.Ledger.UsedBytes) + " / "
				+ UIFormat.Count(_unit.CurrentCapacity) + " " + ModText.Get("LabelBytes", "字节")
				+ "    |    " + _unit.Ledger.StoredTypeCount + " " + ModText.Get("LabelKinds", "类")
				+ "    |    " + _unit.Ledger.StoredTotalAmount + " " + ModText.Get("LabelItems", "件物品"));

			float ratio = _unit.CurrentCapacity > 0
				? Math.Clamp((float)_unit.Ledger.UsedBytes / _unit.CurrentCapacity, 0f, 1f)
				: 0f;

			_capacityFill.Left.Set(2f, 0f);
			_capacityFill.Top.Set(2f, 0f);
			_capacityFill.Width.Set(Math.Max(0f, 256f * ratio), 0f);
			_capacityFill.Height.Set(6f, 0f);
			_capacityFill.BackgroundColor = ratio > 0.9f
				? new Color(255, 140, 120)
				: new Color(110, 200, 255);
			_capacityBar.Recalculate();
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			_status.Left.Set(Pad + 8f, 0f);
			_status.Top.Set(38f, 0f);
            _status.Width.Set(Geo.W-48f,0f); _status.DynamicallyScaleDownToWidth=true;

			_hint.Left.Set(Pad + 8f, 0f);
			_hint.Top.Set(60f, 0f);
            _hint.Width.Set(Geo.W-48f,0f); _hint.DynamicallyScaleDownToWidth=true;

			_upgradeButton.Left.Set(Pad + 8f, 0f);
			_upgradeButton.Top.Set(84f, 0f);
			_upgradeButton.Width.Set(160f, 0f);
			_upgradeButton.Height.Set(28f, 0f);

			_depositButton.Left.Set(Pad + 176f, 0f);
			_depositButton.Top.Set(84f, 0f);
			_depositButton.Width.Set(140f, 0f);
			_depositButton.Height.Set(28f, 0f);

			_capacityBar.Left.Set(Pad + 8f, 0f);
			_capacityBar.Top.Set(114f, 0f);
			_capacityBar.Width.Set(260f, 0f);
			_capacityBar.Height.Set(10f, 0f);

			_panelX = Pad + 8f;
			_contents.Place(_panelX, 132f,
				Math.Max(160f, Geo.W - Pad * 2f - 16f), Math.Max(120f, Geo.H - 132f - 56f));

			_partition.Place(_panelX, 132f,
				Math.Max(160f, Geo.W - Pad * 2f - 16f), Math.Max(120f, Geo.H - 132f - 56f));

			_tabButton.Left.Set(_panelX, 0f);
			_tabButton.Top.Set(Geo.H - 32f, 0f);
			_tabButton.Width.Set(120f, 0f);
			_tabButton.Height.Set(26f, 0f);

			Window.Recalculate();
			ApplyPanelVisibility();
		}

		private void ApplyPanelVisibility()
		{
			if (_contents == null || _partition == null)
			{
				return;
			}

			_contents.IgnoresMouseInteraction = _showPartition;
			_partition.IgnoresMouseInteraction = !_showPartition;

			_contents.Left.Set(_showPartition ? -10000f : _panelX, 0f);
			_partition.Left.Set(_showPartition ? _panelX : -10000f, 0f);
            _contents.Recalculate(); _partition.Recalculate();
		}
	}
}
