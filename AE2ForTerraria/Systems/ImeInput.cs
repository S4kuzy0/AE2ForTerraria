using Microsoft.Xna.Framework;
using ReLogic.Localization.IME;
using ReLogic.OS;
using Terraria;
using Terraria.GameInput;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 输入法(中文)接入 —— **直接用游戏自己的那一套**。
	///
	/// 之前走了弯路:自己挂 FNA 的 TextInputEXT、又加 imm32 兜底,中文还是进不来。
	/// 原因是泰拉瑞亚的文本输入分两半:
	///   1. `ReLogic.Localization.IME.IImeService`(Windows 上是 imm32 实现)——
	///      聊天框 / 告示牌吃中文靠的就是它;
	///   2. 它必须靠 `PlayerInput.WritingText = true` + `Main.instance.HandleIME()`
	///      才会被打开;上屏的字符进 Main 自己的按键队列,最后由
	///      `Main.GetInputText()` 拼进字符串里。
	/// 我们以前只挂了自己的一套,既没打开这个服务、也没读它,所以打拼音只出字母。
	/// (Recipe Browser 的搜索框走的就是上面这个流程,所以它的中文输入是好的。)
	///
	/// 现在:开输入框 = Focus(),关输入框 = Blur(),候选词列表用 DrawCandidates() 画。
	/// </summary>
	public static class ImeInput
	{
		/// <summary>输入法服务(拿不到就是 null,不影响英文输入)。</summary>
		private static IImeService Service
		{
			get
			{
				try
				{
					return Platform.Get<IImeService>();
				}
				catch
				{
					return null;
				}
			}
		}

		/// <summary>输入法是不是开着(状态栏会显示,方便一眼看出问题)。</summary>
		public static bool Enabled
		{
			get
			{
				IImeService service = Service;
				return service != null && service.IsEnabled;
			}
		}

		/// <summary>正在组的那串拼音(没在组字就是空串)。</summary>
		public static string CompositionString
		{
			get
			{
				IImeService service = Service;

				if (service == null || !service.IsEnabled)
				{
					return string.Empty;
				}

				try
				{
					return service.CompositionString ?? string.Empty;
				}
				catch
				{
					return string.Empty;
				}
			}
		}

		/// <summary>
		/// 搜索框拿到焦点:打开输入法。
		/// WritingText 也要置位 —— 那是原版的「现在有人在打字」标志。
		/// </summary>
		public static void Focus()
		{
			PlayerInput.WritingText = true;

			IImeService service = Service;

			if (service == null || service.IsEnabled)
			{
				return;
			}

			try
			{
				service.Enable();
			}
			catch
			{
				// 输入法服务异常就退回纯英文输入,不该把界面搞崩
			}
		}

		/// <summary>输入框失去焦点:关掉输入法(聊天框 / 告示牌还开着的时候不动它)。</summary>
		public static void Blur()
		{
			if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
			{
				return;
			}

			IImeService service = Service;

			if (service == null || !service.IsEnabled)
			{
				return;
			}

			try
			{
				service.Disable();
			}
			catch
			{
				// 同上
			}
		}

		/// <summary>
		/// 把候选词列表画出来(原版那套面板,和聊天框长得一样)。
		/// 由窗口在绘制阶段调用,position 一般是搜索框的位置。
		/// </summary>
		public static void DrawCandidates(Vector2 position)
		{
			IImeService service = Service;

			if (service == null || !service.IsEnabled || !service.IsCandidateListVisible)
			{
				return;
			}

			try
			{
				Main.instance?.DrawWindowsIMEPanel(position);
			}
			catch
			{
				// 画候选词失败不影响打字
			}
		}
	}
}
