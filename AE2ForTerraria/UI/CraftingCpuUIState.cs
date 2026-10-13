using System.Collections.Generic;
using System;
using AE2ForTerraria.Content.Tiles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using AE2ForTerraria.Systems;

namespace AE2ForTerraria.UI
{
	/// <summary>独立合成 CPU 的队列界面:看排队情况、看进度、取消任务。</summary>
	public sealed class CraftingCpuUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = new WindowGeometry(660f, 480f, 520f, 340f);

		private const int IconSize = 34;
		private const float RowHeight = 40f;
		private const float ListTop = 100f;

		private readonly List<UIElement> _spawned = new List<UIElement>();
		private MECraftingCpuEntity _cpu;

		private UITextPanel<string> _viewButton;
        private bool _showKeep;
        private int _lastContentsHash;
        private UITextPanel<string> _dropButton;
		private UITextPanel<string> _clearButton;
		private UITextPanel<string> _pauseButton;
		private UITextPanel<string> _outputButton;
		private UITextPanel<string> _prevButton;
		private UITextPanel<string> _nextButton;
		private UIText _pageLabel;
		private UIText _hint;
		private UIText _status;
		private UIText _empty;
		private int _page;
		private float _lastProgress;
		private MECraftingCpuEntity.Job _lastActiveJob;

