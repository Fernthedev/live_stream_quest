using System.Collections.Generic;
using UnityEngine;

namespace LiveStreamQuest.Managers;

// This whole class is AI math
// we need to be better :(
public class SnapshotManager
{
    // Ordered historical buffer of incoming remote snapshots
    // TODO: Use sorted list
    private readonly List<VRSnapshot> _snapshots = new(12);

    /// <summary>
    /// Returns the total number of valid tracked spatial snapshots currently residing in the buffer.
    /// </summary>
    public int Count => _snapshots.Count;

    private float _currentInterpolationDelay = 0.035f; // Baseline 35ms behind
    private const float MinInterpolationDelay = 0.015f; // Never get closer than 15ms
    private const float MaxInterpolationDelay = 0.080f; // Maximum cushion of 80ms
    private const float DefaultInterpolationDelay = 0.035f;
    private const float SnapshotIntervalEmaAlpha = 0.15f;
    private float _estimatedSnapshotInterval = DefaultInterpolationDelay;


    /// <summary>
    /// Registers a newly arrived remote player snapshot into our linear historical matrix.
    /// </summary>
    /// <param name="snapshot">The validated spatial transform packet structure.</param>
    public void AddSnapshot(VRSnapshot snapshot)
    {
        var insertIndex = FindInsertionIndex(snapshot.SongTime);
        _snapshots.Insert(insertIndex, snapshot);
        
        // If the new snapshot is the latest one, we can update our estimated snapshot interval using an EMA of the last two snapshots.
        if (_snapshots.Count >= 2 && insertIndex == _snapshots.Count - 1)
        {
            var lastInterval = (float)(_snapshots[_snapshots.Count - 1].SongTime - _snapshots[_snapshots.Count - 2].SongTime);
            if (lastInterval > 0f)
            {
                _estimatedSnapshotInterval = Mathf.Lerp(_estimatedSnapshotInterval, lastInterval, SnapshotIntervalEmaAlpha);
            }
        }
    }

    /// <summary>
    /// Evaluates the timeline math for a given rendering time to extract both bounding 
    /// destination targets alongside their calculated linear fractional blending factor.
    /// </summary>
    /// <param name="renderingTimelineTime">The active, stabilized rendering playback time.</param>
    /// <returns>
    /// A named value tuple containing the structural spatial data:
    /// <list type="bullet">
    /// <item><description><c>snapshotA</c>: The past anchor frame baseline.</description></item>
    /// <item><description><c>snapshotB</c>: The future target frame destination.</description></item>
    /// <item><description><c>t</c>: The linear blending factor percentage clamped exactly between <c>[0.0f, 1.0f]</c>.</description></item>
    /// </list>
    /// </returns>
    public (VRSnapshot snapshotA, VRSnapshot snapshotB, float t) GetInterpolationData(double renderingTimelineTime)
    {
        // 1. Locate our perfectly bounded frame slices via binary search
        var (a, b) = FindBestSnapshotWindow(renderingTimelineTime);

        // 2. Compute our precise linear fraction factor 't' relative to the audio grid gap
        var windowDuration = b.SongTime - a.SongTime;
        if (windowDuration <= double.Epsilon) return (a, b, 1);

        var t = (float)((renderingTimelineTime - a.SongTime) / windowDuration);

        // 3. Clamp ensuring precision float errors are safely locked inside bounds
        t = Mathf.Clamp01(t);

        return (a, b, t);
    }

    /// <summary>
    /// Computes a stable rendering timeline by applying a smoothed interpolation delay behind the
    /// newest received snapshot.
    /// The delay is dynamically adjusted from observed packet cadence and widened when the local
    /// song clock approaches or overtakes the newest snapshot time, reducing extrapolation jitter.
    /// </summary>
    /// <param name="currentSongTime">Current local song time in seconds.</param>
    /// <returns>
    /// A delayed timeline time in song seconds used for snapshot interpolation.
    /// If fewer than two snapshots are available, returns <paramref name="currentSongTime"/> unchanged.
    /// </returns>
    public double CalculateRenderingTimelineTime(double currentSongTime)
    {
        if (_snapshots.Count < 2) return currentSongTime;

        var newestSnapshot = _snapshots[_snapshots.Count - 1];
        var clockGap = (float)(newestSnapshot.SongTime - currentSongTime);

        // Keep a small buffer behind the newest packet so the renderer usually interpolates
        // between two historical snapshots instead of chasing the latest arrival.
        var cadenceDelay = Mathf.Clamp(_estimatedSnapshotInterval * 1.8f, MinInterpolationDelay, MaxInterpolationDelay);
        var safetyAdjustedDelay = Mathf.Clamp(
            cadenceDelay + Mathf.Max(0f, 0.010f - clockGap),
            MinInterpolationDelay,
            MaxInterpolationDelay);

        _currentInterpolationDelay = Mathf.Lerp(_currentInterpolationDelay, safetyAdjustedDelay, Time.deltaTime * 8f);

        return currentSongTime - _currentInterpolationDelay;
    }


