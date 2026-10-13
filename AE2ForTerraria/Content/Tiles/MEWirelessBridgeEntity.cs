using System.IO;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Content.Tiles
{
	/// <summary>
	/// 无线桥接器:同一频道的两个桥接器会互相连通,
	/// 于是两块地上的 ME 网络就算隔很远也算一个网络。
	/// </summary>
	public class MEWirelessBridgeEntity : ModTileEntity
	{
		public const int MaxChannel = 9;

		/// <summary>频道(0..9)。右键循环切换。</summary>
		public int Channel { get; private set; }

        public bool Enabled { get; private set; } = true;

        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            Enabled = enabled;
            MENetwork.InvalidateCache();
        }

		public void CycleChannel()
		{
			Channel = (Channel + 1) % (MaxChannel + 1);
            Enabled = true;
            MENetwork.InvalidateCache();
		}

		/// <summary>服务器用来把客户端切好的频道写回真实实体。</summary>
		public void SetChannel(int channel)
		{
			if (channel < 0 || channel > MaxChannel)
			{
				channel = 0;
			}

			Channel = channel;
            MENetwork.InvalidateCache();
		}

		public override bool IsTileValidForEntity(int x, int y)
		{
			return MachineTiles.IsEntityAnchor(x, y, ModContent.TileType<MEWirelessBridgeTile>());
		}

		public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				NetMessage.SendTileSquare(Main.myPlayer, i, j, 1);
				NetMessage.SendData(MessageID.TileEntityPlacement, -1, -1, null, i, j, Type);
				return -1;
			}

			return Place(i, j);
		}

		public override void SaveData(TagCompound tag)
		{
			tag["channel"] = Channel;
            tag["enabled"] = Enabled;
		}

		public override void LoadData(TagCompound tag)
		{
			Channel = TagCompat.ReadInt(tag, "channel", 0);
			if (Channel < 0 || Channel > MaxChannel)
			{
				Channel = 0;
			}
            Enabled = !tag.ContainsKey("enabled") || tag.GetBool("enabled");
            MENetwork.InvalidateCache();
		}

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write((byte)Channel);
            writer.Write(Enabled);
        }

        public override void NetReceive(BinaryReader reader)
        {
            int channel = reader.ReadByte();
            bool enabled = reader.ReadBoolean();
            if (channel > MaxChannel) throw new InvalidDataException("Invalid wireless bridge channel");
            Channel = channel;
            Enabled = enabled;
            MENetwork.InvalidateCache();
        }
	}
}
