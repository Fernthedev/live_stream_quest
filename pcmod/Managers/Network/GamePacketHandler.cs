using System;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using SiraUtil.Submissions;
using SiraUtil.Tools.FPFC;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

public class GamePacketHandler : IInitializable, IDisposable
{
    [Inject] private readonly SongController _songController;
    [Inject] private readonly AudioTimeSyncController _audioTimeSyncController;
    [Inject] private readonly PauseController _pauseController;
    [Inject] private readonly NetworkManager _networkManager;
    [Inject] private readonly Submission _submission;
    [Inject] private readonly LSQMainThreadDispatcher _mainThreadDispatcher;


    [Inject] private readonly IReturnToMenuController _returnToMenuController;
    [Inject] private readonly VRControllerManager _vrControllerManager;
    [Inject] private readonly SiraLog _siraLog;

    private ulong _packetId;
    private bool _ready;

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
                    updatePositionData.RightTransform, updatePositionData.LeftTransform, updatePositionData.Time);
                break;
            case PacketWrapper.PacketOneofCase.StartMap:
                _siraLog.Info("Resuming the map");
                _ready = true;
                ResumeMap();

                if (_audioTimeSyncController is { isReady: true, isAudioLoaded: true, _canStartSong: true })
                {
                    _audioTimeSyncController.SeekTo(packetWrapper.StartMap.SongTime);
                }

                break;
            case PacketWrapper.PacketOneofCase.ExitMap:
                _siraLog.Info("Exit the map");
                _songController.StopSong();
                _returnToMenuController.ReturnToMenu();

                break;
            case PacketWrapper.PacketOneofCase.PauseMap:
                _siraLog.Info("Pause map");

#if BS_1_29
                _mainThreadDispatcher.Enqueue(PauseMap);
#else
                _mainThreadDispatcher.DispatchOnMainThread(PauseMap);
#endif
                break;
        }
    }

    private void ResumeMap()
    {
        // if (_fpfc != null)
        // {
        //     _fpfc.enabled = false;
        // }
        //
        // if (_siraFpfc != null)
        // {
        //     _siraFpfc.Enabled = false;
        // }

        _pauseController.HandlePauseMenuManagerDidPressContinueButton();
    }

    private void PauseMap()
    {
        // if (_fpfc != null)
        // {
        //     _fpfc.enabled = _fpfc._shouldBeEnabled;
        // }
        //
        // if (_siraFpfc != null)
        // {
        //     _siraFpfc.Enabled = _fpfcEnabled;
        // }

        _pauseController.Pause();
        // _songController.PauseSong();
        // _audioTimeSyncController.Pause();
    }

    public void Initialize()
    {
        _networkManager.PacketReceivedEvent.Subscribe<PacketWrapper>(HandlePacket);
        _submission.DisableScoreSubmission(Plugin.ID);
        if (!_ready && _audioTimeSyncController.state == AudioTimeSyncController.State.Playing)
        {
            AudioTimeSyncControllerOnstateChangedEvent();
        }

        _audioTimeSyncController.stateChangedEvent -= AudioTimeSyncControllerOnstateChangedEvent;
        _audioTimeSyncController.stateChangedEvent += AudioTimeSyncControllerOnstateChangedEvent;
    }

    // Pause until ready
    private void AudioTimeSyncControllerOnstateChangedEvent()
    {
        if (_ready) return;
        if (_audioTimeSyncController.state != AudioTimeSyncController.State.Playing) return;

        PauseMap();

        _siraLog.Info("Send ready up packet");
        // Tell Quest we're ready
        var pausePacketWrapper = new PacketWrapper
        {
            ReadyUp = new ReadyUp()
        };
        _networkManager.SendPacket(pausePacketWrapper);
    }

    public void Dispose()
    {
        _submission.Dispose();
        _networkManager.PacketReceivedEvent.Unsubscribe<PacketWrapper>(HandlePacket);
    }
}