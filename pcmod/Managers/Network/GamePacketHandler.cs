using System;
using LiveStreamQuest.Network;
using LiveStreamQuest.Protos;
using Zenject;

namespace LiveStreamQuest.Managers;

public class GamePacketHandler : IPacketHandler, IInitializable
{
    [Inject] private readonly SongController _songController;

    [Inject] private readonly PauseController _pauseController;
    [Inject] private readonly NetworkManager _networkManager;


    public void HandlePacket(PacketWrapper packetWrapper)
    {
        switch (packetWrapper.PacketCase)
        {
            case PacketWrapper.PacketOneofCase.UpdatePosition:
                // TODO:
                break;
            case PacketWrapper.PacketOneofCase.StartMap:
                _pauseController.HandlePauseMenuManagerDidPressContinueButton();
                break;
            case PacketWrapper.PacketOneofCase.ExitMap:
                _songController.StopSong();
                break;
        }
    }

    public void Initialize()
    {
        _pauseController.Pause();

        var packetWrapper = new PacketWrapper
        {
            ReadyUp = new ReadyUp()
        };

        _networkManager.SendPacket(packetWrapper);
    }
}