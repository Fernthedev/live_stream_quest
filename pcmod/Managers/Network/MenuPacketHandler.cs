using System;
using LiveStreamQuest.Protos;
using Zenject;

namespace LiveStreamQuest.Managers;

public class MenuPacketHandler : IPacketHandler
{
    public void HandlePacket(PacketWrapper packetWrapper)
    {
        switch (packetWrapper.PacketCase)
        {
            case PacketWrapper.PacketOneofCase.StartBeatmap:
                break;
        }
    }
}