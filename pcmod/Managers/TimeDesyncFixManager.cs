using System;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.Managers;

// TODO: Investigate the following:
// Glenn Fiedler’s Snapshot Interpolation framework (from Gaffer on Games)
// Linear Phase-Locked Loop (PLL)
// Kalman Filter
// Exponential Weighted Moving Average (EWMA)
// Exponential Moving Average (EMA) Filters
// RTT-based protocols (NTP-style): if we can timestamp packets at both ends, we could estimate one-way latency more directly. However, this may not be feasible in our environment and can be less robust to asymmetric delays.
// Time-Stamp Interp / Extrapolation (Dead Reckoning for Timelines)
// Snapshot Interpolation with Dynamic Timeline Buffering (specifically looking into the works of Glenn Fiedler / Gaffer on Games and implementations like Mirror Networking's standalone SnapshotInterpolation library).
// TODO: https://arxiv.org/pdf/2512.11492
// TODO: https://ieeexplore.ieee.org/document/11139073
// TODO: https://dl.acm.org/doi/10.1145/3519023


/// <summary>
/// Manages and corrects the time synchronization gap (desync) between a remote streaming server 
/// and the local Beat Saber game clock instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mathematical Theory &amp; Framework:</b>
/// This manager utilizes a three-tier synchronization pipeline to eliminate network jitter and clock drift:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>First-Order Linear Extrapolation:</b> When estimating the server's state between packets, 
/// the system projects time forward linearly assuming a constant time velocity (velocity = 1), such that:
/// EstimatedServerTime = PacketServerTime + LocalDeltaTimeSincePacket.
/// This bridges the asynchronous gap between network packet arrivals and the local frame loop.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Exponential Moving Average (EMA) Filtering:</b> Raw network latency is inherently noisy (jitter). 
/// To prevent the synchronization loop from over-correcting for a single delayed packet, a low-pass 
/// discrete filter is applied to the calculated offset. The mathematical recurrence relation is:
/// SmoothedOffset = (Alpha * RawOffset) + ((1 - Alpha) * PreviousSmoothedOffset).
/// Where Alpha (EmaAlpha) represents the degree of weighting decrease (smoothing factor).
/// https://www.investopedia.com/terms/e/ema.asp
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Clock Slewing (Phase Adjustment):</b> Rather than calling disruptive buffer-flushing operations like 
/// SeekTo for small corrections, the algorithm slowly slews the clock phase. 
/// It manipulates the internal reference offset (_audioStartTimeOffsetSinceStart) 
/// proportionally to the smoothed error over time, dynamically accelerating or decelerating the perceived local progression 
/// to converge with the server's timeline.
/// </description>
/// </item>
/// </list>
/// </remarks>
public class TimeDesyncFixManager : ITickable
{
    private readonly AudioTimeSyncController _syncController;
    private readonly SiraLog _siraLog;

    /// <summary>
    /// The most recent authoritative song timestamp received from the streaming server.
    /// </summary>
    private float _latestServerSongTime;
    
    /// <summary>
    /// The high-resolution, monotonic local system time recorded when the latest server packet was captured.
    /// Measured in seconds since application startup using Time.realtimeSinceStartupAsDouble.
    /// </summary>
    private double _lastPacketReceivedTime;
    
    /// <summary>
    /// A state guard ensuring synchronization tracking logic does not execute until at least one 
    /// valid network payload has established an initial time boundary.
    /// </summary>
    private bool _hasReceivedPacket;
    
    /// <summary>
    /// The smoothing factor (Alpha) utilized by the Exponential Moving Average low-pass filter.
    /// Valid range is between 0.0 and 1.0. A value of 0.2 indicates that 20% of the current network 
    /// packet sample weight is combined with 80% of historical offset tracking data.
    /// </summary>
    private const float EmaAlpha = 0.2f;
    
    /// <summary>
    /// The running history variable accumulating the filtered time offset state.
    /// Represents the true, jitter-stripped difference between where the server is and where the client is.
    /// </summary>
    private double _smoothedTimeDriftOffset = 0;