		/// <summary>「清空队列」的二次确认计时。破坏性操作不能一点就生效。</summary>
		private double _clearConfirmUntil;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("TitleCpu", "ME 合成 CPU"));

			_hint = new UIText(ModText.Get("HintCpu",
				"队列为空时空手右键本方块打开此界面;手持物品右键可加入队列(按住下键 +10)"), 0.72f, false);
			Window.Append(_hint);

			_empty = new UIText(ModText.Get("LabelEmptyQueue", "队列为空"), 0.85f, false);
			Window.Append(_empty);

			_status = new UIText(string.Empty, 0.78f, false);
			Window.Append(_status);

			_dropButton = new UIButton(ModText.Get("RemoveHead", "移除队首"), 0.82f, false);
			_dropButton.OnLeftClick += (evt, element) =>
			{
				if (_cpu != null && _cpu.Jobs.Count > 0)
				{
					var head=_cpu.Jobs[0];
                    if (Main.netMode==NetmodeID.MultiplayerClient) MENet.CpuRemove(_cpu.Position,head.Id);
                    else _cpu.DropJob(head);
					Refresh();
				}
			};
			Window.Append(_dropButton);

			_clearButton = new UIButton(ModText.Get("ClearQueue", "清空队列"), 0.82f, false);
			_clearButton.OnLeftClick += (evt, element) =>
			{
				// 第一次点只是「上膛」,2 秒内再点一次才真的清空
				if (UIStatus.Now > _clearConfirmUntil)
				{
					_clearConfirmUntil = UIStatus.Now + 2.0;
					_clearButton.SetText(ModText.Get("ConfirmClearQueue", "再点一次确认清空队列"));
					return;
				}

				_clearConfirmUntil = 0;
				_clearButton.SetText(ModText.Get("ClearQueue", "清空队列"));
				_cpu?.ClearQueue();
				if (_cpu != null)
				{
					MENet.CpuClear(_cpu.Position);
				}

				UIStatus.Toast(ModText.Get("StatusQueueCleared", "队列已清空"));
				Refresh();
			};
			Window.Append(_clearButton);

			_pauseButton = new UIButton(ModText.Get("BtnPause", "暂停"), 0.82f, false);
			_pauseButton.OnLeftClick += (evt, element) =>
			{
				if (_cpu == null)
				{
					return;
				}

				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.CpuPause(_cpu.Position);
				}
				else
				{
					_cpu.Paused = !_cpu.Paused;
				}

				UIStatus.Toast(_cpu.Paused
					? ModText.Get("MsgCpuPaused", "合成队列已暂停")
					: ModText.Get("MsgCpuResumed", "合成队列已继续"));
				Refresh();
			};
			Window.Append(_pauseButton);

			// 产出目的地:进网络 / 交给下单的玩家
			_outputButton = new UIButton(OutputLabel(), 0.82f, false);
			_outputButton.OnLeftClick += (evt, element) =>
			{
				if (_cpu == null)
				{
					return;
				}

				if (Main.netMode == NetmodeID.MultiplayerClient)
				{
					MENet.CpuSetOutput(_cpu.Position, !_cpu.OutputToInventory);
				}
				else
				{
					_cpu.OutputToInventory = !_cpu.OutputToInventory;
				}

				_outputButton.SetText(OutputLabel());
			};
			Window.Append(_outputButton);

			_prevButton = new UIButton(ModText.Get("PrevPage", "◀ 上一页"), 0.75f, false);
			_prevButton.OnLeftClick += (evt, element) =>
			{
				_page = Math.Max(0, _page - 1);
				Refresh();
			};
			Window.Append(_prevButton);

			_nextButton = new UIButton(ModText.Get("NextPage", "下一页 ▶"), 0.75f, false);
			_nextButton.OnLeftClick += (evt, element) =>
			{
				_page++;
				Refresh();
			};
			Window.Append(_nextButton);

			_pageLabel = new UIText(string.Empty, 0.72f, false);
			Window.Append(_pageLabel);

            _viewButton=new UIButton("保持库存",0.75f);
            _viewButton.OnLeftClick+=(_,_)=> { _showKeep=!_showKeep; _page=0; Refresh(); };
            Window.Append(_viewButton);

			ApplyLayout();
		}

		public void SetCpu(MECraftingCpuEntity cpu)
		{
			_cpu = cpu;
			Refresh();
		}

		protected override void UpdateWindow(GameTime gameTime)
		{
			// 正在合成的那一条会随装配室推进 —— 进度条得动起来。
			// 每帧重建整个列表太贵,所以只在「换任务」或「进度走过 2%」时重建。
			var signature=new HashCode();
            if (_cpu!=null) { foreach(var job in _cpu.Jobs) { signature.Add(job); signature.Add(job.Remaining); }
                foreach(var keep in _cpu.Keep) { signature.Add(keep.Key); signature.Add(keep.Target); } }
            int hash=signature.ToHashCode();
            if(hash!=_lastContentsHash) { _lastContentsHash=hash; Refresh(); }
            float progress = _assemblerProgress();
			MECraftingCpuEntity.Job activeJob = _cpu?.ActiveJob;

			if (!ReferenceEquals(activeJob, _lastActiveJob) || Math.Abs(progress - _lastProgress) > 0.02f)
			{
				_lastActiveJob = activeJob;
				_lastProgress = progress;
				Refresh();
			}

			// 队列会被装配室消费,状态栏需要跟着刷新
			RefreshStatus();
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			float w = Geo.W;
			float h = Geo.H;

			_hint.Left.Set(Pad + 6f, 0f);
			_hint.Top.Set(38f, 0f);
            _hint.Width.Set(w-44f,0f); _hint.DynamicallyScaleDownToWidth=true;

			_empty.Left.Set(Pad + 6f, 0f);
			_empty.Top.Set(70f, 0f);

			_dropButton.Left.Set(w - 352f, 0f);
			_dropButton.Top.Set(36f, 0f);
			_dropButton.Width.Set(108f, 0f);
			_dropButton.Height.Set(28f, 0f);

			_clearButton.Left.Set(w - 236f, 0f);
			_clearButton.Top.Set(36f, 0f);
			_clearButton.Width.Set(108f, 0f);
			_clearButton.Height.Set(28f, 0f);

			_pauseButton.Left.Set(w - 120f, 0f);
			_pauseButton.Top.Set(36f, 0f);
			_pauseButton.Width.Set(108f, 0f);
			_pauseButton.Height.Set(28f, 0f);

			_outputButton.Left.Set(w - 470f, 0f);
			_outputButton.Top.Set(36f, 0f);
			_outputButton.Width.Set(110f, 0f);
			_outputButton.Height.Set(28f, 0f);

            UIElement[] actions={_viewButton,_outputButton,_dropButton,_clearButton,_pauseButton};
            float actionWidth=(w-44f-24f)/5f;
            for(int i=0;i<actions.Length;i++) { actions[i].Left.Set(22f+i*(actionWidth+6f),0f); actions[i].Top.Set(62f,0f); actions[i].Width.Set(actionWidth,0f); actions[i].Height.Set(28f,0f); }
            _empty.Top.Set(ListTop+8f,0f);
            _status.Width.Set(w-44f,0f); _status.DynamicallyScaleDownToWidth=true;

			_prevButton.Left.Set(Pad + 6f, 0f);
			_prevButton.Top.Set(h - 68f, 0f);
			_prevButton.Width.Set(96f, 0f);
			_prevButton.Height.Set(26f, 0f);

			_nextButton.Left.Set(Pad + 110f, 0f);
			_nextButton.Top.Set(h - 68f, 0f);
			_nextButton.Width.Set(96f, 0f);
			_nextButton.Height.Set(26f, 0f);

			_pageLabel.Left.Set(Pad + 216f, 0f);
			_pageLabel.Top.Set(h - 63f, 0f);

			_status.Left.Set(Pad + 6f, 0f);
			_status.Top.Set(h - 34f, 0f);

			Window.Recalculate();
			Refresh();
		}

		private void Refresh()
		{
			foreach (UIElement element in _spawned)
			{
				element.Remove();
			}

			_spawned.Clear();

			if (_cpu == null)
			{
				return;
			}

			_empty.SetText(_cpu.Jobs.Count == 0 ? ModText.Get("LabelEmptyQueue", "队列为空") : string.Empty);

			// 一行能显示多少项按窗口高度算(原来是硬编码 6)
			int maxRows = Math.Max(1, (int)((Geo.H - ListTop - 90f) / RowHeight));

			int pages = Math.Max(1, (int)Math.Ceiling((_showKeep ? _cpu.Keep.Count : _cpu.Jobs.Count) / (double)maxRows));
			if (_page >= pages)
			{
				_page = pages - 1;
			}

			if (_page < 0)
			{
				_page = 0;
			}

			_pageLabel.SetText(ModText.Get("Paging", "第") + " " + (_page + 1) + "/" + pages
				+ (ModText.Get("Paging", "第") == "第" ? " 页" : string.Empty));

			_viewButton?.SetText(_showKeep ? "返回任务" : "保持库存");
            if (_showKeep) { _empty.SetText(_cpu.Keep.Count==0 ? "未设置保持库存，请在终端选择物品后设置" : string.Empty);
                BuildKeepRows(ListTop); RefreshStatus(); Window.Recalculate(); return; }
            int start = _page * maxRows;
			int shown = 0;
			for (int index = start; index < _cpu.Jobs.Count; index++)
			{
				MECraftingCpuEntity.Job job = _cpu.Jobs[index];

				if (shown >= maxRows)
				{
					break;
				}

				float top = ListTop + shown * RowHeight;
				bool active = ReferenceEquals(_cpu.ActiveJob, job);

				UIItemIcon icon = UIItemIcon.Create(job.Key.Type, IconSize, null);
				Window.Append(icon);
				_spawned.Add(icon);
				icon.Left.Set(Pad + 8f, 0f);
				icon.Top.Set(top, 0f);

				var label = new UIText(
					(active ? "> " : "  ")
					+ Lang.GetItemNameValue(job.Key.Type)
					+ "   " + job.Done + " / " + job.Total
					+ "   " + ModText.Get("StatusRemaining", "剩") + " " + job.Remaining
					+ (active ? "   " + (int)(_assemblerProgress() * 100f) + "%" : string.Empty),
					0.8f,
					false);
				Window.Append(label);
				_spawned.Add(label);
				label.Left.Set(Pad + 52f, 0f);
				label.Top.Set(top + 6f, 0f);
                label.Width.Set(Geo.W-230f,0f); label.DynamicallyScaleDownToWidth=true;

				if (active)
				{
					// 正在合成的那一条下面画一条进度条
					var bar = new UIFlatPanel();
					bar.BackgroundColor = new Color(90, 175, 240);
					Window.Append(bar);
					_spawned.Add(bar);
					bar.Left.Set(Pad + 52f, 0f);
					bar.Top.Set(top + 28f, 0f);
					bar.Width.Set(Math.Max(1f, (Geo.W - 150f) * _assemblerProgress()), 0f);
					bar.Height.Set(4f, 0f);
				}

				// 每一项都能单独取消(之前只有「移除队首」)
				MECraftingCpuEntity.Job target = job;
				int jobIndex = index;

				// 上移 / 置顶:排错顺序时不用清空重排
				if (index > start)
				{
					var up = new UIButton(ModText.Get("BtnMoveUp", "上移"), 0.72f, false);
					up.OnLeftClick += (evt, element) => MoveJob(target, jobIndex, -1);
					Window.Append(up);
					_spawned.Add(up);
					up.Left.Set(Geo.W - 146f, 0f);
					up.Top.Set(top + 2f, 0f);
					up.Width.Set(42f, 0f);
					up.Height.Set(28f, 0f);
				}

				if (index > 0)
				{
					var toTop = new UIButton(ModText.Get("BtnMoveTop", "置顶"), 0.72f, false);
					toTop.OnLeftClick += (evt, element) => MoveJob(target, jobIndex, -999);
					Window.Append(toTop);
					_spawned.Add(toTop);
					toTop.Left.Set(Geo.W - 100f, 0f);
					toTop.Top.Set(top + 2f, 0f);
					toTop.Width.Set(42f, 0f);
					toTop.Height.Set(28f, 0f);
				}

				var cancel = new UIIconButton(UIIconButton.IconKind.Cross);
				cancel.OnLeftClick += (evt, element) =>
				{
					int removeIndex = _cpu?.Jobs.IndexOf(target) ?? -1;
					if (Main.netMode!=NetmodeID.MultiplayerClient) _cpu?.DropJob(target);
					if (_cpu != null && removeIndex >= 0)
					{
						MENet.CpuRemove(_cpu.Position, target.Id);
					}

					Refresh();
				};
				Window.Append(cancel);
				_spawned.Add(cancel);
				cancel.Left.Set(Geo.W - 46f, 0f);
				cancel.Top.Set(top + 2f, 0f);
				cancel.Width.Set(28f, 0f);
				cancel.Height.Set(28f, 0f);

				shown++;
			}

			if (_cpu.Jobs.Count > start + shown)
			{
				var more = new UIText(ModText.Format("LblMoreInQueue", "…后面还有 {0} 项(用翻页按钮)",
					_cpu.Jobs.Count - start - shown), 0.75f, false);
				Window.Append(more);
				_spawned.Add(more);
				more.Left.Set(Pad + 8f, 0f);
				more.Top.Set(ListTop + shown * RowHeight, 0f);
			}

			Window.Recalculate();

			RefreshStatus();
		}

		/// <summary>「保持库存」清单:显示目标数量,右侧 × 可以取消。</summary>
		/// <summary>把队列里的一项上移 / 下移 / 置顶(多人走服务器)。</summary>
		private string OutputLabel()
		{
			return _cpu != null && _cpu.OutputToInventory
				? ModText.Get("BtnOutputInventory", "产出:给玩家")
				: ModText.Get("BtnOutputNetwork", "产出:进网络");
		}

		private void MoveJob(MECraftingCpuEntity.Job job, int index, int delta)
		{
			if (_cpu == null || job == null)
			{
				return;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				MENet.CpuMove(_cpu.Position, job.Id, delta);
			}
			else
			{
				_cpu.MoveJob(job, delta);
			}

			Refresh();
		}

		private void BuildKeepRows(float startTop)
		{
			if (_cpu == null || _cpu.Keep.Count == 0)
			{
				return;
			}

			var header = new UIText(ModText.Get("LblKeepStock", "保持库存(自动补货)") + ":", 0.75f, false);
			Window.Append(header);
			_spawned.Add(header);
			header.Left.Set(Pad + 8f, 0f);
			header.Top.Set(startTop, 0f);

			float top = startTop; header.SetText(string.Empty);
            int maxRows=Math.Max(1,(int)((Geo.H-ListTop-90f)/RowHeight)); int keepIndex=0;

			foreach (MECraftingCpuEntity.KeepEntry entry in _cpu.Keep)
			{
				if (keepIndex++ < _page*maxRows) continue;
                if (top + RowHeight > Geo.H - 80f)
				{
					break;
				}

				UIItemIcon icon = UIItemIcon.Create(entry.Key.Type, IconSize, null);
				Window.Append(icon);
				_spawned.Add(icon);
				icon.Left.Set(Pad + 8f, 0f);
				icon.Top.Set(top, 0f);

				var label = new UIText(
					UIItemNames.Display(entry.Key) + "   ≥ " + entry.Target,
					0.78f, false);
				Window.Append(label);
				_spawned.Add(label);
				label.Left.Set(Pad + 52f, 0f);
				label.Top.Set(top + 6f, 0f);
                label.Width.Set(Geo.W-230f,0f); label.DynamicallyScaleDownToWidth=true;

				MECraftingCpuEntity.KeepEntry target = entry;
				var cancel = new UIIconButton(UIIconButton.IconKind.Cross);
				cancel.OnLeftClick += (evt, element) =>
				{
					if (Main.netMode == NetmodeID.MultiplayerClient)
					{
						MENet.CpuKeep(_cpu.Position, target.Key, 0);
					}
					else
					{
						_cpu.SetKeep(target.Key, 0);
					}

					Refresh();
				};
				Window.Append(cancel);
				_spawned.Add(cancel);
				cancel.Left.Set(Geo.W - 46f, 0f);
				cancel.Top.Set(top + 2f, 0f);
				cancel.Width.Set(28f, 0f);
				cancel.Height.Set(28f, 0f);

				top += RowHeight;
			}
		}

		/// <summary>
		/// 正在合成的那一件的进度。装配室跑在同一台机器上,直接问网络里的装配室;
		/// 找不到就按 0 显示(单人世界里不会有这个问题)。
		/// </summary>
		private float _assemblerProgress()
		{
			if (_cpu == null)
			{
				return 0f;
			}

			MEMolecularAssemblerEntity assembler = MENetwork.FindAssembler(_cpu.Position.X, _cpu.Position.Y);
			return assembler?.ProgressFraction ?? 0f;
		}

		private void RefreshStatus()
		{
			if (_status == null)
			{
				return;
			}

			_status.SetText(_cpu == null
				? ModText.Get("LabelNotBound", "未绑定")
				: ModText.Get("LabelQueue", "队列") + " " + _cpu.Jobs.Count + " "
					+ ModText.Get("StatusItemsSuffix", "项")
					+ "   |   " + (_cpu.Paused
						? ModText.Get("StatusPaused", "队列已暂停")
						: ModText.Get("StatusRunning", "运行中"))
					+ "   |   " + _cpu.LastStatus);

			_pauseButton?.SetText(_cpu?.Paused == true
				? ModText.Get("BtnResume", "继续")
				: ModText.Get("BtnPause", "暂停"));
		}
	}
}
