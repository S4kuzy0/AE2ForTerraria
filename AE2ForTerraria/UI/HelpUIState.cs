using System;
using AE2ForTerraria.Systems;
using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace AE2ForTerraria.UI
{
	/// <summary>
	/// 帮助页 —— **单独一个子页面**,不再挤在主窗口里当浮层,也没有「帮助翻页」按钮:
	/// 一整篇写在可以滚轮滚动的框里,滚着看就行。
	/// </summary>
	public sealed class HelpUIState : MovableWindowState
	{
		private static readonly WindowGeometry Geometry = WindowGeometry.Compact();

		/// <summary>
		/// 帮助正文的回退文案。**要和 Localization 里的 `UI.HelpBody` 保持一致** ——
		/// 以前 hjson 有 5 段、代码里只有 2 段,漏翻的语言会看到残缺的帮助。
		/// 行尽量短(40 个字以内),窄窗口下才不会顶出框。
		/// </summary>
		private const string HelpFallback =
			"【搜索】\n"
			+ "Tab 开物品浏览器 / O 网络概览 / P 打开绑定的终端\n"
			+ "中文 / 英文 / 全拼 / 拼音首字母都能搜\n"
			+ "@模组名 = 只看这个模组的物品\n"
			+ "← → Home End 移光标,Delete 删一个,Ctrl+A 全选\n"
			+ "Ctrl+V 粘贴 / Ctrl+Z 清空 / Ctrl+C 复制\n"
			+ "\n【浏览】\n"
			+ "左键 = 看来源(R)  右键 = 看用途(U)\n"
			+ "方向键选格子 + 回车打开\n"
			+ "滚轮上下滚(往下滚 = 往后看);PageUp / PageDown 翻一屏\n"
			+ "Alt + 左键 收藏 / 取消，金色星标表示已收藏\n"
            + "收藏树拖动标题移动；眼睛切换常显 / 仅打开背包时显示\n"
            + "收藏树持续读取终端库存，只展开缺少材料；盔甲悬停查看套装效果\n"
            + "Shift + 滚轮横向查看深层分支；重复配方点击引用可跳转\n"
            + "历史保留最近十次浏览，重开先回目录，记录跨重启保存\n"
			+ "排序按钮:默认 / 时间点 / 名称 / 最近查看\n"
			+ "\n【窗口】\n"
			+ "窗口的任意空位按住左键就能拖,不用非去抓边框\n"
			+ "只拖右下角斜线缩放；其余边框移动，右上角重置布局\n"
			+ "关闭只关最上面那层,底下的窗口还在\n"
			+ "刚打开的子页面会自动躲开主窗口\n"
			+ "\n【作弊扳手】\n"
			+ "右下角扳手点亮(变红)= 作弊模式\n"
			+ "点亮后 Shift + 左键直接刷出一组物品\n"
			+ "配置里关掉「作弊模式」时这个按钮不出现\n"
			+ "\n【快捷搬运】(终端 / 内容界面)\n"
			+ "Shift + 左键  网格 = 取一整叠,背包格 = 存进网络\n"
			+ "Alt + 左键    网格 = 收藏合成树，不取出物品\n"
			+ "Ctrl + 左键   网格 = 加入多选,配合「取出选中」\n"
			+ "M = 开关随身掉落磁铁(配置里另有总开关)\n"
			+ "\n【网络】\n"
			+ "维持库存 = 少于设定数量自动补货(格子上有青色角标)\n"
			+ "驱动器 / 存储单元 / 存储总线都能设分区清单\n"
			+ "网络概览里可以改名、看构成与占用、查存取日志\n";

		private UIText _hint;
		private UIScrollText _body;
        private UIButton _guide, _goals;

		public override void OnInitialize()
		{
			InitWindow(Geometry, ModText.Get("BtnHelp", "? 帮助"));
            _guide = new UIButton("终端入门", .75f);
            _guide.OnLeftClick += (_, _) => UISystem.OpenGuide(GuidePage.Tutorial);
            _goals = new UIButton("收藏目标 · 下一步", .75f);
            _goals.OnLeftClick += (_, _) => UISystem.OpenGuide(GuidePage.Goals);
            Window.Append(_guide); Window.Append(_goals);

			_hint = new UIText(string.Empty, 0.75f, false);
			_hint.TextColor = UITheme.Muted;
			Window.Append(_hint);

			_body = new UIScrollText();
			Window.Append(_body);

			_body.SetText(ModText.Get("HelpBody", HelpFallback));
			_hint.SetText(ModText.Get("HelpHint", "滚轮上下翻;这篇帮助没有分页"));

			RequestLayout();
		}

		/// <summary>语言切换 / 重新打开时刷一遍正文。</summary>
		public void RefreshText()
		{
			_body?.SetText(ModText.Get("HelpBody", HelpFallback));
			_hint?.SetText(ModText.Get("HelpHint", "滚轮上下翻;这篇帮助没有分页"));
		}

		protected override void ApplyLayout()
		{
			base.ApplyLayout();

			float w = Geo.W;
			float h = Geo.H;

			_hint.Left.Set(Pad + 4f, 0f);
			_hint.Top.Set(66f, 0f);
            foreach (var button in new[] { _guide, _goals }) { button.Top.Set(34, 0); button.Height.Set(24, 0); button.Width.Set((w - Pad * 2 - 4) / 2, 0); }
            _guide.Left.Set(Pad, 0); _goals.Left.Set(w / 2 + 2, 0);

			_body.Left.Set(Pad, 0f);
			_body.Top.Set(88f, 0f);
			_body.Width.Set(Math.Max(160f, w - Pad * 2f), 0f);
			_body.Height.Set(Math.Max(80f, h - 88f - 20f), 0f);

			Window.Recalculate();
		}
	}
}
