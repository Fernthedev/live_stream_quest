using System;
using LiveStreamQuest.Network;
using LiveStreamQuest.Protos;
using SiraUtil.Submissions;
using Zenject;

namespace LiveStreamQuest.Managers;

public class GamePacketHandler : IPacketHandler, IInitializable,IDisposable
{
    [Inject] private readonly SongController _songController;
    [Inject] private readonly PauseController _pauseController;
    [Inject] private readonly NetworkManager _networkManager;
    [Inject] private readonly Submission _submission;

    [Inject] private readonly VRControllerManager _vrControllerManager;


    public void HandlePacket(PacketWrapper packetWrapper)
    {
        switch (packetWrapper.PacketCase)
        {
            case PacketWrapper.PacketOneofCase.UpdatePosition:
                var updatePositionData = packetWrapper.UpdatePosition;
                _vrControllerManager.UpdateTransforms(updatePositionData.HeadTransform, updatePositionData.RightTransform, updatePositionData.RightTransform);
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
        _submission.DisableScoreSubmission(Plugin.ID);
        _pauseController.Pause();

        var packetWrapper = new PacketWrapper
        {
            ReadyUp = new ReadyUp()
        };

        _networkManager.SendPacket(packetWrapper);
    }

    public void Dispose()
    {
        _submission?.Dispose();
    }
}