using LiveStreamQuest.Managers;
using UnityEngine;

namespace LiveStreamQuest.Tests;

/// <summary>
/// Comprehensive unit tests for SnapshotManager ensuring snapshot buffering, 
/// interpolation window selection, and desync correction behave correctly.
/// </summary>
[TestFixture]
public class SnapshotManagerTests
{
    private SnapshotManager _manager = null!;

    [SetUp]
    public void Setup()
    {
        _manager = new SnapshotManager();
    }

    #region AddSnapshot & Count Tests

    [Test]
    public void AddSnapshot_SingleSnapshot_CountShouldBeOne()
    {
        var snapshot = CreateTestSnapshot(songTime: 1.0);
        _manager.AddSnapshot(snapshot);
        
        Assert.That(_manager.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddSnapshot_MultipleSnapshots_CountShouldReflectTotal()
    {
        for (int i = 0; i < 5; i++)
        {
            _manager.AddSnapshot(CreateTestSnapshot(songTime: i * 0.5));
        }
        
        Assert.That(_manager.Count, Is.EqualTo(5));
    }

    [Test]
    public void AddSnapshot_OutOfOrderSnapshots_ShouldSortByTime()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 3.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        
        // Verify sorted order via FindBestSnapshotWindow
        var (a, b) = _manager.FindBestSnapshotWindow(1.5);
        Assert.That(a.SongTime, Is.LessThan(b.SongTime));
    }

    #endregion

    #region FindBestSnapshotWindow Tests

    [Test]
    public void FindBestSnapshotWindow_TwoSnapshots_ExactTargetTime_ReturnsCorrectPair()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        
        var (a, b) = _manager.FindBestSnapshotWindow(1.5);
        
        Assert.That(a.SongTime, Is.EqualTo(1.0));
        Assert.That(b.SongTime, Is.EqualTo(2.0));
    }

