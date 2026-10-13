using Terraria.ModLoader;
using System.IO;
using AE2ForTerraria.Systems;

namespace AE2ForTerraria
{
	/// <summary>
	/// 模组入口。框架版只负责承载各子系统的注册,本身不注册内容。
	/// </summary>
	public class AE2ForTerraria : Mod
	{
		public static AE2ForTerraria Instance => ModContent.GetInstance<AE2ForTerraria>();

		/// <summary>打开查询窗口的按键。可在 设置 → 控制 里改。</summary>
		public static ModKeybind OpenSearchKeybind { get; private set; }

		/// <summary>远程打开已绑定的合成终端(需要先右键 ME 访问终端绑定)。</summary>
		public static ModKeybind OpenRemoteTerminalKeybind { get; private set; }

		/// <summary>打开「世界里的 ME 网络概览」。</summary>
		public static ModKeybind OpenNetworkOverviewKeybind { get; private set; }

		/// <summary>随身掉落磁铁的开关(玩家级,会同步到服务器)。</summary>
		public static ModKeybind ToggleMagnetKeybind { get; private set; }

		public override void Load()
		{
			OpenSearchKeybind = KeybindLoader.RegisterKeybind(this, "OpenSearch", "Tab");
			OpenRemoteTerminalKeybind = KeybindLoader.RegisterKeybind(this, "OpenRemoteTerminal", "P");
			OpenNetworkOverviewKeybind = KeybindLoader.RegisterKeybind(this, "OpenNetworkOverview", "O");
			ToggleMagnetKeybind = KeybindLoader.RegisterKeybind(this, "ToggleMagnet", "M");
		}

		public override void Unload()
		{
			OpenSearchKeybind = null;
			OpenRemoteTerminalKeybind = null;
			OpenNetworkOverviewKeybind = null;
			ToggleMagnetKeybind = null;
		}

		/// <summary>多人同步包的入口。客户端发请求,服务器执行并广播快照。</summary>
		public override void HandlePacket(BinaryReader reader, int whoAmI)
		{
			MENet.Handle(reader, whoAmI);
		}
	}
}
