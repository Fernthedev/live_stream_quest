using System.Net;
using Google.Protobuf;
using LiveStreamQuest.Managers.Network;
using LiveStreamQuest.Protos;

namespace LiveStreamQuest.Tests;

public class FramedPacketTests
{
    [Test]
    public void Encode_WritesNetworkOrderLengthPrefix()
    {
        var packet = new PacketWrapper
        {
            ReadyUp = new ReadyUp()
        };

        var framed = FramedPacket.Encode(packet);
        var payloadLength = packet.CalculateSize();

        Assert.That(framed, Has.Length.EqualTo(FramedPacket.HeaderSize + payloadLength));
        Assert.That(FramedPacket.DecodeLength(framed), Is.EqualTo(payloadLength));
        Assert.That(framed[FramedPacket.HeaderSize..], Is.EqualTo(packet.ToByteArray()));
    }

    [Test]
    public void DecodeLength_RoundTripsEncodedValue()
    {
        const int length = 0x01234567;
        var prefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(length));

        Assert.That(FramedPacket.DecodeLength(prefix), Is.EqualTo(length));
    }
}
