using System;
using SiraUtil.Logging;
using Zenject;

namespace LiveStreamQuest.Managers;

public class TimeDesyncFixManager : ITickable
{
    [Inject] private readonly AudioTimeSyncController _syncController;
    [Inject] private readonly SiraLog _siraLog;

    private float _questSongTimeSeconds;

    private DateTime? _lastPacketTime;

    // the amount of time since the last time a packet was sent
    // resets every tick
    private TimeSpan _deltaPacketTime = new(0);

    public void Tick()
    {
        if (!_syncController.isAudioLoaded) return;
        if (!_syncController.isReady) return;
        if (_syncController.state != AudioTimeSyncController.State.Playing) return;
        
        if (_lastPacketTime == null) return;
        var deltaPacketTime = _deltaPacketTime;

        // reset to 0
        _deltaPacketTime = new TimeSpan(0);
        
        // Adjust for network latency
        // TODO: Actually be smart and statistical about this
        var deltaSongTime = Math.Abs(_questSongTimeSeconds - _syncController.songTime);
        
        // if distance is greater than 0.25s
        // if song time delta is greater than 75% of the packet time
        // we need to adjust
        if (deltaSongTime <= 0.25 || deltaSongTime <= deltaPacketTime.TotalSeconds * 0.75) return;
        var adjustedQuestSongTime = _questSongTimeSeconds + deltaPacketTime.TotalSeconds * 0.75;
            
        _siraLog.Info($"Adjusting song time from {_syncController.songTime} to {adjustedQuestSongTime} to account for latency ({deltaSongTime}");
        _syncController.SeekTo((float)adjustedQuestSongTime);
    }

    public void UpdateTime(float updatePositionSongTime)
    {
        _questSongTimeSeconds = updatePositionSongTime;

        var dateTime = DateTime.Now;

        var lastPacketTime = _lastPacketTime;
        if (lastPacketTime != null)
        {
            _deltaPacketTime += dateTime.Subtract(lastPacketTime.Value);
        }

        _lastPacketTime = dateTime;
    }
}