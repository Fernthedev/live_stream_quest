using System;
using System.Net;
using Google.Protobuf;
using LiveStreamQuest.Protos;

namespace LiveStreamQuest.Managers.Network;

/// <summary>
/// Shared framing helpers for the 4-byte big-endian length prefix used by both transports.
/// </summary>
internal static class FramedPacket
{
    /// <summary>
    /// Gets the size of the length prefix in bytes.
    /// </summary>
    public const int HeaderSize = sizeof(uint);

    /// <summary>
    /// Encodes a protobuf packet into a single framed buffer with a 4-byte network-order prefix.
    /// </summary>
    public static byte[] Encode(PacketWrapper packetWrapper)
    {
        var packetLen = packetWrapper.CalculateSize();
        var lenBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(packetLen));
        var framed = new byte[HeaderSize + packetLen];
        
        Buffer.BlockCopy(lenBytes, 0, framed, 0, HeaderSize);
        packetWrapper.WriteTo(new Span<byte>(framed, HeaderSize, packetLen));
        
        return framed;
    }
    
    public static byte[] Encode(byte[] raw)
    {
        var len =  BitConverter.GetBytes(IPAddress.HostToNetworkOrder(raw.Length));
        var framed = new byte[HeaderSize + raw.Length];
        Buffer.BlockCopy(len, 0, framed, 0, HeaderSize);
        Buffer.BlockCopy(raw, 0, framed, HeaderSize, raw.Length);
        
        return framed;
    }

    /// <summary>
    /// Reads the payload length from a framed buffer.
    /// </summary>
    public static int DecodeLength(byte[] buffer, int offset = 0)
    {
        // TODO: This does not work with Uint32
        return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer, offset));
    }
}
