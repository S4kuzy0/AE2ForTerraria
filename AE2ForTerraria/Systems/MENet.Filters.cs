using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AE2ForTerraria.Core;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    private const int MaxFilterIdentityBytes = 1024;
    private const int MaxFilterReferences = 100000;
    private static readonly UTF8Encoding FilterEncoding = new(false, true);

    // ModPacket stores its complete frame length in a ushort. Measure the actual envelope,
    // including tML's variable-size mod ID, rather than assuming a fixed header length.
    internal static bool TrySerializeFilterRequest(Action<BinaryWriter> write, long envelopeBytes, out byte[] payload)
    {
        payload = null;
        try
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            write(writer);
            if (envelopeBytes < 0 || stream.Length > ushort.MaxValue - envelopeBytes) return false;
            payload = stream.ToArray();
            return true;
        }
        catch (InvalidDataException) { return false; }
        catch (EncoderFallbackException) { return false; }
    }

    private static bool SendFilterRequest(Point16 position, Action<BinaryWriter> write)
    {
        using var packet = CreatePacket();
        if (!TrySerializeFilterRequest(write, packet.BaseStream.Position, out byte[] payload))
        {
            UIStatus.Toast("名单过大或包含无效身份，未提交；服务器原配置保持不变，请减少条目或修正名单");
            RequestLedger(position);
            return false;
        }
        packet.Write(payload);
        packet.Send();
        return true;
    }

    // Each entry is an item identity, never executable mod data or a session-specific mod item ID.
    internal static void WriteFilter(BinaryWriter writer, IEnumerable<int> filter)
    {
        var identities = ItemPersistence.FilterIdentities(filter);
        if (identities.Count > MaxFilterReferences) throw new InvalidDataException("Filter contains too many item identities");
        writer.Write(identities.Count);
        foreach (string identity in identities)
        {
            byte[] bytes = FilterEncoding.GetBytes(identity);
            if (bytes.Length == 0 || bytes.Length > MaxFilterIdentityBytes)
                throw new InvalidDataException("Filter item identity is too long");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }

    internal static List<int> ReadFilter(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxFilterReferences || count > (reader.BaseStream.Length - reader.BaseStream.Position) / 3)
            throw new InvalidDataException("Invalid filter length");
        var identities = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadUInt16();
            if (length == 0 || length > MaxFilterIdentityBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid filter item identity length");
            identities.Add(FilterEncoding.GetString(reader.ReadBytes(length)));
        }
        // Filters terminate every current message. Reject malformed suffixes before publishing any settings.
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Trailing filter configuration");
        return ItemPersistence.FilterFromIdentities(identities);
    }
}