    // Configuration Thresholds (in seconds)
    /// <summary>
    /// The dead-zone cushion threshold (in seconds). Micro-drifts under this limit (e.g., 15 milliseconds) 
    /// are entirely bypassed to preserve audio stability and prevent fighting the native Unity DSP clock.
    /// </summary>
    private const float MaxAcceptableDrift = 0.015f; // 15ms. Ignore micro-jitters below this.
    
    /// <summary>
    /// The critical desync upper boundary limit (in seconds). If the filtered offset exceeds this value (200 milliseconds),
    /// a structural lag spike or scene load is assumed, and a hard timeline snap is executed via a Seek operation.
    /// </summary>
    private const float HardSnapThreshold = 0.200f;  // 200ms. Past this, force a destructive snap.
    
    /// <summary>
    /// The proportional gain multiplier governing the clock-slewing convergence velocity. 
    /// Higher values close the synchronization gap faster but risk more noticeable visual tracking variance.
    /// </summary>
    private const float SlewingStrength = 1.5f;      // Speed multiplier for catching up.

    [Inject]
    public TimeDesyncFixManager(SiraLog siraLog, AudioTimeSyncController syncController)
    {
        _siraLog = siraLog;
        _syncController = syncController;
    }
    
    public void Tick()
    {
        if (!_syncController.isAudioLoaded || !_syncController.isReady) return;
        if (_syncController.state != AudioTimeSyncController.State.Playing) return;
        if (!_hasReceivedPacket) return;

        // 1. Linearly extrapolate what the server's song time should be right now
        float timeSinceLastPacket = (float)(Time.realtimeSinceStartupAsDouble - _lastPacketReceivedTime);
        float estimatedServerSongTime = _latestServerSongTime + timeSinceLastPacket;

        // 2. Measure actual desync against Beat Saber's current local song time
        float currentLocalTime = _syncController.songTime;
        float timeDriftOffset = estimatedServerSongTime - currentLocalTime;

        // 3. Smooth the noise using Exponential Moving Average
        // https://en.wikipedia.org/wiki/Exponential_smoothing#Comparison_with_moving_average
        _smoothedTimeDriftOffset = (EmaAlpha * timeDriftOffset) + ((1.0f - EmaAlpha) * _smoothedTimeDriftOffset);
        double absoluteOffset = Math.Abs(_smoothedTimeDriftOffset);

        // Case A: Massive Lag Spike / Desync -> Hard Snap using Native Method
        if (absoluteOffset > HardSnapThreshold)
        {
            _siraLog.Warn($"[Desync] Massive drift ({absoluteOffset:F3}s). Executing Hard Seek.");
            _syncController.SeekTo(estimatedServerSongTime);
            _smoothedTimeDriftOffset = 0;
            return;
        }

        // Case B: Small clock drift or frame-rate drop -> Slew the clock smoothly
        if (absoluteOffset > MaxAcceptableDrift)
        {
            // Calculate how much adjustment we need this frame
            float adjustmentThisFrame = (float)(_smoothedTimeDriftOffset * SlewingStrength * Time.deltaTime);

            // Gaining insight from the decompiled code:
            // num2 (the expected song timeline) relies entirely on timeSinceStart - _audioStartTimeOffsetSinceStart.
            // Shifting _audioStartTimeOffsetSinceStart backwards pushes local time FORWARD to catch up to the server.
            _syncController._audioStartTimeOffsetSinceStart -= adjustmentThisFrame;
            
            // Flag internal system that we are currently manipulating the sync state
            _syncController._fixingAudioSyncError = true;
        }
    }

    /// <summary>
    /// Feed the incoming server song timestamps into this manager.
    /// </summary>
    public void UpdateTime(float serverSongTime)
    {
        _latestServerSongTime = serverSongTime;
        _lastPacketReceivedTime = Time.realtimeSinceStartupAsDouble;
        _hasReceivedPacket = true;
    }
}