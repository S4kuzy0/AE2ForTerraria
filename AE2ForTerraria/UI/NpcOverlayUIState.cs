using System;
using System.Collections.Generic;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 怪物详情覆盖窗口:属性、掉落物、刷新条件。
	/// 掉落物用游戏自带的掉落表(ItemDropDatabase)查,点掉落物可以跳到那个物品。
	/// </summary>
	public sealed class NpcOverlayUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = WindowGeometry.Compact();

		private const int IconSize = 38;
		private const int IconStep = 44;

		private readonly List<UIElement> _spawned = new List<UIElement>();
		private readonly List<string> _lines = new List<string>();

		private int _npcType;
		private int _dropPage;

		private UIText _title;
		private UIText _body;
		private UIText _dropLabel;
		private UIIconButton _prev;
		private UIIconButton _next;

		/// <summary>点了掉落物之后往哪里跳(由外部注入:打开那个物品的来源窗口)。</summary>
		public Action<int> OnItemClicked;

		public override void OnInitialize()
		{
			InitWindow(Geometry, string.Empty);

			_title = new UIText(string.Empty, 1f, false);
			Window.Append(_title);

			_body = new UIText(string.Empty, 0.8f, false);
			Window.Append(_body);

			_dropLabel = new UIText(string.Empty, 0.8f, false);
			Window.Append(_dropLabel);

			_prev = new UIIconButton(UIIconButton.IconKind.Left);
			_prev.OnLeftClick += (evt, element) => Turn(-1);
			Window.Append(_prev);

			_next = new UIIconButton(UIIconButton.IconKind.Right);
			_next.OnLeftClick += (evt, element) => Turn(1);
			Window.Append(_next);

			RequestLayout();
		}

		public void Show(int npcType)
		{
			GameDataIndex.EnsureBuilt();
			_npcType = npcType;
			_dropPage = 0;
			Refresh();
		}

		private void Turn(int delta)
		{
			_dropPage += delta;
			Refresh();
		}

		private void Refresh()
		{
			foreach (UIElement element in _spawned)
			{
				element.Remove();
			}

			_spawned.Clear();
			_lines.Clear();

			if (_npcType <= 0)
			{
				_title.SetText(ModText.Get("LblNoNpcSelected", "未选择怪物"));
				_body.SetText(string.Empty);
				_dropLabel.SetText(string.Empty);
				return;
			}

			string name = NpcIndex.GetName(_npcType);
			_title.SetText(name);

			AppendStats();

			_body.SetText(string.Join("\n", _lines));

			BuildDrops();
		}

		private void AppendStats()
		{
			try
			{
				var sample = new NPC();
				sample.SetDefaults(_npcType);
				_lines.Add(ModText.Get("NpcLife", "生命") + ": " + sample.lifeMax + "    " + ModText.Get("NpcDefense", "防御") + ": " + sample.defense
					+ "    " + ModText.Get("NpcDamage", "接触伤害") + ": " + sample.damage);
				_lines.Add(sample.boss ? ModText.Get("NpcTypeBoss", "类型:Boss") : ModText.Get("NpcTypeNormal", "类型:普通怪物"));
			}
			catch
			{
				_lines.Add(ModText.Get("LblStatsFailed", "(属性读取失败)"));
			}

			string spawn = StorageSystem.Acquisition != null
				? StorageSystem.Acquisition.GetSpawnCondition(_npcType)
				: null;

			_lines.Add(string.IsNullOrEmpty(spawn)
				? ModText.Get("NpcSpawn", "刷新条件") + ": " + ModText.Get("NpcSpawnNone", "暂无数据")
				: ModText.Get("NpcSpawn", "刷新条件") + ": " + spawn);

			// 图鉴是否解锁(击杀数 > 0 视为已解锁)
			try
			{
				// GetKillCount 要的是 NPC 实例(单个,很便宜)
				var probe = new NPC();
				probe.SetDefaults(_npcType);

				int kills = Main.BestiaryTracker.Kills.GetKillCount(probe);
				_lines.Add(kills > 0
					? ModText.Get("NpcBestiaryUnlocked", "图鉴已解锁(击杀") + " " + kills + ")"
					: ModText.Get("NpcBestiaryLocked", "图鉴未解锁"));
			}
			catch
			{
				// 图鉴追踪器读不出来就跳过这一行
			}
		}

		private void BuildDrops()
		{
			// 直接用加载时建好的索引,避免每次打开都重算掉落表
			IReadOnlyList<(int item, float rate)> drops = GameDataIndex.GetDrops(_npcType);

			if (drops.Count == 0)
			{
				_dropLabel.SetText(ModText.Get("NpcNoDrops", "掉落:无数据"));
				_prev.Left.Set(-200f, 1f);
				_next.Left.Set(-200f, 1f);
				return;
			}

			int perPage = Math.Max(1, (int)((Geo.H - 300f) / IconStep));
			int pages = (drops.Count + perPage - 1) / perPage;
			_dropPage = Math.Max(0, Math.Min(_dropPage, pages - 1));

			int start = _dropPage * perPage;
			int shown = Math.Min(perPage, drops.Count - start);

			for (int i = 0; i < shown; i++)
			{
				(int itemId, float rate) = drops[start + i];
				if (itemId <= 0)
				{
					continue;
				}

				UIItemIcon icon = UIItemIcon.Create(itemId, IconSize, OnItemClicked);
				Window.Append(icon);
				_spawned.Add(icon);
				icon.Left.Set(Pad + 16f, 0f);
				icon.Top.Set(210f + i * IconStep, 0f);
			}

			// 掉落文字列表(带掉落率)
			var lines = new List<string>();
			for (int i = 0; i < shown; i++)
			{
				(int itemId, float rate) = drops[start + i];
				if (itemId <= 0)
				{
					continue;
				}

				lines.Add(Lang.GetItemNameValue(itemId) + "   " + rate.ToString("0.#") + "%");
			}

			_dropLabel.SetText(string.Join("\n", lines));
			_dropLabel.Left.Set(Pad + 16f + IconStep + 6f, 0f);
			_dropLabel.Top.Set(216f, 0f);

			_prev.Left.Set(Geo.W - 108f, 0f);
			_next.Left.Set(Geo.W - 66f, 0f);
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			float w = Geo.W;
			float h = Geo.H;

			_title.Left.Set(Pad + 16f, 0f);
			_title.Top.Set(34f, 0f);

			_body.Left.Set(Pad + 16f, 0f);
			_body.Top.Set(70f, 0f);

			_dropLabel.Left.Set(Pad + 16f, 0f);
			_dropLabel.Top.Set(216f, 0f);

			_prev.Left.Set(w - 108f, 0f);
			_prev.Top.Set(h - 48f, 0f);
			_prev.Width.Set(34f, 0f);
			_prev.Height.Set(30f, 0f);

			_next.Left.Set(w - 66f, 0f);
			_next.Top.Set(h - 48f, 0f);
			_next.Width.Set(34f, 0f);
			_next.Height.Set(30f, 0f);

			Window.Recalculate();

			// 注意:这里不要调 Refresh() —— 缩放时每帧都会走到这里,
			// 而 Refresh 要重算整个掉落表,会卡。
			// 图标用的是绝对坐标,不需要因为窗口变大而重排;只有翻页按钮依赖窗口宽度,
			// 上面已经重定位过了。
		}
	}
}
