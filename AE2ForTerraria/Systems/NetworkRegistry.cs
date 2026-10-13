using System;
using System.Linq;
using System.Collections.Generic;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems
{
    /// <summary>World policies keyed by persistent component membership; physical anchors locate current networks.</summary>
	public sealed partial class NetworkRegistry : ModSystem
	{
		private static readonly Dictionary<string, string> Names = new Dictionary<string, string>();

		/// <summary>网络所有者(玩家名)。无主网络谁先用谁成为所有者。</summary>
		private static readonly Dictionary<string, string> Owners = new Dictionary<string, string>();

		/// <summary>网络权限表:锚点 → (玩家名 → 权限等级)。所有者隐含最高级。</summary>
		private static readonly Dictionary<string, Dictionary<string, int>> Acl =
			new Dictionary<string, Dictionary<string, int>>();

		/// <summary>审计日志:锚点 → 最近若干条存取记录(新在前)。</summary>
		private static readonly Dictionary<string, List<string>> Audit =
			new Dictionary<string, List<string>>();

		/// <summary>每个网络保留多少条日志。</summary>
		public const int AuditLimit = 40;

		/// <summary>
		/// 物品级锁定:锚点 → 被锁的物品 ID 集合。
		/// 被锁的物品只有所有者 / 管理者能取 —— 「可存取」权限的人也不行。
		/// 用来保护关键物品(比如唯一的 Boss 召唤物)。
		/// </summary>
		private static readonly Dictionary<string, HashSet<int>> Locked =
			new Dictionary<string, HashSet<int>>();

		public static bool IsLocked(Point16 anchor, int itemType)
            => Policies(anchor).Any(k=>Locked.TryGetValue(k,out var set) && set.Contains(itemType));

		public static void SetLocked(Point16 anchor, int itemType, bool locked)
        {
            if (anchor.X<0 || !Core.SavedItemGuard.IsValidType(itemType)) return;
            foreach (string key in Policies(anchor))
            {
                if (!Locked.TryGetValue(key,out var set)) Locked[key]=set=new();
                if (locked) set.Add(itemType); else set.Remove(itemType);
            }
        }

		/// <summary>整份替换锁定清单(勾选网格编辑完一次性提交)。</summary>
		public static void SetLockedList(Point16 anchor, List<int> types)
        {
            if (anchor.X<0) return;
            foreach (string key in Policies(anchor)) Locked[key]=new((types ?? new()).Where(Core.SavedItemGuard.IsValidType));
        }

		public static List<int> GetLocked(Point16 anchor)
            => Policies(anchor).Where(Locked.ContainsKey).SelectMany(k=>Locked[k]).Distinct().ToList();

		public static void ApplyRemoteLocked(Point16 anchor, List<int> types)
		{
			if (anchor.X < 0)
			{
				return;
			}

			Locked[Key(anchor)] = types == null ? new HashSet<int>() : new HashSet<int>(types);
		}

		/// <summary>记一条审计。玩家名 / 动作 / 物品 / 数量 / 时间。</summary>
		public static void RecordAudit(Point16 anchor, string player, string action, string item, long amount)
		{
			if (anchor.X < 0 || string.IsNullOrEmpty(player))
			{
				return;
			}

			string key = Policies(anchor)[0];

			if (!Audit.TryGetValue(key, out List<string> list))
			{
				list = new List<string>();
				Audit[key] = list;
			}

			string stamp = System.DateTime.Now.ToString("MM-dd HH:mm");
			list.Insert(0, stamp + "|" + player + "|" + action + "|" + (item ?? "") + "|" + amount);

			while (list.Count > AuditLimit)
			{
				list.RemoveAt(list.Count - 1);
			}
		}

		public static List<string> GetAudit(Point16 anchor)
            => Policies(anchor).Where(Audit.ContainsKey).SelectMany(k=>Audit[k]).Distinct().OrderByDescending(k=>k).Take(AuditLimit).ToList();

		/// <summary>客户端收到的日志副本(只用于显示)。</summary>
		public static void ApplyRemoteAudit(Point16 anchor, List<string> entries)
		{
			if (anchor.X >= 0)
			{
				Audit[Key(anchor)] = entries ?? new List<string>();
			}
		}

		/// <summary>权限等级。</summary>
		public const int LevelNone = 0;
		public const int LevelReadOnly = 1;
		public const int LevelUse = 2;
		public const int LevelManage = 3;

		private static string Key(Point16 anchor)
		{
			return anchor.X + "," + anchor.Y;
		}

		public static string GetName(Point16 anchor)
        {
            string value=string.Join(" / ",Policies(anchor).Where(Names.ContainsKey).Select(k=>Names[k]).Distinct());
            return value.Length==0 ? null : value;
        }

		public static void SetName(Point16 anchor, string name)
        {
            if (anchor.X<0) return;
            foreach (string key in Policies(anchor))
                if (string.IsNullOrWhiteSpace(name)) Names.Remove(key); else Names[key]=name.Trim();
        }

		public static string GetOwner(Point16 anchor)
        {
            var owners=Policies(anchor).Where(Owners.ContainsKey).Select(k=>Owners[k]).Distinct().ToList();
            return owners.Count==0 ? null : owners.Count==1 ? owners[0] : "[合并网络：需要各所有者共同授权，或断开连接]";
        }

		/// <summary>认领一个无主网络。已经有主就返回 false。</summary>
		public static bool TryClaim(Point16 anchor, string playerName)
        {
            if (anchor.X<0 || string.IsNullOrWhiteSpace(playerName) || GetOwner(anchor)!=null) return false;
            foreach (string key in Policies(anchor)) Owners[key]=playerName;
            return true;
        }

		/// <summary>某个玩家在这个网络里的权限等级。所有者永远是最高级。</summary>
		public static int GetLevel(Point16 anchor, string playerName)
        {
            if (anchor.X<0 || string.IsNullOrWhiteSpace(playerName)) return LevelNone;
            int result=LevelManage; bool owned=false;
            foreach (string key in Policies(anchor))
            {
                if (!Owners.TryGetValue(key,out string owner)) continue;
                owned=true;
                int level=owner==playerName ? LevelManage
                    : Acl.TryGetValue(key,out var table) && table.TryGetValue(playerName,out int explicitLevel) ? explicitLevel : LevelNone;
                result=Math.Min(result,level);
            }
            return owned ? result : LevelUse;
        }

		public static void SetLevel(Point16 anchor, string playerName, int level)
        {
            if (anchor.X<0 || string.IsNullOrWhiteSpace(playerName)) return;
            foreach (string key in Policies(anchor))
            {
                if (!Acl.TryGetValue(key,out var table)) Acl[key]=table=new();
                if (level<=0) table.Remove(playerName); else table[playerName]=Math.Clamp(level,0,3);
            }
        }

		public static Dictionary<string, int> GetAcl(Point16 anchor)
        {
            var result=new Dictionary<string,int>();
            foreach (string key in Policies(anchor))
                if (Acl.TryGetValue(key,out var table)) foreach (string player in table.Keys) result[player]=GetLevel(anchor,player);
            return result;
        }

		/// <summary>
		/// 客户端收到服务器的权限快照后写进本地副本(只用于显示)。
		/// 服务器仍然是权威,客户端的改动只是一个请求。
		/// </summary>
		public static void ApplyRemoteSecurity(Point16 anchor, string owner, Dictionary<string, int> table)
		{
			if (anchor.X < 0)
			{
				return;
			}

			string key = Key(anchor);

			if (string.IsNullOrEmpty(owner))
			{
				Owners.Remove(key);
			}
			else
			{
				Owners[key] = owner;
			}

			Acl[key] = table ?? new Dictionary<string, int>();
		}

		public override void SaveWorldData(TagCompound tag)
		{
			SaveMembership(tag);
			var list = new List<TagCompound>();

			foreach (KeyValuePair<string, string> pair in Names)
			{
				var entry = new TagCompound();
				entry["k"] = pair.Key;
				entry["v"] = pair.Value;
				list.Add(entry);
			}

			tag["networkNames"] = list;

			var owners = new List<TagCompound>();
			foreach (KeyValuePair<string, string> pair in Owners)
			{
				var entry = new TagCompound();
				entry["k"] = pair.Key;
				entry["v"] = pair.Value;
				owners.Add(entry);
			}

			tag["networkOwners"] = owners;

			var acl = new List<TagCompound>();
			foreach (KeyValuePair<string, Dictionary<string, int>> pair in Acl)
			{
				var entry = new TagCompound();
				entry["k"] = pair.Key;

				var inner = new List<TagCompound>();
				foreach (KeyValuePair<string, int> level in pair.Value)
				{
					var one = new TagCompound();
					one["p"] = level.Key;
					one["l"] = level.Value;
					inner.Add(one);
				}

				entry["list"] = inner;
				acl.Add(entry);
			}

			tag["networkAcl"] = acl;

			var audit = new List<TagCompound>();
			foreach (KeyValuePair<string, List<string>> pair in Audit)
			{
				var entry = new TagCompound();
				entry["k"] = pair.Key;
				entry["list"] = pair.Value;
				audit.Add(entry);
			}

			tag["networkAudit"] = audit;

			var lockedList = new List<TagCompound>();
			foreach (KeyValuePair<string, HashSet<int>> pair in Locked)
			{
				var entry = new TagCompound();
				entry["k"] = pair.Key;
				Core.ItemPersistence.SaveFilter(entry,"list",pair.Value);
				lockedList.Add(entry);
			}

			tag["networkLocked"] = lockedList;
		}

		public override void LoadWorldData(TagCompound tag)
		{
			LoadMembership(tag);
			Names.Clear();
			Owners.Clear();
			Acl.Clear();
			Audit.Clear();
			Locked.Clear();

			foreach (TagCompound entry in tag.GetList<TagCompound>("networkNames"))
			{
				string key = entry.GetString("k");
				string value = entry.GetString("v");

				if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
				{
					Names[key] = value;
				}
			}

			if (tag.ContainsKey("networkOwners"))
			{
				foreach (TagCompound entry in tag.GetList<TagCompound>("networkOwners"))
				{
					string key = entry.GetString("k");
					string value = entry.GetString("v");

					if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
					{
						Owners[key] = value;
					}
				}
			}

			if (tag.ContainsKey("networkAcl"))
			{
				foreach (TagCompound entry in tag.GetList<TagCompound>("networkAcl"))
				{
					string key = entry.GetString("k");
					if (string.IsNullOrEmpty(key))
					{
						continue;
					}

					var table = new Dictionary<string, int>();

					foreach (TagCompound one in entry.GetList<TagCompound>("list"))
					{
						string player = one.GetString("p");
						if (!string.IsNullOrEmpty(player))
						{
							table[player] = TagCompat.ReadInt(one, "l");
						}
					}

					Acl[key] = table;
				}
			}

			if (tag.ContainsKey("networkAudit"))
			{
				foreach (TagCompound entry in tag.GetList<TagCompound>("networkAudit"))
				{
					string key = entry.GetString("k");

					if (!string.IsNullOrEmpty(key))
					{
						Audit[key] = new List<string>(entry.GetList<string>("list"));
					}
				}
			}

			if (tag.ContainsKey("networkLocked"))
			{
				foreach (TagCompound entry in tag.GetList<TagCompound>("networkLocked"))
				{
					string key = entry.GetString("k");

					if (string.IsNullOrEmpty(key))
					{
						continue;
					}

                    var references=Core.ItemPersistence.LoadFilter(entry,"list");
                    var set=new HashSet<int>(references);
                    Core.ItemPersistence.CopyMissingFilterReferences(references,set);

					Locked[key] = set;
				}
			}
		}

		public override void ClearWorld()
		{
			Membership.Clear(); Components.Clear();
			Names.Clear();
			Owners.Clear();
			Acl.Clear();
			Audit.Clear();
			Locked.Clear();
		}
	}
}
