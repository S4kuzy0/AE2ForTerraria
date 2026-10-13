using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems
{
	/// <summary>
	/// 读存档里数字时的类型兼容。
	///
	/// tModLoader 的 NBT 反序列化**不保证整数是 Int32**:
	/// 玩家存档里 `boundX = 3187` 被存成了短整型(Int16),
	/// 而 `TagCompound.GetInt()` 要求值**正好**是 Int32,
	/// 于是抛 `InvalidCastException` → 整个 LoadData 失败 → **人物加载不出来**。
	///
	/// 这个坑是真实发生过的(0.16.0 用老角色进游戏时崩在 MEPlayer.LoadData)。
	/// 所以所有「读存档整数」的地方都走这里:先按 Int32,不行再试 Int16 / Int64 / 浮点。
	/// 顺带一条原则:**LoadData 永远不该抛异常**,读不出来的数据当默认值处理。
	/// </summary>
	public static class TagCompat
	{
		/// <summary>
		/// 取出存档里的**原始**值(不经过 TagCompound 的类型化取值)。
		///
		/// 为什么需要它:老把 boundX / boundY 是按 short 存进去的,而
		/// <c>TagCompound.GetInt()</c> 要求值**正好**是 Int32 —— 类型不符时
		/// tModLoader 会在 TagIO 里报一条 NBT 反序列化错误(日志里刷一片),
		/// 而且坐标可能被当成 0(绑定静默丢失)。
		/// 直接拿原始值自己转换,这两个问题就都没有了。
		/// </summary>
		private static bool TryReadRaw(TagCompound tag, string key, out object raw)
		{
			raw = null;

			try
			{
				foreach (KeyValuePair<string, object> pair in tag)
				{
					if (string.Equals(pair.Key, key, System.StringComparison.Ordinal))
					{
						raw = pair.Value;
						return raw != null;
					}
				}
			}
			catch
			{
				// 这个 tModLoader 版本不支持枚举 TagCompound —— 走下面的老路
			}

			return false;
		}

		public static int ReadInt(TagCompound tag, string key, int fallback = 0)
		{
			if (tag == null || string.IsNullOrEmpty(key) || !tag.ContainsKey(key))
			{
				return fallback;
			}

			// 原始值转换:short / ushort / int / long / byte / float / double 全吃得下
			if (TryReadRaw(tag, key, out object raw))
			{
				try
				{
					return System.Convert.ToInt32(raw);
				}
				catch
				{
					// 不是数字(例如字符串),继续往下试
				}
			}

			try
			{
				return tag.GetInt(key);
			}
			catch
			{
				// 不是 Int32,继续往下试
			}

			try
			{
				return tag.GetShort(key);
			}
			catch
			{
				// 继续
			}

			try
			{
				return (int)tag.GetLong(key);
			}
			catch
			{
				// 继续
			}

			try
			{
				return (int)tag.GetFloat(key);
			}
			catch
			{
				// 全部失败就用默认值
			}

			return fallback;
		}

		public static long ReadLong(TagCompound tag, string key, long fallback = 0L)
		{
			if (tag == null || string.IsNullOrEmpty(key) || !tag.ContainsKey(key))
			{
				return fallback;
			}

			if (TryReadRaw(tag, key, out object raw))
			{
				try
				{
					return System.Convert.ToInt64(raw);
				}
				catch
				{
					// 继续
				}
			}

			try
			{
				return tag.GetLong(key);
			}
			catch
			{
				// 继续
			}

			return ReadInt(tag, key, (int)fallback);
		}

		public static float ReadFloat(TagCompound tag, string key, float fallback = 0f)
		{
			if (tag == null || string.IsNullOrEmpty(key) || !tag.ContainsKey(key))
			{
				return fallback;
			}

			if (TryReadRaw(tag, key, out object raw))
			{
				try
				{
					return System.Convert.ToSingle(raw);
				}
				catch
				{
					// 继续
				}
			}

			try
			{
				return tag.GetFloat(key);
			}
			catch
			{
				// 存档里可能是整数(老版本写进去的)
			}

			return ReadInt(tag, key, (int)fallback);
		}
	}
}
