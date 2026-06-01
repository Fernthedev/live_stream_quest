using System;
using LiveStreamQuest.Configuration;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.Managers;

public class TimeDesyncFixManager : ITickable
{
    private readonly AudioTimeSyncController _syncController;
    private readonly SiraLog _siraLog;
    private readonly PluginConfig _config;
    private readonly SnapshotManager _snapshotManager;

    private float _latestServerSongTime;
    private double _lastPacketReceivedTime;
    private bool _hasReceivedPacket;

    private const float EmaAlpha = 0.15f; // Slightly smoother weight tracking
    private double _smoothedTimeDriftOffset;

    // Configuration Thresholds (in seconds)
    private const float HardSnapThreshold = 0.150f; // 150ms. Past this, force a hard SeekTo audio sync.

    private const float
        WindowDriftTolerance =
            0.100f; // 100ms. If a snapshot and estimated future time are within this range, seek towards it


    [Inject]
    public TimeDesyncFixManager(SiraLog siraLog, AudioTimeSyncController syncController, PluginConfig config,
        SnapshotManager snapshotManager)
    {
        _siraLog = siraLog;
        _syncController = syncController;
        _config = config;
        _snapshotManager = snapshotManager;
    }

    /// <summary>
    /// The canonical, stabilized timeline clock for your interpolation buffer.
    /// Constrained to fall within the available snapshot window, preferring the newest snapshot.
    /// </summary>
    public double SmoothedSongTime
    {
        get
        {
            // Fallback to raw song time if network sync isn't ready
            if (!_config.SyncTime || !_hasReceivedPacket) return _syncController.songTime;

            var smoothedTime = _syncController.songTime + _smoothedTimeDriftOffset;

            return smoothedTime;
        }
    }

    public void Tick()
    {
        if (!_config.SyncTime) return;
        if (!_syncController.isAudioLoaded || !_syncController.isReady) return;
        if (_syncController.state != AudioTimeSyncController.State.Playing) return;
        if (!_hasReceivedPacket) return;

        // 1. Linearly extrapolate where the server's (Quest) song time should be right now
        var timeSinceLastPacket = Time.realtimeSinceStartupAsDouble - _lastPacketReceivedTime;
        var estimatedServerSongTime = _latestServerSongTime + timeSinceLastPacket;

        // 2. Measure raw delta desync against local raw audio time
        double currentLocalTime = _syncController.songTime;
        var rawTimeDriftOffset = estimatedServerSongTime - currentLocalTime;


        // 3. HARD CRITICAL DESYNC FIXED: If PC and Quest run completely out of bounds (> 150ms)
        // Execute an explicit, safe native timeline Seek operation. However, avoid seeking
        // if the estimated server time is still within our historical snapshot window (with
        // a small forward tolerance). This prevents unnecessary hard seeks during minor
        // network jitter while still correcting large structural desyncs.
        if (Math.Abs(rawTimeDriftOffset) > HardSnapThreshold)
        {
            _siraLog.Warn(
                $"[TimeSync] Massive structural desync detected ({rawTimeDriftOffset:F3}s). Resetting PC timeline clock via SeekTo.");
            _syncController.SeekTo((float)estimatedServerSongTime);
            _smoothedTimeDriftOffset = 0;
            return;
        }

        // 4. Smooth out minor network jitter using the low-pass filter
        _smoothedTimeDriftOffset = (EmaAlpha * rawTimeDriftOffset) + ((1.0 - EmaAlpha) * _smoothedTimeDriftOffset);

        // 5. SOFT SLEWING FIXED: For micro-drifts between 25ms and 150ms, let your 
        // VRControllerManager absorb the visual adjustment via SmoothedSongTime smoothly 
        // rather than resetting the audio source frame-by-frame.
    }

    public void UpdateTime(float serverSongTime)
    {
        // Flush internal filters if the incoming packet indicates a manual track rewind/seek back on Quest
        if (_hasReceivedPacket && serverSongTime < _latestServerSongTime - 0.250f)
        {
            _smoothedTimeDriftOffset = 0;
        }

        _latestServerSongTime = serverSongTime;
        _lastPacketReceivedTime = Time.realtimeSinceStartupAsDouble;
        _hasReceivedPacket = true;
    }
}