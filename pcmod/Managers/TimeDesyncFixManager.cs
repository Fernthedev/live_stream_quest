using System;
using SiraUtil.Logging;
using Zenject;

namespace LiveStreamQuest.Managers;

public class TimeDesyncFixManager : ITickable
{
    [Inject] private readonly AudioTimeSyncController _syncController;
    [Inject] private readonly SiraLog _siraLog;

    private float _questSongTimeSeconds;

    private DateTime _lastPacketTime;

    // the amount of time since the last time a packet was sent
    // resets every tick
    private TimeSpan _deltaPacketTime;

    public void Tick()
    {
        var deltaPacketTime = _deltaPacketTime;

        // reset to 0
        _deltaPacketTime = new TimeSpan(0);


        // if (deltaPacketTime <= new TimeSpan(days: 0, seconds:0, minutes: 0, hours:0, milliseconds: 250)) return;

        // Adjust for network latency
        // TODO: Actually be smart and statistical about this
        var adjustedQuestSongTime = _questSongTimeSeconds + deltaPacketTime.TotalSeconds * 0.75;

        // if distance is greater than half a second
        if (Math.Abs(adjustedQuestSongTime - _syncController.songTime) <= 0.5) return;

        _siraLog.Info($"Adjusting song time to {adjustedQuestSongTime} to account for latency");
        _syncController.SeekTo((float)adjustedQuestSongTime);
    }

    public void UpdateTime(float updatePositionSongTime)
    {
        _questSongTimeSeconds = updatePositionSongTime;

        var dateTime = DateTime.Now;

        _deltaPacketTime += dateTime.Subtract(_lastPacketTime);
        _lastPacketTime = dateTime;
    }
}