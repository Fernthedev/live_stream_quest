using LiveStreamQuest.Protos;

namespace LiveStreamQuest.Managers;

public interface IPacketHandler
{
    void HandlePacket(PacketWrapper packetWrapper);
}