    [Test]
    public void FindBestSnapshotWindow_TargetBeforeAllSnapshots_ReturnsMostEarly()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 3.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 4.0));
        
        var (a, b) = _manager.FindBestSnapshotWindow(0.5);
        
        Assert.That(a.SongTime, Is.EqualTo(2.0));
        Assert.That(b.SongTime, Is.EqualTo(3.0));
    }

    [Test]
    public void FindBestSnapshotWindow_TargetAfterAllSnapshots_ReturnsMostLate()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 3.0));
        
        var (a, b) = _manager.FindBestSnapshotWindow(5.0);
        
        Assert.That(a.SongTime, Is.EqualTo(2.0));
        Assert.That(b.SongTime, Is.EqualTo(3.0));
    }

    [Test]
    public void FindBestSnapshotWindow_LargeGapBetweenSnapshots_StillFindsBestMatch()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 10.0)); // 9 second gap
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 11.0));
        
        var (a, b) = _manager.FindBestSnapshotWindow(5.5);
        
        Assert.That(a.SongTime, Is.EqualTo(1.0));
        Assert.That(b.SongTime, Is.EqualTo(10.0));
    }

    [Test]
    public void FindBestSnapshotWindow_MultipleSnapshots_CorrectInterpolationBounds()
    {
        for (double t = 0.0; t <= 5.0; t += 1.0)
        {
            _manager.AddSnapshot(CreateTestSnapshot(songTime: t));
        }
        
        // Test multiple points within the buffer
        var (a1, b1) = _manager.FindBestSnapshotWindow(1.5);
        Assert.That(a1.SongTime, Is.EqualTo(1.0));
        Assert.That(b1.SongTime, Is.EqualTo(2.0));
        
        var (a2, b2) = _manager.FindBestSnapshotWindow(3.8);
        Assert.That(a2.SongTime, Is.EqualTo(3.0));
        Assert.That(b2.SongTime, Is.EqualTo(4.0));
    }

    [Test]
    public void FindBestSnapshotWindow_InsufficientSnapshots_ReturnsDummyOrEdgeCaseHandled()
    {
        // With 0 snapshots, should return dummy
        var (a0, b0) = _manager.FindBestSnapshotWindow(1.0);
        Assert.That(a0, Is.EqualTo(default(VRSnapshot)));
        Assert.That(b0, Is.EqualTo(default(VRSnapshot)));
        
        // With 1 snapshot, should return the same snapshot twice
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        var (a1, b1) = _manager.FindBestSnapshotWindow(0.5);
        Assert.That(a1.SongTime, Is.EqualTo(b1.SongTime));
        Assert.That(a1.SongTime, Is.EqualTo(1.0));
    }

    #endregion

    #region GetInterpolationData Tests

    [Test]
    public void GetInterpolationData_BoundedTarget_ReturnsCorrectInterpolationFactor()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 0.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        
        var (a, b, t) = _manager.GetInterpolationData(0.5);
        
        Assert.That(t, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(a.SongTime, Is.EqualTo(0.0));
        Assert.That(b.SongTime, Is.EqualTo(1.0));
    }

    [Test]
    public void GetInterpolationData_BeginningOfWindow_ReturnsTZeroish()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 0.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        
        var (a, b, t) = _manager.GetInterpolationData(0.0);
        
        Assert.That(t, Is.EqualTo(0.0f).Within(0.001f));
    }

    [Test]
    public void GetInterpolationData_EndOfWindow_ReturnsTOne()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 0.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        
        var (a, b, t) = _manager.GetInterpolationData(1.0);
        
        Assert.That(t, Is.EqualTo(1.0f).Within(0.001f));
    }

    [Test]
    public void GetInterpolationData_IdenticalSnapshotTimes_ReturnsTOne()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        
        var (a, b, t) = _manager.GetInterpolationData(1.0);
        
        // Should return 1.0 when duration is effectively zero
        Assert.That(t, Is.EqualTo(1.0f));
    }

    [Test]
    public void GetInterpolationData_TOutOfBounds_ClampsTo01()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        
        var (_, __, tUnder) = _manager.GetInterpolationData(0.5);
        Assert.That(tUnder, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(tUnder, Is.LessThanOrEqualTo(1.0f));
        
        var (_, ___, tOver) = _manager.GetInterpolationData(3.0);
        Assert.That(tOver, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(tOver, Is.LessThanOrEqualTo(1.0f));
    }

    #endregion

    #region CalculateRenderingTimelineTime Tests

    [Test]
    public void CalculateRenderingTimelineTime_InsufficientSnapshots_ReturnsCurrentTime()
    {
        double result = _manager.CalculateRenderingTimelineTime(5.0f);
        
        Assert.That(result, Is.EqualTo(5.0).Within(0.001));
    }

    [Test]
    public void CalculateRenderingTimelineTime_CurrentTimeWithinWindow_AddsInterpolationDelay()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 4.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 5.0));
        
        double result = _manager.CalculateRenderingTimelineTime(5.0f);
        
        // Should be less than current time (pushed into past by delay)
        Assert.That(result, Is.LessThan(5.0));
        Assert.That(result, Is.GreaterThan(4.0));
    }

    [Test]
    public void CalculateRenderingTimelineTime_MinInterpolationDelay_NotLessThanMin()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 5.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 5.05));
        
        double result = _manager.CalculateRenderingTimelineTime(5.05f);
        
        // Gap is 0.05, so delay should be clamped to minimum (0.03)
        Assert.That(5.05 - result, Is.GreaterThanOrEqualTo(0.03));
    }

    #endregion

    #region PruneOldSnapshots Tests

    [Test]
    public void PruneOldSnapshots_RemovesSnapshotsBelowThreshold()
    {
        for (double t = 0; t <= 5.0; t += 1.0)
        {
            _manager.AddSnapshot(CreateTestSnapshot(songTime: t));
        }
        
        _manager.PruneOldSnapshots(2.5);
        
        // Should have removed: 0.0, 1.0, 2.0 (all < 2.5)
        // Should keep: 3.0, 4.0, 5.0
        Assert.That(_manager.Count, Is.EqualTo(3));
    }

    [Test]
    public void PruneOldSnapshots_PruneAllSnapshots_ResultsInEmptyBuffer()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        
        _manager.PruneOldSnapshots(10.0);
        
        Assert.That(_manager.Count, Is.EqualTo(0));
    }

    [Test]
    public void PruneOldSnapshots_NoSnapshotsMeetThreshold_BufferUnchanged()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 5.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 6.0));
        
        int countBefore = _manager.Count;
        _manager.PruneOldSnapshots(1.0);
        
        Assert.That(_manager.Count, Is.EqualTo(countBefore));
    }

    #endregion

    #region ConstrainTimeToSnapshotWindow Tests

    [Test]
    public void ConstrainTimeToSnapshotWindow_InsufficientSnapshots_ReturnsDesiredTime()
    {
        double result = _manager.ConstrainTimeToSnapshotWindow(5.0);
        
        Assert.That(result, Is.EqualTo(5.0));
    }

    [Test]
    public void ConstrainTimeToSnapshotWindow_TimeBeforeWindow_ReturnsOldestSnapshot()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 3.0));
        
        double result = _manager.ConstrainTimeToSnapshotWindow(1.0);
        
        Assert.That(result, Is.EqualTo(2.0));
    }

    [Test]
    public void ConstrainTimeToSnapshotWindow_TimeAfterWindow_ReturnsNewestSnapshot()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 3.0));
        
        double result = _manager.ConstrainTimeToSnapshotWindow(5.0);
        
        Assert.That(result, Is.EqualTo(3.0), "Should prefer newest snapshot when time is beyond buffer");
    }

    [Test]
    public void ConstrainTimeToSnapshotWindow_TimeWithinWindow_ReturnsTimeDirect()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 5.0));
        
        double result = _manager.ConstrainTimeToSnapshotWindow(3.0);
        
        Assert.That(result, Is.EqualTo(3.0));
    }

    #endregion

    #region Clear Tests

    [Test]
    public void Clear_RemovesAllSnapshots()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 3.0));
        
        _manager.Clear();
        
        Assert.That(_manager.Count, Is.EqualTo(0));
    }

    #endregion

    #region Stress & Edge Cases

    [Test]
    public void StressTest_ManySnapshots_RemainsStable()
    {
        const int snapshotCount = 1000;
        for (int i = 0; i < snapshotCount; i++)
        {
            _manager.AddSnapshot(CreateTestSnapshot(songTime: i * 0.016)); // 16ms intervals
        }
        
        Assert.That(_manager.Count, Is.EqualTo(snapshotCount));
        
        // Should still find windows efficiently
        var (a, b) = _manager.FindBestSnapshotWindow(8.0);
        Assert.That(a.SongTime, Is.LessThanOrEqualTo(8.0));
        Assert.That(b.SongTime, Is.GreaterThanOrEqualTo(8.0));
    }

    [Test]
    public void DuplicateTimeSnapshots_HandledCorrectly()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 2.0));
        
        // Should still have all 3
        Assert.That(_manager.Count, Is.EqualTo(3));
        
        // Window finding should work despite duplicates
        var (a, b) = _manager.FindBestSnapshotWindow(1.0);
        Assert.That(a.SongTime, Is.EqualTo(1.0));
    }

    [Test]
    public void ReversedTimeline_HandlesProperly()
    {
        // Simulate user scrubbing backwards in the timeline
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 5.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 4.0)); // Goes backward
        
        // Should still be sorted
        var (a, b) = _manager.FindBestSnapshotWindow(4.5);
        Assert.That(a.SongTime, Is.EqualTo(4.0));
        Assert.That(b.SongTime, Is.EqualTo(5.0));
    }

    [Test]
    public void VerySmallTimes_PrecisionHandledCorrectly()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 0.001));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 0.002));
        
        var (a, b, t) = _manager.GetInterpolationData(0.0015);
        
        Assert.That(t, Is.GreaterThanOrEqualTo(0.0f));
        Assert.That(t, Is.LessThanOrEqualTo(1.0f));
    }

    [Test]
    public void VeryLargeTimes_PrecisionHandledCorrectly()
    {
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1000.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1001.0));
        
        var (a, b, t) = _manager.GetInterpolationData(1000.5);
        
        Assert.That(t, Is.EqualTo(0.5f).Within(0.001f));
    }

    #endregion

    #region Integration Tests: Time Progression & Interpolation Correctness

    [Test]
    public void TimeProgression_TFactorIncreasesMonotonically()
    {
        // Create snapshots at 0s and 1s
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 0.0));
        _manager.AddSnapshot(CreateTestSnapshot(songTime: 1.0));
        
        // Sample t at progressive time points
        var prevT = 0.0f;
        for (double time = 0.0; time <= 1.0; time += 0.1)
        {
            var (_, __, t) = _manager.GetInterpolationData(time);
            
            // t should increase monotonically
            Assert.That(t, Is.GreaterThanOrEqualTo(prevT), 
                $"Interpolation factor should increase at time {time}");
            prevT = t;
        }
        
        // Final t must be >= 1.0 (or clamped to 1.0)
        Assert.That(prevT, Is.GreaterThanOrEqualTo(0.9f));
    }

    [Test]
    public void TimeProgression_CorrectSnapshotPairs()
    {
        // Setup: 3 snapshots at 0s, 1s, 2s
        var snapA = CreateTestSnapshot(songTime: 0.0);
        var snapB = CreateTestSnapshot(songTime: 1.0);
        var snapC = CreateTestSnapshot(songTime: 2.0);
        
        _manager.AddSnapshot(snapA);
        _manager.AddSnapshot(snapB);
        _manager.AddSnapshot(snapC);
        
        // At 0.5s: should use (snapA, snapB)
        var (a1, b1) = _manager.FindBestSnapshotWindow(0.5);
        Assert.That(a1.SongTime, Is.EqualTo(0.0));
        Assert.That(b1.SongTime, Is.EqualTo(1.0));
        
        // At 1.5s: should use (snapB, snapC)
        var (a2, b2) = _manager.FindBestSnapshotWindow(1.5);
        Assert.That(a2.SongTime, Is.EqualTo(1.0));
        Assert.That(b2.SongTime, Is.EqualTo(2.0));
    }

    [Test]
    public void SmoothMotion_LinearInterpolationAccuracy()
    {
        // Create two snapshots with known positions
        var snap0 = new VRSnapshot(
            SongTime: 0.0,
            HeadPosition: new Vector3(0, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        );
        
        var snap1 = new VRSnapshot(
            SongTime: 1.0,
            HeadPosition: new Vector3(10, 0, 0), // Move 10 units in X
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        );
        
        _manager.AddSnapshot(snap0);
        _manager.AddSnapshot(snap1);
        
        // Sample interpolation at midpoint
        var (a, b, t) = _manager.GetInterpolationData(0.5);
        
        // At t=0.5, the position should be lerped to (5, 0, 0)
        var lerpedPos = Vector3.Lerp(a.HeadPosition, b.HeadPosition, t);
        
        Assert.That(t, Is.EqualTo(0.5f).Within(0.01f), "t factor at midpoint should be 0.5");
        Assert.That(lerpedPos.x, Is.EqualTo(5.0f).Within(0.01f), "X position should be halfway");
        Assert.That(lerpedPos, Is.EqualTo(new Vector3(5, 0, 0)).Using(Vector3Comparer));
    }

    [Test]
    public void RealWorldScenario_SimulatePlayerMotion()
    {
        // Simulate a player moving from (0,0,0) to (10,0,0) over 2 seconds
        // via 4 network packets
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 0.0,
            HeadPosition: new Vector3(0, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 0.5,
            HeadPosition: new Vector3(2.5f, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 1.0,
            HeadPosition: new Vector3(5, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 2.0,
            HeadPosition: new Vector3(10, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        // Sample at various times and verify smooth progression
        var times = new[] { 0.0, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0 };
        var prevX = 0.0f;
        
        foreach (var time in times)
        {
            var (a, b, t) = _manager.GetInterpolationData(time);
            var pos = Vector3.Lerp(a.HeadPosition, b.HeadPosition, t);
            
            // X position should increase monotonically
            Assert.That(pos.x, Is.GreaterThanOrEqualTo(prevX), 
                $"Position should not decrease going forward at t={time}");
            prevX = pos.x;
        }
        
        // Final position should be close to (10, 0, 0)
        Assert.That(prevX, Is.EqualTo(10.0f).Within(0.1f));
    }

    [Test]
    public void InterpolationBoundary_ExactSnapshotTime()
    {
        // When rendering time exactly equals a snapshot time,
        // ensure correct interpolation behavior
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 1.0,
            HeadPosition: new Vector3(1, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 2.0,
            HeadPosition: new Vector3(2, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        // At exact snapshot time 1.0
        var (a1, b1, t1) = _manager.GetInterpolationData(1.0);
        Assert.That(t1, Is.EqualTo(0.0f).Within(0.001f));
        var pos1 = Vector3.Lerp(a1.HeadPosition, b1.HeadPosition, t1);
        Assert.That(pos1.x, Is.EqualTo(1.0f).Within(0.01f));
        
        // At exact snapshot time 2.0
        var (a2, b2, t2) = _manager.GetInterpolationData(2.0);
        Assert.That(t2, Is.EqualTo(1.0f).Within(0.001f));
        var pos2 = Vector3.Lerp(a2.HeadPosition, b2.HeadPosition, t2);
        Assert.That(pos2.x, Is.EqualTo(2.0f).Within(0.01f));
    }

    [Test]
    public void NetworkDelay_SimulatePacketArrival()
    {
        // Simulate packets arriving with delay: packets sent at 0s, 0.5s, 1s
        // but arrive at slightly different times
        
        // "Packet 1" with data from 0s
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 0.0,
            HeadPosition: new Vector3(0, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        // "Packet 2" some time later (representing ~50ms network delay)
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 0.5,
            HeadPosition: new Vector3(5, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        // "Packet 3"
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 1.0,
            HeadPosition: new Vector3(10, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        // At interpolation time 0.75s:
        // Should smoothly interpolate between packet 2 (0.5s) and packet 3 (1.0s)
        var (a, b, t) = _manager.GetInterpolationData(0.75);
        
        Assert.That(a.SongTime, Is.EqualTo(0.5));
        Assert.That(b.SongTime, Is.EqualTo(1.0));
        Assert.That(t, Is.EqualTo(0.5f).Within(0.01f), "At 0.75s, should be halfway between 0.5s and 1.0s");
        
        var pos = Vector3.Lerp(a.HeadPosition, b.HeadPosition, t);
        Assert.That(pos.x, Is.EqualTo(7.5f).Within(0.1f), "Position should be 7.5 (halfway between 5 and 10)");
    }

    [Test]
    public void Rotation_CorrectSnapshotsSelectedForInterpolation()
    {
        // Test that the correct snapshots containing rotation data are selected
        var snap0 = new VRSnapshot(
            SongTime: 0.0,
            HeadPosition: Vector3.zero,
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        );
        
        var snap1 = new VRSnapshot(
            SongTime: 1.0,
            HeadPosition: Vector3.zero,
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        );
        
        _manager.AddSnapshot(snap0);
        _manager.AddSnapshot(snap1);
        
        // At 0.5s, should select snap0 and snap1 for interpolation
        var (a, b, t) = _manager.GetInterpolationData(0.5);
        
        // Verify correct snapshots are selected
        Assert.That(a.SongTime, Is.EqualTo(0.0), "First snapshot should be from time 0.0");
        Assert.That(b.SongTime, Is.EqualTo(1.0), "Second snapshot should be from time 1.0");
        Assert.That(t, Is.EqualTo(0.5f).Within(0.01f), "Interpolation factor should be 0.5 at midpoint");
        
        // Rotations should exist and be ready for interpolation
        Assert.That(a.HeadRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(b.HeadRotation, Is.EqualTo(Quaternion.identity));
    }

    [Test]
    public void TimeContinuity_NoJumpsWhenAdvancingTime()
    {
        // Verify that there are no position "jumps" when advancing time continuously
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 0.0,
            HeadPosition: new Vector3(0, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 1.0,
            HeadPosition: new Vector3(10, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 2.0,
            HeadPosition: new Vector3(15, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        // Simulate continuous time advancement
        Vector3 lastPos = Vector3.zero;
        const float timeStep = 0.05f;
        
        for (double t = 0.0; t <= 2.0; t += timeStep)
        {
            var (a, b, tFactor) = _manager.GetInterpolationData(t);
            var pos = Vector3.Lerp(a.HeadPosition, b.HeadPosition, tFactor);
            
            // Distance between consecutive positions should be reasonable (no jumps)
            float distance = Vector3.Distance(pos, lastPos);
            float maxAllowedDistance = 10f * timeStep + 0.1f; // Linear max + tolerance
            
            Assert.That(distance, Is.LessThanOrEqualTo(maxAllowedDistance),
                $"Position jump detected at time {t}: distance={distance}");
            
            lastPos = pos;
        }
    }

    [Test]
    public void ReversePlayback_TimeGoingBackward()
    {
        // When timeline rewinds (e.g., seeking), positions should still interpolate correctly
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 0.0,
            HeadPosition: new Vector3(0, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        _manager.AddSnapshot(new VRSnapshot(
            SongTime: 2.0,
            HeadPosition: new Vector3(20, 0, 0),
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        ));
        
        // Forward at 1.0s: should be at 10
        var (aFwd, bFwd, tFwd) = _manager.GetInterpolationData(1.0);
        var posFwd = Vector3.Lerp(aFwd.HeadPosition, bFwd.HeadPosition, tFwd);
        Assert.That(posFwd.x, Is.EqualTo(10.0f).Within(0.1f));
        
        // Backward at 0.5s: should be at 5
        var (aBwd, bBwd, tBwd) = _manager.GetInterpolationData(0.5);
        var posBwd = Vector3.Lerp(aBwd.HeadPosition, bBwd.HeadPosition, tBwd);
        Assert.That(posBwd.x, Is.EqualTo(5.0f).Within(0.1f));
    }

    #endregion

    #region Comparison Helpers

    /// <summary>
    /// Custom comparer for Vector3 equality within tolerance
    /// </summary>
    private static IEqualityComparer<Vector3> Vector3Comparer => new Vector3EqualityComparer();

    private class Vector3EqualityComparer : IEqualityComparer<Vector3>
    {
        private const float Tolerance = 0.01f;

        public bool Equals(Vector3 x, Vector3 y)
        {
            return Vector3.Distance(x, y) < Tolerance;
        }

        public int GetHashCode(Vector3 obj)
        {
            return obj.GetHashCode();
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Factory method to create test snapshots with configurable song time.
    /// </summary>
    private static VRSnapshot CreateTestSnapshot(double songTime)
    {
        return new VRSnapshot(
            SongTime: songTime,
            HeadPosition: Vector3.zero,
            HeadRotation: Quaternion.identity,
            LeftHandPosition: Vector3.zero,
            LeftHandRotation: Quaternion.identity,
            RightHandPosition: Vector3.zero,
            RightHandRotation: Quaternion.identity
        );
    }

    #endregion
}




