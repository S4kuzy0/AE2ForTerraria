using System.Collections.Generic;
using Terraria.GameContent.UI.Elements;

namespace AE2ForTerraria.UI
{
    /// <summary>Avoid repeated SetText/layout work; weak keys do not retain closed UI elements. Callers still construct their strings.</summary>
	public static class UITextCache
	{
		private sealed class Value { public string Text; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIText,Value> Last = new();

		public static void Set(UIText text, string value)
		{
			if (text == null)
			{
				return;
			}

			value ??= string.Empty;

			if (Last.TryGetValue(text, out var previous) && previous.Text == value)
			{
				return;
			}

			Last.GetOrCreateValue(text).Text = value;
			text.SetText(value);
		}

		/// <summary>模组卸载 / 界面重建时清掉,免得留着已经不用的元素引用。</summary>
		public static void Clear()
		{
			Last.Clear();
		}
	}
}
