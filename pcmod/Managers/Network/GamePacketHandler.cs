using System;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using SiraUtil.Submissions;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

public class GamePacketHandler : IInitializable, IDisposable
{
    [Inject] private readonly SongController _songController;
    [Inject] private readonly PauseController _pauseController;
    [Inject] private readonly NetworkManager _networkManager;
    [Inject] private readonly Submission _submission;

    [Inject] private readonly VRControllerManager _vrControllerManager;
    [Inject] private readonly SiraLog _siraLog;

    private ulong _packetId;


    public void HandlePacket(PacketWrapper packetWrapper)
    {
        switch (packetWrapper.PacketCase)
        {
            case PacketWrapper.PacketOneofCase.UpdatePosition:
                var updatePositionData = packetWrapper.UpdatePosition;
                // ignore old packet
                if (_packetId > packetWrapper.QueryResultId) return;
                _packetId = packetWrapper.QueryResultId;

                _vrControllerManager.UpdateTransforms(updatePositionData.HeadTransform,
                    updatePositionData.RightTransform, updatePositionData.RightTransform, updatePositionData.Time);
                break;
            case PacketWrapper.PacketOneofCase.StartMap:
                _siraLog.Info("Resuming the map");
                _pauseController.HandlePauseMenuManagerDidPressContinueButton();
                
                break;
            case PacketWrapper.PacketOneofCase.ExitMap:
                _siraLog.Info("Exit the map");

                _songController.StopSong();
                break;
        }
    }

    public void Initialize()
    {
        _networkManager.PacketReceivedEvent.Subscribe<PacketWrapper>(HandlePacket);
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
        _submission.Dispose();
        _networkManager.PacketReceivedEvent.Unsubscribe<PacketWrapper>(HandlePacket);
    }
}