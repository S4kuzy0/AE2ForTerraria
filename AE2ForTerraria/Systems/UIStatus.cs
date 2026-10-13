using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 窗口内的状态提示(toast)。
	///
	/// 以前「已存入 N 件」「已下单」这类提示全走 Main.NewText 打到聊天栏,
	/// 一操作就刷屏、还会被别的消息挤掉。现在改成在界面底部浮一条 2.5 秒的消息,
	/// 聊天栏只留「做不成」的原因。
	/// </summary>
	public static class UIStatus
	{
		private sealed class Line
		{
			public string Text;
			public Color Color;
			public double ExpireAt;
		}

		private const int MaxLines = 5;
		private const double Lifetime = 2.5;

		private static readonly List<Line> Lines = new List<Line>();
		private static double _now;

		/// <summary>界面用的单调时钟(秒)。给「再点一次确认」这类交互计时。</summary>
		public static double Now => _now;

		/// <summary>由 UISystem 每帧推进的时间轴(不用游戏时间,免得受暂停/世界切换影响)。</summary>
		public static void Advance(double seconds)
		{
			_now += seconds;

			for (int i = Lines.Count - 1; i >= 0; i--)
			{
				if (Lines[i].ExpireAt <= _now)
				{
					Lines.RemoveAt(i);
				}
			}
		}

		public static void Toast(string text)
		{
			// 成功 / 普通提示一律低调一点,别跟报错抢注意力
			Toast(text, new Color(150, 220, 255));
		}

		public static void Toast(string text, Color color)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			Lines.Add(new Line { Text = text, Color = color, ExpireAt = _now + Lifetime });

			while (Lines.Count > MaxLines)
			{
				Lines.RemoveAt(0);
			}
		}

		public static IReadOnlyList<(string Text, Color Color, float Alpha)> Active()
		{
			var result = new List<(string, Color, float)>(Lines.Count);

			foreach (Line line in Lines)
			{
				double left = line.ExpireAt - _now;
				float alpha = left > 1.0 ? 1f : (float)System.Math.Max(0.0, left);
				result.Add((line.Text, line.Color, alpha));
			}

			return result;
		}

		public static void Clear()
		{
			Lines.Clear();
			_now = 0;
		}
	}
}
