using System.Collections.Generic;
using Terraria.GameContent.UI.Elements;

namespace AE2ForTerraria.UI
{
    /// <summary>Avoid repeated SetText/layout work; weak keys do not retain closed UI elements. Callers still construct their strings.</summary>
	public static class UITextCache
	{
		private sealed class Value { public string Text; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIText,Value> Last = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIText,object> Fitted = new();

		public static void Set(UIText text, string value)
		{
			if (text == null)
			{
				return;
			}

			value ??= string.Empty;
            bool fittedNow = text.DynamicallyScaleDownToWidth && !Fitted.TryGetValue(text, out _);
            if (fittedNow)
            {
                // UIText.Recalculate calls InternalSetText, which restores its natural
                // MinWidth. This callback runs after that write and before layout uses it.
                text.OnInternalTextChange += () => text.MinWidth.Set(0, 0);
                Fitted.Add(text, new object());
            }

			if (!fittedNow && Last.TryGetValue(text, out var previous) && previous.Text == value)
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
