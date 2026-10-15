using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 界面系统:持有全部界面状态、处理按键开关,并把界面挂到绘制层。
	/// </summary>
	public sealed class UISystem : ModSystem
	{
		public static UserInterface Interface { get; private set; }

		/// <summary>
		/// 覆盖窗口(来源 / 用途、怪物详情)单独一层。
		/// 以前用的是单状态 SetState,一查来源就把终端顶掉了 ——
		/// 现在覆盖窗口画在主窗口**上面**,底下那个窗口还开着。
		/// </summary>
		public static UserInterface OverlayInterface { get; private set; }

		/// <summary>
		/// 第二层主窗口。
		///
		/// 以前所有主窗口共用 `Interface` 的单状态,于是打开驱动器必然顶掉终端。
		/// 现在把「方块窗口」(驱动器 / 存储单元 / 存储器总线 / 装配室 / CPU / 收集器 / 网络概览)
		/// 放到这一层:打开它们不会关掉底下的浏览器或终端。
		/// 绘制顺序:主层 → 第二层 → 覆盖层。
		/// </summary>
		public static UserInterface SecondaryInterface { get; private set; }

		public static NetworkOverviewUIState NetworkOverviewState { get; private set; }

		public static SearchUIState SearchState { get; private set; }
        public static BridgeUIState BridgeState { get; private set; }

		/// <summary>帮助页(子页面,不再是浏览器里的一块浮层)。</summary>
		public static HelpUIState HelpState { get; private set; }

		public static DriveUIState DriveState { get; private set; }

		public static TerminalUIState TerminalState { get; private set; }
        public static TerminalViewMode ActiveTerminalViewMode { get; private set; } = TerminalViewMode.Access;

		public static AssemblerUIState AssemblerState { get; private set; }

		public static RecipeOverlayUIState RecipeState { get; private set; }

		public static NpcOverlayUIState NpcState { get; private set; }

		public static CraftingCpuUIState CraftingCpuState { get; private set; }

		public static DropCollectorUIState DropCollectorState { get; private set; }

		public static StorageBusUIState StorageBusState { get; private set; }

		public static StorageUnitUIState StorageUnitState { get; private set; }

		/// <summary>当前绑定的 ME 网络(磁盘驱动器列表)。</summary>
		public static List<IMEStorage> ActiveNetwork { get; private set; }

		/// <summary>ActiveNetwork 的内容汇总。</summary>
		public static Dictionary<ItemKey, long> ActiveNetworkContents { get; private set; }

		/// <summary>当前网络里的分子装配室(终端下单会用它)。</summary>
		public static MEMolecularAssemblerEntity ActiveAssembler { get; private set; }

		/// <summary>终端的方块坐标(多人下客户端要把请求发给服务器)。</summary>
		public static Point16 ActiveTerminalPos { get; private set; }

		/// <summary>当前网络里的独立合成 CPU(终端下单会排进它的队列)。</summary>
		public static MECraftingCpuEntity ActiveCraftingCpu { get; private set; }
        public static MECraftingTerminalEntity ActiveCraftingTerminal { get; private set; }

		/// <summary>
		/// 服务器发来的「整网汇总」,按终端坐标缓存。
		/// 远程终端的情况下客户端本地没有驱动器 / 存储单元的方块实体,
		/// 靠这份数据才能看到库存。
		/// </summary>
		private static readonly Dictionary<Point16, Dictionary<ItemKey, long>> RemoteContents =
			new Dictionary<Point16, Dictionary<ItemKey, long>>();

		public static void SetRemoteContents(Point16 position, Dictionary<ItemKey, long> contents)
		{
			if (contents == null)
			{
				RemoteContents.Remove(position);
				return;
			}

			// 缓存别无限长:玩家跑过的每个终端都会留一份汇总,超过几十个就整份丢掉。
			// 丢掉的代价只是下次打开那个终端要等一次快照,无所谓。
			if (RemoteContents.Count > 24 && !RemoteContents.ContainsKey(position))
			{
				RemoteContents.Clear();
			}

			RemoteContents[position] = contents;

			// 如果这份汇总正好属于当前打开的终端,立刻让界面用上
			if (ActiveTerminalPos == position)
			{
				ActiveNetworkContents = contents;
			}
		}

		private static Dictionary<ItemKey, long> GetRemoteContents(Point16 position)
		{
			return RemoteContents.TryGetValue(position, out Dictionary<ItemKey, long> contents)
				? contents
				: null;
		}

        private static readonly TerminalReadState TerminalRead = new();
        private static readonly TerminalPolicyState TerminalPolicy = new();
        internal static bool TerminalPolicyReady => Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient
            || TerminalPolicy.Matches(ActiveTerminalPos, TerminalRead.Epoch);
        internal static bool TerminalItemLocked(int type) => Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient
            ? !TerminalPolicyReady || TerminalPolicy.IsLocked(type)
            : NetworkRegistry.IsLocked(MENetwork.CanonicalAnchor(MENetwork.Collect(ActiveTerminalPos.X, ActiveTerminalPos.Y)), type);
        internal static void ApplyTerminalPolicy(Point16 position, int epoch, Point16 anchor, List<int> types)
        {
            if (AcceptTerminalSnapshot(position, epoch)) TerminalPolicy.Apply(position, epoch, anchor, types);
        }
        private static readonly List<MECraftingCpuEntity> RemoteCraftingHosts = new();
        private static readonly List<MEMolecularAssemblerEntity> RemoteStationHosts = new();
        public static bool HasTerminalReadSession => TerminalRead.Position.X >= 0;
        public static bool TerminalReadable => Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient || TerminalRead.Readable;
        private static int LocalTerminalLevel()
        {
            if (ActiveTerminalPos.X < 0) return NetworkRegistry.LevelNone;
            var anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(ActiveTerminalPos.X,ActiveTerminalPos.Y));
            var player = Main.LocalPlayer;
            if (anchor.X < 0 || player == null) return NetworkRegistry.LevelNone;
            if (NetworkRegistry.GetOwner(anchor) == null && Vector2.DistanceSquared(player.Center,
                new Vector2(ActiveTerminalPos.X*16f+8f,ActiveTerminalPos.Y*16f+8f)) <= 512f*512f) return NetworkRegistry.LevelManage;
            return NetworkRegistry.GetLevel(anchor,player.name);
        }
        public static bool TerminalCanUse => Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient ? TerminalRead.Readable && TerminalRead.Level >= NetworkRegistry.LevelUse : LocalTerminalLevel() >= NetworkRegistry.LevelUse;
        public static bool TerminalCanManage => Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient ? TerminalRead.Readable && TerminalRead.Level >= NetworkRegistry.LevelManage : LocalTerminalLevel() >= NetworkRegistry.LevelManage;
        public static string TerminalAccessStatus => TerminalRead.Denied ? "无权读取此网络" : TerminalRead.Waiting ? "正在同步库存…" : "";
        internal static bool AcceptTerminalSnapshot(Point16 position, int epoch) => position == ActiveTerminalPos
            && position == TerminalRead.Position && epoch == TerminalRead.Epoch && !TerminalRead.Denied;
        private static void ResetTerminalRead()
        {
            TerminalPolicy.Reset();
            TerminalRead.Reset(); MENet.ClearClientInventoryPages(); RemoteCraftingHosts.Clear(); RemoteStationHosts.Clear();
            ActiveCraftingCpu=null; ActiveCraftingTerminal=null; ActiveAssembler=null;
            RemoteContents.Clear(); ActiveNetworkContents=null;
        }
        internal static void ApplyTerminalAccess(Point16 position, int epoch, int level, Dictionary<ItemKey,long> contents, Point16 cpu, Point16 assembler)
        {
            if (!TerminalRead.Apply(position,epoch,level)) return;
            if (!TerminalRead.Readable)
            {
                RemoteContents.Clear(); ActiveNetworkContents=null; ActiveNetwork=null;
                RemoteCraftingHosts.Clear(); RemoteStationHosts.Clear(); ActiveCraftingCpu=null; ActiveCraftingTerminal=null; ActiveAssembler=null;
                UIStatus.Toast("此网络已无读取权限，库存视图已关闭"); return;
            }
            SetRemoteContents(position,contents); SetRemoteMachinePositions(cpu,assembler);
        }
        public static void ApplyCraftingHosts(List<MECraftingCpuEntity> cpus,List<MEMolecularAssemblerEntity> assemblers)
        { RemoteCraftingHosts.Clear(); RemoteCraftingHosts.AddRange(cpus); RemoteStationHosts.Clear(); RemoteStationHosts.AddRange(assemblers); }
        public static IReadOnlyList<MECraftingCpuEntity> GetCraftingHosts()
        {
            if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return RemoteCraftingHosts;
            var result=new List<MECraftingCpuEntity>();
            foreach (var p in MENetwork.Collect(ActiveTerminalPos.X,ActiveTerminalPos.Y))
                if (TileEntity.ByPosition.TryGetValue(p,out var entity) && entity is MECraftingCpuEntity cpu) result.Add(cpu);
            return result;
        }
        public static IReadOnlyList<(Point16 Position,Item[] Stations)> GetStationHosts()
        {
            var result=new List<(Point16,Item[])>();
            if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
            {
                foreach (var cpu in RemoteCraftingHosts) if (cpu is MECraftingTerminalEntity terminal) result.Add((cpu.Position,terminal.Stations));
                foreach (var assembler in RemoteStationHosts) result.Add((assembler.Position,assembler.Stations));
                return result;
            }
            foreach (var p in MENetwork.Collect(ActiveTerminalPos.X,ActiveTerminalPos.Y))
            {
                if (!TileEntity.ByPosition.TryGetValue(p,out var entity)) continue;
                if (entity is MECraftingTerminalEntity terminal) result.Add((p,terminal.Stations));
                else if (entity is MEMolecularAssemblerEntity assembler) result.Add((p,assembler.Stations));
            }
            return result;
        }
        public static void OpenBridge(MEWirelessBridgeEntity bridge)
        {
            EnsureStates(); CloseOverlay();
            if (!ActivateState("无线桥",BridgeState)) return;
            BridgeState.SetBridge(bridge); SecondaryInterface?.SetState(BridgeState);
        }

        private static readonly Dictionary<Point16,MECraftingCpuEntity> RemoteCpus=new();
        private static readonly Dictionary<Point16,MEMolecularAssemblerEntity> RemoteAssemblers=new();
        private static readonly Dictionary<Point16,MECraftingTerminalEntity> RemoteTerminals=new();
        public static MECraftingTerminalEntity RemoteTerminal(Point16 pos)
        {
            if (!RemoteTerminals.TryGetValue(pos,out var terminal))
            { if (RemoteTerminals.Count>=24) RemoteTerminals.Clear(); RemoteTerminals[pos]=terminal=new MECraftingTerminalEntity { Position=pos }; }
            return terminal;
        }
        public static void SetRemoteTerminalPosition(Point16 position)
        {
            ActiveCraftingTerminal=position.X<0 ? null : TileEntity.ByPosition.TryGetValue(position,out var entity)
                && entity is MECraftingTerminalEntity local ? local : RemoteTerminal(position);
            if (ActiveCraftingTerminal!=null) ActiveCraftingCpu=ActiveCraftingTerminal;
        }
        public static MECraftingCpuEntity RemoteCpu(Point16 pos)
        {
            if (!RemoteCpus.TryGetValue(pos,out var cpu))
            { if (RemoteCpus.Count>=24) RemoteCpus.Clear(); RemoteCpus[pos]=cpu=new MECraftingCpuEntity { Position=pos }; }
            return cpu;
        }
        public static MEMolecularAssemblerEntity RemoteAssembler(Point16 pos)
        {
            if (!RemoteAssemblers.TryGetValue(pos,out var assembler))
            { if (RemoteAssemblers.Count>=24) RemoteAssemblers.Clear(); RemoteAssemblers[pos]=assembler=new MEMolecularAssemblerEntity { Position=pos }; }
            return assembler;
        }
        public static void SetRemoteMachinePositions(Point16 cpu,Point16 assembler)
        {
            ActiveCraftingTerminal=null;
            ActiveCraftingCpu=cpu.X<0 ? null : Terraria.DataStructures.TileEntity.ByPosition.TryGetValue(cpu,out var ce) && ce is MECraftingCpuEntity localCpu ? localCpu : RemoteTerminals.TryGetValue(cpu, out var knownTerminal) ? knownTerminal : RemoteCpu(cpu);
            ActiveCraftingTerminal=ActiveCraftingCpu as MECraftingTerminalEntity;
            ActiveAssembler=assembler.X<0 ? null : Terraria.DataStructures.TileEntity.ByPosition.TryGetValue(assembler,out var ae) && ae is MEMolecularAssemblerEntity localAssembler ? localAssembler : RemoteAssembler(assembler);
        }
		public static bool IsOpen =>
			(Interface != null && Interface.CurrentState != null)
			|| (SecondaryInterface != null && SecondaryInterface.CurrentState != null)
			|| (OverlayInterface != null && OverlayInterface.CurrentState != null);

		/// <summary>切换主窗口时顺手关掉覆盖层,免得「新窗口 + 旧来源窗口」叠在一起。</summary>
		private static void CloseOverlay()
		{
			OverlayInterface?.SetState(null);
		}

        public override void ClearWorld()
        {
            CloseAll(); ResetTerminalRead(); RemoteContents.Clear(); RemoteCpus.Clear(); RemoteAssemblers.Clear(); RemoteTerminals.Clear(); ActiveNetwork=null; ActiveNetworkContents=null;
            ActiveCraftingTerminal=null;
            ActiveAssembler=null; ActiveCraftingCpu=null; ActiveTerminalPos=new Point16(-1,-1);
            ActiveTerminalViewMode = TerminalViewMode.Access;
            AEUiSession.Reset(false);
            BlockGameInput(false);
        }
        private int _refreshTicks;
		public override void Load()
		{
			if (Main.dedServ)
			{
				return;
			}

			Interface = new UserInterface();
			OverlayInterface = new UserInterface();
			SecondaryInterface = new UserInterface();

			// 窗口位置 / 大小从存档目录读回来
			WindowLayout.Load();
		}

		/// <summary>
		/// 界面状态延迟创建。
		/// UIState.Activate() 会立刻测量文字(UIText 构造时就要量字体),
		/// 如果在 ModSystem.Load() 里建,字体可能还没加载好。
		/// 所以推迟到「游戏已经跑起来」的时机,由 PostUpdateInput / 绘制层触发。
		///
		/// 每个窗口**各自** try/catch:以前是一串裸 Activate(),
		/// 只要有一个窗口在初始化里抛异常(例如驱动器窗口摆子面板位置时空引用),
		/// 后面那些窗口就永远没 Activate —— 打开它们必然又在空控件上崩。
		/// 现在单个窗口坏掉只影响它自己,下一帧还会重试,日志也只打一次。
		/// </summary>
		private static void EnsureStates()
		{
			if (Main.dedServ || Interface == null || _statesReady)
			{
				return;
			}

			SearchState ??= new SearchUIState();
            BridgeState ??= new BridgeUIState();
			HelpState ??= new HelpUIState();
			DriveState ??= new DriveUIState();
			TerminalState ??= new TerminalUIState();
			AssemblerState ??= new AssemblerUIState();
			RecipeState ??= new RecipeOverlayUIState();
			NpcState ??= new NpcOverlayUIState { OnItemClicked = type => OpenRecipe(type, false) };
			CraftingCpuState ??= new CraftingCpuUIState();
			DropCollectorState ??= new DropCollectorUIState();
			StorageBusState ??= new StorageBusUIState();
			StorageUnitState ??= new StorageUnitUIState();
			NetworkOverviewState ??= new NetworkOverviewUIState();

			bool allReady = true;
			allReady &= ActivateState("物品浏览器", SearchState);
            allReady &= ActivateState("无线桥", BridgeState);
			allReady &= ActivateState("帮助", HelpState);
			allReady &= ActivateState("磁盘驱动器", DriveState);
			allReady &= ActivateState("合成终端", TerminalState);
			allReady &= ActivateState("分子装配室", AssemblerState);
			allReady &= ActivateState("来源 / 用途", RecipeState);
			allReady &= ActivateState("怪物详情", NpcState);
			allReady &= ActivateState("合成 CPU", CraftingCpuState);
			allReady &= ActivateState("掉落收集器", DropCollectorState);
			allReady &= ActivateState("存储总线", StorageBusState);
			allReady &= ActivateState("存储单元", StorageUnitState);
			allReady &= ActivateState("网络概览", NetworkOverviewState);

			// 全部成功才算「准备好了」;有失败就留着下一帧重试(失败的窗口会再试一次)。
			_statesReady = allReady;
		}

		/// <summary>界面状态是否已经全部初始化完成。</summary>
		private static bool _statesReady;

		/// <summary>已经打过日志的失败窗口名 —— 免得每帧重试时刷屏。</summary>
		private static readonly HashSet<string> _stateErrorLogged = new HashSet<string>();

		/// <summary>
		/// 已经成功初始化过的窗口。自己记一份,「只激活一次」就不依赖
		/// UIElement.Activate() 是否幂等 —— 拖动 / 反复开关窗口都不会重建控件。
		/// </summary>
		private static readonly HashSet<string> _stateActivated = new HashSet<string>();

		/// <summary>
		/// 激活一个窗口状态,并且**不往外抛**:失败只记一次日志、返回 false。
		/// 这样任何一个窗口初始化失败,都不会连累其它窗口。
		/// </summary>
		private static bool ActivateState(string name, UIState state)
		{
			if (state == null)
			{
				return false;
			}

			if (_stateActivated.Contains(name))
			{
				return true;
			}

			try
			{
				state.Activate();
				_stateActivated.Add(name);
				return true;
			}
			catch (System.Exception ex)
			{
				if (_stateErrorLogged.Add(name))
				{
					AE2ForTerraria.Instance?.Logger.Error(
						"窗口「" + name + "」初始化失败,已跳过它(其它窗口不受影响):" + ex);
				}

				return false;
			}
		}

		public override void Unload()
		{
            AEUiSession.Reset(false);
			// 把「输入被我们屏蔽」的状态复位,并放开输入法 ——
			// 否则界面上重载模组之后,游戏会一直按键没反应、也打不进字。
			if (_blockedInput)
			{
				Main.blockInput = false;
				PlayerInput.WritingText = false;
				_blockedInput = false;
			}

			SearchUIState.ReleaseImeForUnload();

			WindowLayout.Save();
			UIStatus.Clear();
			UITextCache.Clear();
            BrowserCategories.Reset(); BrowserFavorites.Reset(); BrowserHover.Reset(); UIChoiceMenu.Close();

			Interface = null;
			OverlayInterface = null;
			SecondaryInterface = null;
			SearchState = null; BridgeState = null; ResetTerminalRead();
			HelpState = null;
			DriveState = null;
			TerminalState = null;
			AssemblerState = null;
			RecipeState = null;
			NpcState = null;
			CraftingCpuState = null;
			DropCollectorState = null;
			StorageBusState = null;
			StorageUnitState = null;
			NetworkOverviewState = null;
			ActiveNetwork = null;
			ActiveNetworkContents = null;
			ActiveAssembler = null;
			ActiveCraftingCpu = null;
			RemoteContents.Clear();

			// 状态对象都清了,「已经初始化完成」的标记也得跟着复位,
			// 否则重载模组之后会以为状态还在,窗口再也建不出来。
			_statesReady = false;
			_stateErrorLogged.Clear();
			_stateActivated.Clear();
		}

		public override void PostUpdateInput()
		{
			AEUiSession.Advance();
			if (Main.dedServ || Interface == null || Main.gameMenu)
			{
				return;
			}

			EnsureStates();

			// 界面开着的时候,把游戏本身的输入让给我们:
			// 在搜索框里敲字母不该开地图 / 触发快捷键 / 用掉手上的物品。
			// WritingText 与 blockInput 是原版「有人正在输入文字」的两个标志(原版聊天框就靠它们),
			// mouseInterface 负责让左键不使用物品。
			bool uiOpen = Interface.CurrentState != null
				|| SecondaryInterface?.CurrentState != null
				|| OverlayInterface?.CurrentState != null;
			BlockGameInput(AEUiSession.KeyboardCaptured);

			var focused=OverlayInterface?.CurrentState as MovableWindowState
                ?? SecondaryInterface?.CurrentState as MovableWindowState ?? Interface.CurrentState as MovableWindowState;
            if (focused?.SearchBoxFocused == true || ImeInput.CompositionString.Length>0) return;
            if (uiOpen && Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))
            {
                if (UIChoiceMenu.IsOpen) UIChoiceMenu.Close();
                else if (focused is SearchUIState search && search.CloseDetails()) { }
                else if (focused is TerminalUIState terminal && terminal.CloseDetails()) { }
                else Close();
                return;
            }
            ModKeybind keybind = AE2ForTerraria.OpenSearchKeybind;
			if (keybind == null)
			{
				return;
			}

			// 远程打开已绑定的访问终端
			ModKeybind remote = AE2ForTerraria.OpenRemoteTerminalKeybind;
			if (remote != null && KeyPressed(remote, uiOpen, ref _prevRemoteCurrent))
			{
				OpenBoundTerminal();
				return;
			}

			// 网络概览
			ModKeybind overview = AE2ForTerraria.OpenNetworkOverviewKeybind;
			if (overview != null && KeyPressed(overview, uiOpen, ref _prevOverviewCurrent))
			{
				if (SecondaryInterface?.CurrentState == NetworkOverviewState)
				{
					SecondaryInterface.SetState(null);
				}
				else
				{
					OpenNetworkOverview();
				}

				return;
			}

			// 随身磁铁开关
			ModKeybind magnet = AE2ForTerraria.ToggleMagnetKeybind;
			if (magnet != null && KeyPressed(magnet, uiOpen, ref _prevMagnetCurrent))
			{
				Player local = Main.LocalPlayer;

				if (local != null)
				{
					MEPlayer modPlayer = local.GetModPlayer<MEPlayer>();
					modPlayer.MagnetEnabled = !modPlayer.MagnetEnabled;

					bool global = MEConfig.Instance?.PersonalMagnetEnabled ?? false;

					UIStatus.Toast(modPlayer.MagnetEnabled
						? ModText.Get("MsgMagnetOn", "随身磁铁:开")
							+ (global ? string.Empty
								: "  (" + ModText.Get("MsgMagnetGlobalOff", "但模组配置里总开关是关的") + ")")
						: ModText.Get("MsgMagnetOff", "随身磁铁:关"));
				}

				return;
			}

			if (!KeyPressed(keybind, uiOpen, ref _prevSearchCurrent))
			{
				return;
			}

			// 同一个按键开关窗口。之前这里有个「搜索框有焦点就不响应」的判断,
			// 而焦点标记一旦置位就不会复位,导致窗口再也关不掉 —— 已去掉。
			if (Interface.CurrentState != null || SecondaryInterface?.CurrentState != null
				|| OverlayInterface?.CurrentState != null)
			{
				CloseAll();
			}
			else
			{
				OpenSearch();
			}
		}

		private static bool _blockedInput;
		private static bool _prevSearchCurrent;
		private static bool _prevRemoteCurrent;
		private static bool _prevOverviewCurrent;
		private static bool _prevMagnetCurrent;

		/// <summary>界面打开时屏蔽游戏输入;关掉界面时把自己设过的标志复位。</summary>
		private static void BlockGameInput(bool uiOpen)
		{
			if (uiOpen)
			{
				Main.blockInput = true;
				PlayerInput.WritingText = true;
				_blockedInput = true;

				// 滚轮也是游戏输入:不加这一句,在界面上滚轮会顺手切物品栏 / 缩放地图。
				// vanilla 只在自己那一份「界面占着鼠标滚轮」的名单为空时才处理滚轮
				// (Player.Update 里 HandleHotbar 那段),这名单每帧会被清掉,所以要每帧登记。
				PlayerInput.LockVanillaMouseScroll("AE2ForTerraria");

				Player player = Main.LocalPlayer;

				if (player != null)
				{
					player.mouseInterface = true; // 左键不使用手上的物品
				}
			}
			else if (_blockedInput)
			{
				// 只复位自己设过的那一份,免得干扰原版 / 其他模组
				Main.blockInput = false;
				PlayerInput.WritingText = false;
				_blockedInput = false;
				ImeInput.Blur();
			}
		}

		/// <summary>
		/// 界面开着时键盘被我们屏蔽了,ModKeybind 可能因此读不到按键 —— 兜底自己看一遍原始按键状态,
		/// 否则会出现「窗口打开后就关不掉」。
		/// </summary>
		private static bool KeyPressed(ModKeybind keybind, bool uiOpen, ref bool previousCurrent)
		{
			bool current = keybind.Current;
			bool rising = current && !previousCurrent;
			previousCurrent = current;

			if (keybind.JustPressed)
			{
				return true;
			}

			if (!uiOpen)
			{
				return false;
			}

			// 兜底一:按住状态自己算上升沿(JustPressed 被屏蔽时它通常还读得到)
			if (rising)
			{
				return true;
			}

			// 兜底二:直接看原始按键状态
			try
			{
				foreach (string name in keybind.GetAssignedKeys(InputMode.Keyboard))
				{
					if (!System.Enum.TryParse(name, out Keys key))
					{
						continue;
					}

					if (Main.keyState.IsKeyDown(key) && Main.oldKeyState.IsKeyUp(key))
					{
						return true;
					}
				}
			}
			catch
			{
				// 拿不到键位就算了,JustPressed 那条路还在
			}

			return false;
		}

		public static void SetActiveNetwork(
			List<IMEStorage> drives,
			MEMolecularAssemblerEntity assembler,
			MECraftingCpuEntity cpu = null)
		{
			ActiveNetwork = drives;
			ActiveAssembler = assembler;
			ActiveCraftingCpu = cpu;
            ActiveCraftingTerminal = cpu as MECraftingTerminalEntity;
			RefreshNetwork();
		}

		public static void RefreshNetwork()
        {
            if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
            { ActiveNetworkContents = GetRemoteContents(ActiveTerminalPos); return; }
            if (ActiveTerminalPos.X >= 0)
            {
                ActiveNetwork = MENetwork.CollectStorage(ActiveTerminalPos.X,ActiveTerminalPos.Y);
                ActiveAssembler = MENetwork.FindAssembler(ActiveTerminalPos.X,ActiveTerminalPos.Y);
                ActiveCraftingCpu = MENetwork.FindCraftingCpu(ActiveTerminalPos.X,ActiveTerminalPos.Y);
                ActiveCraftingTerminal = ActiveCraftingCpu as MECraftingTerminalEntity;
            }
			if (ActiveNetwork != null && ActiveNetwork.Count > 0)
			{
				ActiveNetworkContents = MENetwork.Aggregate(ActiveNetwork);
				return;
			}

			// 本地一个存储实体都没有(典型情况:开着远程终端,人在远处)
			// 就用服务器发来的整网汇总,否则界面会是空的。
			ActiveNetworkContents = GetRemoteContents(ActiveTerminalPos);
		}

		public static long GetStoredAmount(ItemKey key)
		{
			if (ActiveNetworkContents == null)
			{
				return 0;
			}

			return ActiveNetworkContents.TryGetValue(key, out long amount) ? amount : 0L;
		}

		public static void OpenSearch()
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("物品浏览器", SearchState))
			{
				return;
			}

			// 清掉上一个终端的坐标,免得 RemoteContents 里的旧汇总被当成当前网络的
			ActiveTerminalPos = new Point16(-1, -1); ResetTerminalRead();
			SetActiveNetwork(null, null);
			SearchState.ResetForOpen();
			Interface?.SetState(SearchState);
		}

		public static void OpenDrive(MEDiskDriveEntity drive)
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("磁盘驱动器", DriveState))
			{
				return;
			}

			// 多人:先让服务器把磁盘槽位快照发过来
			MENet.RequestLedger(drive.Position);

			DriveState.SetDrive(drive);
			SecondaryInterface?.SetState(DriveState);
            Main.playerInventory = true;
		}

		public static void OpenTerminal(int i, int j, TerminalViewMode mode = TerminalViewMode.Craft)
		{
			ActiveTerminalViewMode = mode;
			EnsureStates();
			CloseOverlay();
			SecondaryInterface?.SetState(null);

			if (!ActivateState("合成终端", TerminalState))
			{
				return;
			}

			ActiveTerminalPos = MachineTiles.Origin(i, j);
            MachineTiles.EnsureTerminal(ActiveTerminalPos.X, ActiveTerminalPos.Y);
            ResetTerminalRead();

			// 多人:先让服务器把库存快照发过来
			if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) { TerminalRead.Begin(ActiveTerminalPos); MENet.RequestTerminalRead(ActiveTerminalPos, TerminalRead.Epoch); }

			SetActiveNetwork(
				MENetwork.CollectStorage(i, j),
				MENetwork.FindAssembler(i, j),
				MENetwork.FindCraftingCpu(i, j));
			Interface?.SetState(TerminalState);
            TerminalState.ResetForOpen();
            Main.playerInventory = true;
		}

		public static void OpenAssembler(MEMolecularAssemblerEntity assembler)
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("分子装配室", AssemblerState))
			{
				return;
			}

			AssemblerState.SetAssembler(assembler);
			SecondaryInterface?.SetState(AssemblerState);
		}

		/// <summary>打开掉落收集器自己的过滤界面。</summary>
		public static void OpenDropCollector(MEDropCollectorEntity collector)
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("掉落收集器", DropCollectorState))
			{
				return;
			}

			MENet.RequestLedger(collector.Position);
			DropCollectorState.SetCollector(collector);
			SecondaryInterface?.SetState(DropCollectorState);
		}

		/// <summary>打开存储总线的分区清单界面。</summary>
		public static void OpenStorageBus(MEStorageBusEntity bus)
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("存储总线", StorageBusState))
			{
				return;
			}

			MENet.RequestLedger(bus.Position);
			StorageBusState.SetBus(bus);
			SecondaryInterface?.SetState(StorageBusState);
		}

		/// <summary>打开 ME 存储单元的界面(和驱动器同款内容面板)。</summary>
		public static void OpenStorageUnit(MEStorageUnitEntity unit)
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("存储单元", StorageUnitState))
			{
				return;
			}

			MENet.RequestLedger(unit.Position);
			StorageUnitState.SetUnit(unit);
			SecondaryInterface?.SetState(StorageUnitState);
		}

		public static void OpenCraftingCpu(MECraftingCpuEntity cpu)
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("合成 CPU", CraftingCpuState))
			{
				return;
			}

			CraftingCpuState.SetCpu(cpu);
			SecondaryInterface?.SetState(CraftingCpuState);
		}

		/// <summary>打开「世界里都有哪些网络」的总览。</summary>
		public static void OpenNetworkOverview()
		{
			EnsureStates();
			CloseOverlay();

			if (!ActivateState("网络概览", NetworkOverviewState))
			{
				return;
			}

			NetworkOverviewState.RefreshNetworks();
			SecondaryInterface?.SetState(NetworkOverviewState);
		}

		/// <summary>远程打开绑定的访问终端(走玩家身上的绑定坐标)。</summary>
		private static void OpenBoundTerminal()
		{
			Player player = Main.LocalPlayer;
			if (player == null)
			{
				return;
			}

			var modPlayer = player.GetModPlayer<MEPlayer>();
			modPlayer.PruneBindings();

			if (!modPlayer.HasTerminalBinding)
			{
				Main.NewText(ModText.Get("MsgNoBinding", "还没有绑定 ME 访问终端"), 255, 180, 180);
				return;
			}

			Point16 bound = modPlayer.PrimaryTerminal;
			OpenTerminal(bound.X, bound.Y, TerminalViewMode.Access);
		}

		/// <summary>打开配方覆盖窗口(来源 / 用途)。</summary>
		public static void OpenRecipe(int itemType, bool uses)
        {
            EnsureStates();
            if (OverlayInterface?.CurrentState == null && SecondaryInterface?.CurrentState == null)
            {
                if (Interface?.CurrentState == SearchState) { SearchState.ShowDetails(itemType, uses); return; }
                if (Interface?.CurrentState == TerminalState) { TerminalState.ShowDetails(itemType, uses); return; }
            }

			// 必须先确保这个窗口初始化过(Activate),再 Show:
			// Show → Refresh 会直接操作窗口里的文本 / 控件,没初始化过就是空引用。
			if (!ActivateState("来源 / 用途", RecipeState))
			{
				return;
			}

			SeparateFromMainWindow(RecipeState);
			RecipeState.Show(itemType, uses);
			OverlayInterface?.SetState(RecipeState);
		}

		/// <summary>打开怪物详情覆盖窗口。</summary>
		public static void OpenNpc(int npcType)
        {
            EnsureStates();
            if (Interface?.CurrentState == SearchState && OverlayInterface?.CurrentState == null && SecondaryInterface?.CurrentState == null)
            { SearchState.ShowNpcDetails(npcType); return; }

			if (!ActivateState("怪物详情", NpcState))
			{
				return;
			}

			SeparateFromMainWindow(NpcState);
			NpcState.Show(npcType);
			OverlayInterface?.SetState(NpcState);
		}

		/// <summary>打开帮助页(子页面)。</summary>
		public static void OpenHelp()
		{
			EnsureStates();

			if (!ActivateState("帮助", HelpState))
			{
				return;
			}

			SeparateFromMainWindow(HelpState);
			HelpState.RefreshText();
			OverlayInterface?.SetState(HelpState);
		}

		/// <summary>
		/// 子页面和主页面分离:子页面几乎完全压在主窗口上时,把它挪到主窗口旁边
		/// (右边放不下就放左边,再不行放下面)。
		/// 只处理「基本盖住」的情况 —— 玩家自己拖出来的位置不动。
		/// </summary>
		private static void SeparateFromMainWindow(MovableWindowState sub)
		{
			if (sub == null || Interface == null)
			{
				return;
			}

			MovableWindowState main = Interface.CurrentState as MovableWindowState;

			if (main == null || main == sub)
			{
				return;
			}

			WindowGeometry a = main.Layout;
			WindowGeometry b = sub.Layout;

			if (a == null || b == null)
			{
				return;
			}

			float overlapW = Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X);
			float overlapH = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y);
			float overlap = Math.Max(0f, overlapW) * Math.Max(0f, overlapH);

			if (overlap < b.W * b.H * 0.6f)
			{
				return; // 没怎么压住,保持原样
			}

			float right = a.X + a.W + 12f;
			float left = a.X - b.W - 12f;

			if (right + b.W <= Main.screenWidth - 8f)
			{
				b.X = right;
				b.Y = a.Y;
			}
			else if (left >= 8f)
			{
				b.X = left;
				b.Y = a.Y;
			}
			else
			{
				// 左右都放不下就往下面放,至少别整块盖住
				b.X = a.X;
				b.Y = a.Y + a.H + 12f;
			}

			sub.FitToScreen();
		}

		public static void Close()
		{
			// 覆盖窗口的 × 只关覆盖层,底下那个窗口(浏览器 / 终端)继续开着
			if (OverlayInterface?.CurrentState != null)
			{
				OverlayInterface.SetState(null);
				return;
			}

			// 方块窗口(驱动器 / 装配室 / 概览…)关掉后,底下的浏览器 / 终端继续开着
			if (SecondaryInterface?.CurrentState != null)
			{
				SecondaryInterface.SetState(null);
				return;
			}

			Interface?.SetState(null); ResetTerminalRead();
		}

		/// <summary>一键关掉所有层(按键开关窗口时用,不用按三次)。</summary>
		public static void CloseAll()
		{
            UIChoiceMenu.Close();
			OverlayInterface?.SetState(null);
			SecondaryInterface?.SetState(null);
			Interface?.SetState(null); ResetTerminalRead();
		}

		/// <summary>
		/// 界面必须在 UpdateUI 阶段更新,不能放在绘制层里。
		/// 之前放在绘制层,UITextBox 处理文本输入时已经太晚 —— 于是搜索框输不进字。
		/// </summary>
		public override void UpdateUI(GameTime gameTime)
		{
			if (Main.dedServ || Main.gameMenu || Interface == null)
			{
				return;
			}

			EnsureStates();
            if (Interface.CurrentState == TerminalState && ++_refreshTicks >= 30)
            {
                _refreshTicks=0;
                if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) { if (TerminalRead.ShouldRequest) MENet.RequestTerminalRead(ActiveTerminalPos, TerminalRead.Epoch); }
                else RefreshNetwork();
            }

            // 状态提示的 2.5 秒计时靠这里推进(不用游戏时间,免得暂停时不动)
			UIStatus.Advance(gameTime.ElapsedGameTime.TotalSeconds);

			using (AEUiSession.EnterOwnedInput())
            {
                if (OverlayInterface?.CurrentState != null) OverlayInterface.Update(gameTime);
                else if (SecondaryInterface?.CurrentState != null) SecondaryInterface.Update(gameTime);
                else Interface.Update(gameTime);
            }
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (Main.dedServ)
			{
				return;
			}

			int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
			if (index < 0)
			{
				return;
			}

			layers.Insert(index, new LegacyGameInterfaceLayer(
				AEUiSession.LayerName,
				() =>
				{
					if (Main.gameMenu || Interface == null)
					{
						return true;
					}

					EnsureStates();

                    if (AEUiSession.CapturesInput) AEUiSession.BeginHover();
                    AEUiSession.DrawWindow(Interface, Main._drawInterfaceGameTime);
                    AEUiSession.DrawWindow(SecondaryInterface, Main._drawInterfaceGameTime);
                    AEUiSession.DrawWindow(OverlayInterface, Main._drawInterfaceGameTime);
                    if (AEUiSession.CapturesInput) AEUiSession.DrawHover();

					DrawToasts();

					return true;
				},
				InterfaceScaleType.UI));
		}

		/// <summary>把窗口内的状态提示画在屏幕下方(聊天栏只留「做不成」的原因)。</summary>
		private static void DrawToasts()
		{
			var active = UIStatus.Active();
			if (active.Count == 0)
			{
				return;
			}

			var font = Terraria.GameContent.FontAssets.MouseText.Value;
			float y = Main.screenHeight - 190f;

            for (int i = 0; i < active.Count; i++)
            {
                (string text, Color color, float alpha) = active[i];
                var size = font.MeasureString(text) * 0.85f;
                float x = (Main.screenWidth - size.X) / 2f;

                // 深色底 + 左侧一条颜色边:成功绿 / 警告黄 / 失败红一眼分得清
                var box = new Rectangle((int)x - 14, (int)y - 4, (int)size.X + 34,
                    (int)size.Y + 8);

                UI.UITheme.FillRounded(Main.spriteBatch, box, new Color(14, 18, 30) * (0.82f * alpha), 4);
                UI.UITheme.Fill(Main.spriteBatch,
                    new Rectangle(box.X + 4, box.Y + 3, 3, System.Math.Max(1, box.Height - 6)),
                    color * alpha);

                Terraria.Utils.DrawBorderString(Main.spriteBatch, text, new Vector2(x + 4f, y),
                    color * alpha, 0.85f);

                y += size.Y + 10f;
            }
		}
	}
}
