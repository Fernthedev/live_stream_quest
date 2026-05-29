using System;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using SiraUtil.Submissions;
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
    [Inject] private readonly TimeDesyncFixManager _timeDesyncFixManager;
    [Inject] private readonly SiraLog _siraLog;

    private ulong _packetId;
    private bool _ready;

    /// <summary>
    /// Subscribes to protocol events and forces a pause if the map is already playing
    /// before the PC has reported readiness.
    /// </summary>
    public void Initialize()
    {
        _networkManager.PacketReceivedEvent -= HandlePacket;
        _networkManager.PacketReceivedEvent += HandlePacket;
        _submission.DisableScoreSubmission(Plugin.ID);
        // if not ready but map is playing, we pause
        if (!_ready && _audioTimeSyncController.state == AudioTimeSyncController.State.Playing)
        {
            AudioTimeSyncControllerOnstateChangedEvent();
        }

        _audioTimeSyncController.stateChangedEvent -= AudioTimeSyncControllerOnstateChangedEvent;
        _audioTimeSyncController.stateChangedEvent += AudioTimeSyncControllerOnstateChangedEvent;
    }

    /// <summary>
    /// Handles Quest protocol packets while the game is loaded.
    /// <list type="bullet">
    /// <item><description><see cref="PacketWrapper.PacketOneofCase.UpdatePosition"/> updates remote controller snapshots.</description></item>
    /// <item><description><see cref="PacketWrapper.PacketOneofCase.StartMap"/> resumes the song once Quest has authorized the start.</description></item>
    /// <item><description><see cref="PacketWrapper.PacketOneofCase.ExitMap"/> stops playback and returns to the menu.</description></item>
    /// <item><description><see cref="PacketWrapper.PacketOneofCase.PauseMap"/> pauses locally and then sends <see cref="ReadyUp"/> back to Quest.</description></item>
    /// </list>
    /// </summary>
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
                    updatePositionData.RightTransform, updatePositionData.LeftTransform, updatePositionData.Time, updatePositionData.SongTime);
                _timeDesyncFixManager.UpdateTime(packetWrapper.UpdatePosition.SongTime);
                break;
            case PacketWrapper.PacketOneofCase.StartMap:
                _siraLog.Info("7. Starting the map");
                _ready = true;
                // Anchor the local audio clock to the authoritative song time as soon as
                // the resume packet arrives. This prevents controller snapshots from
                // starting one packet late relative to the audio timeline.
                _timeDesyncFixManager.UpdateTime(packetWrapper.StartMap.SongTime);

                if (_audioTimeSyncController is { isReady: true, isAudioLoaded: true, _canStartSong: true })
                {
                    _audioTimeSyncController.SeekTo(packetWrapper.StartMap.SongTime);
                }

                ResumeMap();

                break;
            case PacketWrapper.PacketOneofCase.ExitMap:
                _siraLog.Info("Exit the map");
                _songController.StopSong();
                _returnToMenuController.ReturnToMenu();

                break;
            case PacketWrapper.PacketOneofCase.PauseMap:
                _siraLog.Info("4. Pause map");

#if BS_1_29
                _mainThreadDispatcher.Enqueue(PauseMapAndReadyUp);
#else
                _mainThreadDispatcher.DispatchOnMainThread(PauseMapAndReadyUp);
#endif
                break;
        }
        PauseController.Start()
    }



    /// <summary>
    /// Resumes the local pause menu after Quest sends <see cref="PacketWrapper.PacketOneofCase.StartMap"/>
    /// and the authoritative song time has already been applied.
    /// </summary>
    private void ResumeMap()
    {
        _pauseController.HandlePauseMenuManagerDidPressContinueButton();
    }

    /// <summary>
    /// Pauses the local game without notifying Quest.
    /// Used by the shared pause-and-ready path after the pause has actually been applied.
    /// </summary>
    private void PauseMap()
    {
        _pauseController.Pause();
    }

    /// <summary>
    /// Pauses the local game and then sends <see cref="ReadyUp"/> to Quest.
    /// Called when Quest sends <see cref="PacketWrapper.PacketOneofCase.PauseMap"/>
    /// and when the PC is forced to pause during the startup wait path.
    /// </summary>
    private void PauseMapAndReadyUp()
    {
        PauseMap();
        ReadyUp();
    }

    /// <summary>
    /// Sends the protocol-level readiness acknowledgement back to Quest after the PC
    /// has finished pausing.
    /// </summary>
    private void ReadyUp()
    {
        // Tell Quest we're ready
        _siraLog.Info("4. Send ready up packet");
        var pausePacketWrapper = new PacketWrapper
        {
            ReadyUp = new ReadyUp()
        };
        _networkManager.SendPacket(pausePacketWrapper);
    }

    /// <summary>
    /// Fires when the audio controller changes state.
    /// If the map starts playing before Quest has finished the handshake, the
    /// PC is forced back into pause and reports <see cref="ReadyUp"/> so Quest
    /// can finish the start sequence.
    /// </summary>
    private void AudioTimeSyncControllerOnstateChangedEvent()
    {
        if (_ready) return;
        if (_audioTimeSyncController.state != AudioTimeSyncController.State.Playing) return;

        PauseMapAndReadyUp();
    }

    public void Dispose()
    {
        _submission.Dispose();
        _networkManager.PacketReceivedEvent -= HandlePacket;
    }
}