    /// <summary>
    /// Safely locates the snapshot window bounding our target rendering time pointer.
    /// </summary>
    /// <param name="renderingTimelineTime">The active, stabilized rendering playback time.</param>
    /// <returns>
    /// A named value tuple containing the two bounding historical frames:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>a</c> (<see cref="VRSnapshot"/>): The past anchor frame. It is guaranteed to be strictly 
    /// older than the timeline clock (<c>snapshotA.SongTime &lt; renderingTimelineTime</c>).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>b</c> (<see cref="VRSnapshot"/>): The future target frame. It is the immediate chronological 
    /// successor to frame <c>a</c>, guaranteed to be greater than or equal to the timeline clock 
    /// (<c>snapshotB.SongTime &gt;= renderingTimelineTime</c>). When the times match exactly, 
    /// interpolation completes fully (<c>t = 1.0</c>).
    /// </description>
    /// </item>
    /// </list>
    /// </returns>
    public (VRSnapshot a, VRSnapshot b) FindBestSnapshotWindow(double renderingTimelineTime)
    {
        // Fallback protection check to prevent critical array failures before data arrives.
        if (_snapshots.Count < 2)
        {
            var dummy = _snapshots.Count == 1 ? _snapshots[0] : default;
            return (dummy, dummy);
        }

        var lastWindowIndex = _snapshots.Count - 2;

        // BOUNDARY CASE 1: Target time has completely overshot our absolute freshest data point.
        // Clamp strictly to the last known valid historical segment window.
        if (renderingTimelineTime >= _snapshots[_snapshots.Count - 1].SongTime)
        {
            return (_snapshots[lastWindowIndex], _snapshots[lastWindowIndex + 1]);
        }

        var low = 0;
        var high = _snapshots.Count - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var midTime = _snapshots[mid].SongTime;

            // Using strict '<' means if midTime == renderingTimelineTime, it goes to the 'else' block.
            // This forces 'high' to move left, guaranteeing that any snapshot matching the time
            // will end up at the 'low' index pointer when the search finishes.
            if (midTime < renderingTimelineTime)
            {
                low = mid + 1; // Element is strictly before target time
            }
            else
            {
                high = mid - 1; // Element is after or EQUAL to target time
            }
        }

        // POST-SEARCH EQUALITY GUARANTEE:
        // Because of the strict '<' check above, when the loop breaks:
        // - _snapshots[high] is guaranteed to be STRICTLY LESS THAN renderingTimelineTime (<)
        // - _snapshots[low] is guaranteed to be GREATER THAN OR EQUAL TO renderingTimelineTime (>=)
        var targetIndex = high;

        // BOUNDARY CASE 2: Target time underruns our oldest buffered historical milestone.
        if (targetIndex < 0)
        {
            return (_snapshots[0], _snapshots[1]);
        }

        // Safety clamp to prevent out-of-bounds array access on the right edge
        if (targetIndex > lastWindowIndex)
        {
            targetIndex = lastWindowIndex;
        }

        return (_snapshots[targetIndex], _snapshots[targetIndex + 1]);
    }

    /// <summary>
    /// Performs a binary search to find the correct insertion index for a new snapshot based on its song time.
    /// </summary>
    /// <param name="songTime"></param>
    /// <returns></returns>
    private int FindInsertionIndex(double songTime)
    {
        var low = 0;
        var high = _snapshots.Count;

        while (low < high)
        {
            var mid = low + ((high - low) >> 1);
            if (_snapshots[mid].SongTime <= songTime)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>
    /// Automatically purges snapshots that are older than the specified historical time ceiling.
    /// </summary>
    /// <param name="thresholdSongTimeSeconds">The point in timeline seconds behind which all snapshots are discarded.</param>
    public void PruneOldSnapshots(double thresholdSongTimeSeconds)
    {
        _snapshots.RemoveRange(0, FindInsertionIndex(thresholdSongTimeSeconds));
    }

    public void Clear()
    {
        _snapshots.Clear();
    }

    /// <summary>
    /// Constrains the provided time to fall within the valid snapshot window.
    /// If insufficient snapshots exist, returns the provided time unchanged.
    /// Preferentially selects the newest (most recently received) snapshot when possible.
    /// </summary>
    /// <param name="desiredTime">The desired rendering time in song seconds.</param>
    /// <returns>
    /// A time guaranteed to be within the recordable snapshot window.
    /// If there are fewer than 2 snapshots, returns desiredTime unchanged.
    /// Otherwise, returns a time clamped between the oldest and newest snapshots.
    /// </returns>
    public double ConstrainTimeToSnapshotWindow(double desiredTime)
    {
        if (_snapshots.Count < 2) return desiredTime;

        var oldestTime = _snapshots[0].SongTime;
        // do we pick the newest snapshot or the second newest snapshot for time?
        var newestTime = _snapshots[_snapshots.Count - 1].SongTime;

        // Prefer the newest snapshot when the desired time is beyond our buffer
        if (desiredTime >= newestTime)
        {
            return newestTime;
        }

        // Clamp to the oldest snapshot if we're before the buffer
        if (desiredTime <= oldestTime)
        {
            return oldestTime;
        }

        return desiredTime;
    }
}

public readonly record struct VRSnapshot(
    double SongTime,
    Vector3 HeadPosition,
    Quaternion HeadRotation,
    Vector3 LeftHandPosition,
    Quaternion LeftHandRotation,
    Vector3 RightHandPosition,
    Quaternion RightHandRotation
);
