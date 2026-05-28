using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using LiveStreamQuest.Extensions;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;
using Quaternion = UnityEngine.Quaternion;
using Transform = UnityEngine.Transform;
using Vector3 = UnityEngine.Vector3;

namespace LiveStreamQuest.Managers;

// TODO: Investigate the following:
// Snapshot Interpolation with Dynamic Timeline Buffering (Glenn Fiedler / Mirror) 
// https://github.com/kookmin-sw/2026-capstone-29/blob/main/Assets/Mirror/Examples/Snapshot%20Interpolation/ClientCube.cs

// https://gafferongames.com/post/snapshot_interpolation/
// Exponential Moving Average (EMA) / Exponential Weighted Moving Average (EWMA)
// Linear Phase-Locked Loop (PLL)

// TODO: make time desync fix manager do the syncing math and here only do lerp math.

/// <summary>
/// Manages remote player VR avatar positions using Glenn Fiedler's Timeline Snapshot Interpolation framework.
///
/// This was heavily influenced by AI as I struggled to find a good resource on how to implement snapshot interpolation in a game tightly coupled to an audio timeline.
///
/// 
/// </summary>
/// <remarks>
/// <para>
/// <b>Theory of Operation:</b><br/>
/// Network transmission inherently introduces jitter, packet bunching, and Head-of-Line blocking (especially over TCP).
/// Displaying snapshots immediately upon arrival causes severe visual micro-stuttering due to varying time deltas.
/// </para>
/// <para>
/// This architecture decouples network packet delivery from the local rendering frame rate by maintaining an internal historical 
/// timeline buffer. The local game engine renders objects at a target time equal to (Authoritative Clock minus Interpolation Delay).
/// The class samples frames bounding this target window to smoothly blend tracking orientations.
/// </para>
/// <para>
/// <b>Integration with Beat Saber's Audio Architecture:</b><br/>
/// In standard multiplayer games, the rendering timeline is driven by the system clock (such as Time.time). However, inside 
/// Beat Saber, the game world is tightly coupled to the audio track. To prevent visual tracking desynchronization, this manager 
/// relies on <see cref="TimeDesyncFixManager"/>, which wraps Beat Saber's core <b>AudioTimeSyncController</b>.
/// </para>
/// <para>
/// AudioTimeSyncController provides the canonical songTime, which does not progress linearly via system uptime. Instead, it is 
/// modulated by the audio driver, DSP clock samples, game pauses, and practice mode modifiers. By utilizing 
/// <c>_timeSyncManager.SmoothedSongTime</c> as our master reference clock, the rendering timeline automatically scales 
/// when song speeds are altered (e.g., 85% slower practice speed or 150% faster map modifiers).
/// </para>
/// <para>
/// <b>Mathematical Logic:</b><br/>
/// Given two sequential network snapshots with server timestamps timeA and timeB, and a historical target rendering time 
/// renderingTimelineTime (where timeA &lt;= renderingTimelineTime &lt;= timeB), the interpolation factor 't' is explicitly computed as:
/// <c>t = (renderingTimelineTime - timeA) / (timeB - timeA)</c>
/// </para>
/// <para>
/// Spatial translation is then evaluated deterministically via linear spatial interpolation (Vector3.Lerp) and spherical quaternion 
/// linear interpolation (Quaternion.Slerp):
/// <c>RenderPosition = Lerp(PositionA, PositionB, t)</c><br/>
/// <c>RenderRotation = Slerp(RotationA, RotationB, t)</c>
/// </para>
/// <para>
/// Because 't' derives strictly from the audio-bound server-side structures rather than local frame metrics (Time.deltaTime), 
/// spatial calculations are invariant to local frame dropouts or rendering spikes.
/// </para>
/// <para>
/// <b>Primary Sources:</b><br/>
/// <list type="bullet">
/// <item><description>Fiedler, Glenn. "Snapshot Interpolation." Gaffer On Games, <a href="https://gafferongames.com/post/snapshot_interpolation/">https://gafferongames.com/post/snapshot_interpolation/</a></description></item>
/// <item><description>Mirror Networking Snapshot Interpolation Architecture: ClientCube implementation semantics</description></item>
/// <item><description>Academic & Protocol Context: Network Time Protocol (NTP) & DSP Audio Clocks RFC 5905</description></item>
/// </list>
/// </para>
/// </remarks>
public class VRControllerManager : IInitializable, ITickable
{
    private readonly PlayerVRControllersManager _playerVRControllersManager;

    private readonly PlayerTransforms _playerTransforms;
    private readonly SiraLog _siraLog;
    private readonly PauseController _pauseController;
    private readonly MainCamera? _mainCamera;
    private readonly AudioTimeSyncController _timeSyncManager;

    // circular buffers
    // TODO: Switch to sorted list
    private readonly List<VRSnapshot> _snapshots = new(12);


    private Transform _properCameraTransform = null!;

    private const float InterpolationDelay = 0.03f;
    
    // Tracks timeline continuity across frames to catch slide rewinds/seeks
    private double _lastRenderingTimelineTime = double.MinValue;

    [Inject]
    public VRControllerManager(PlayerVRControllersManager playerVRControllersManager, PlayerTransforms playerTransforms,
        SiraLog siraLog, PauseController pauseController, [Inject(Optional = true)] MainCamera? mainCamera, AudioTimeSyncController timeSyncManager)
    {
        _playerVRControllersManager = playerVRControllersManager;
        _playerTransforms = playerTransforms;
        _siraLog = siraLog;
        _pauseController = pauseController;
        _mainCamera = mainCamera;
        _timeSyncManager = timeSyncManager;
    }

    public void Initialize()
    {
        _playerVRControllersManager.DisableAllVRControllers();

        // TODO: Replace with a GameObject and parent so we can disable/enable the offset
        _properCameraTransform = _mainCamera != null ? _mainCamera.transform : _playerTransforms._headTransform;
    }

    public void Tick()
    {
        // 1. Calculate our target timeline rendering position in history
        // Convert your local system DateTime/songTime scale into seconds for straightforward math
        double renderingTimelineTime = _timeSyncManager.songTime - InterpolationDelay;
        
        // TODO: Handle rewind
        // TODO: Handle timescale
        
        // 2. Clear out tracking caches if song time rewinds or skips backwards via practice slider
        if (Math.Abs(_lastRenderingTimelineTime - double.MinValue) > 0.001 && renderingTimelineTime < _lastRenderingTimelineTime - 0.100)
        {
            _siraLog.Info("[VRControllerManager] Timeline rewind detected! Clearing snapshot buffers.");
            _snapshots.Clear();
            _lastRenderingTimelineTime = renderingTimelineTime;
            return;
        }
        
        _lastRenderingTimelineTime = renderingTimelineTime;

        // Guard baseline condition
        if (_snapshots.Count < 2)
        {
            return;
        }
        
        int targetIndex = -1;

        // Look for snapshots that are in the song time 
        for (int i = 0; i < _snapshots.Count - 1; i++)
        {
            double timeA = _snapshots[i].ServerTimeSeconds;
            double timeB = _snapshots[i + 1].ServerTimeSeconds;

            if (renderingTimelineTime >= timeA && renderingTimelineTime <= timeB)
            {
                targetIndex = i;
                break;
            }
        }
        
        // 4. Run interpolation loops based on timeline tracking availability
        if (targetIndex != -1)
        {
            var snapshotA = _snapshots[targetIndex];
            var snapshotB = _snapshots[targetIndex + 1];

            // Evaluate percentage factor strictly using internal timestamps
            float interpolationFactor = (float)((renderingTimelineTime - snapshotA.ServerTimeSeconds) / 
                                                (snapshotB.ServerTimeSeconds - snapshotA.ServerTimeSeconds));
            
            interpolationFactor = Mathf.Clamp01(interpolationFactor);

            if (_pauseController._paused != PauseController.PauseState.Paused)
            {
                LerpProper(_properCameraTransform, snapshotA.HeadPosition, snapshotB.HeadPosition, snapshotA.HeadRotation, snapshotB.HeadRotation, interpolationFactor);
                LerpProper(_playerTransforms._headTransform, snapshotA.HeadPosition, snapshotB.HeadPosition, snapshotA.HeadRotation, snapshotB.HeadRotation, interpolationFactor);
            }

            LerpProper(_playerTransforms._rightHandTransform, snapshotA.RightHandPosition, snapshotB.RightHandPosition, snapshotA.RightHandRotation, snapshotB.RightHandRotation, interpolationFactor);
            LerpProper(_playerTransforms._leftHandTransform, snapshotA.LeftHandPosition, snapshotB.LeftHandPosition, snapshotA.LeftHandRotation, snapshotB.LeftHandRotation, interpolationFactor);
        }
        else if (renderingTimelineTime > _snapshots[_snapshots.Count - 1].ServerTimeSeconds)
        {
            // Extrapolation Fallback: If network starves, hold positions at latest frame boundary
            var newest = _snapshots[_snapshots.Count - 1];
            
            if (_pauseController._paused != PauseController.PauseState.Paused)
            {
                SetTransformDirectly(_properCameraTransform, newest.HeadPosition, newest.HeadRotation);
                SetTransformDirectly(_playerTransforms._headTransform, newest.HeadPosition, newest.HeadRotation);
            }
            SetTransformDirectly(_playerTransforms._rightHandTransform, newest.RightHandPosition, newest.RightHandRotation);
            SetTransformDirectly(_playerTransforms._leftHandTransform, newest.LeftHandPosition, newest.LeftHandRotation);
        }
        
        // 5. Run standard buffer housecleaning routines
        PruneOldSnapshots(renderingTimelineTime - 1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetTransformDirectly(Transform transform, in Vector3 pos, in Quaternion rot)
    {
        if (_playerTransforms._useOriginParentTransformForPseudoLocalCalculations)
        {
            transform.position = pos;
            transform.rotation = rot;
        }
        else
        {
            transform.localPosition = pos;
            transform.localRotation = rot;
        }
    }

    public void UpdateTransforms(Protos.Transform headTransform, Protos.Transform rightTransform,
        Protos.Transform leftTransform, Timestamp serverTimestamp)
    {
        var now = Time.realtimeSinceStartupAsDouble; // time.ToDateTime();
        var serverTime = serverTimestamp.ToDateTime(); // time.ToDateTime();
        
        
        // p1 - p0
        var deltaPacketTime = 0.0;

        // t_1 - t_0
        var timeElapsedServer = TimeSpan.Zero;
        
        if (_snapshots.Count > 0)
        {
            var latest = _snapshots[_snapshots.Count - 1];
            deltaPacketTime = now - latest.PacketMoment;
            timeElapsedServer = serverTime - latest.ServerTime;
        }

        
        var newSnapshot = BuildVRSnapshot(headTransform, leftTransform, rightTransform,
            
            lastPacketMoment: now,
            deltaPacketTime: deltaPacketTime,
            serverTime: serverTime,
            timeElapsedServer: timeElapsedServer
        );
        
        _snapshots.Add(newSnapshot);
        
        // Ensure snapshots remain strictly sorted by chronological progression
        _snapshots.Sort((a, b) => a.ServerTimeSeconds.CompareTo(b.ServerTimeSeconds));
    }

    /// <summary>
    /// Lerps the given transform to the target position and rotation based on the current settings.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LerpProper(Transform transform, Vector3 posA, Vector3 posB, Quaternion rotA, Quaternion rotB, float t)
    {
        Vector3 blendedPos = Vector3.Lerp(posA, posB, t);
        Quaternion blendedRot = Quaternion.Slerp(rotA, rotB, t);
        SetTransformDirectly(transform, blendedPos, blendedRot);
    }


    /// <summary>
    /// Transforms the given protobuf Vector3 to Unity's Vector3, applying the necessary transformations based on the current settings.
    /// </summary>
    /// <param name="protoVec">The protobuf Vector3 to transform.</param>
    /// <returns>The resulting Vector3 after transformation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 TransformPointProper(Protos.Vector3? protoVec)
    {
        if (protoVec == null) return Vector3.zero;


        var vec = protoVec.ToVector3();

        return _playerTransforms._useOriginParentTransformForPseudoLocalCalculations
            ? _playerTransforms._originParentTransform.TransformPoint(vec)
            : vec;
    }

    /// <summary>
    /// Transforms the given protobuf Quaternion to Unity's Quaternion, applying the necessary transformations based on the current settings.
    /// </summary>
    /// <param name="protoQuat">The protobuf Quaternion to transform.</param>
    /// <returns>The resulting Quaternion after transformation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Quaternion TransformRotationProper(Protos.Quaternion? protoQuat)
    {
        if (protoQuat == null) return Quaternion.identity;


        var quat = protoQuat.ToQuaternion();

        return _playerTransforms._useOriginParentTransformForPseudoLocalCalculations
            ? _playerTransforms._originParentTransform.TransformRotation(quat)
            : quat;
    }
    
    private void PruneOldSnapshots(double thresholdTimeSeconds)
    {
        _snapshots.RemoveAll(snap => snap.ServerTimeSeconds < thresholdTimeSeconds);
    }

    VRSnapshot BuildVRSnapshot(Protos.Transform headTransform, Protos.Transform leftHandTransform,
        Protos.Transform rightHandTransform, double lastPacketMoment, double deltaPacketTime, DateTime serverTime, TimeSpan timeElapsedServer)
    {
        return new VRSnapshot(
            PacketMoment: lastPacketMoment,
            DeltaPacketTime: deltaPacketTime,
            ServerTime: serverTime,
            TimeElapsedServer: timeElapsedServer,
            
            HeadPosition: TransformPointProper(headTransform.Position),
            HeadRotation: TransformRotationProper(headTransform.Rotation),
            LeftHandPosition: TransformPointProper(leftHandTransform.Position),
            LeftHandRotation: TransformRotationProper(leftHandTransform.Rotation),
            RightHandPosition: TransformPointProper(rightHandTransform.Position),
            RightHandRotation: TransformRotationProper(rightHandTransform.Rotation)
        );
    }
}

public readonly record struct VRSnapshot(
// PC Time
// e.g time between receiving packetA and packetB
    /// <summary>
    /// Moment of time when packet time was last received
    /// </summary>
    double PacketMoment,
    /// <summary>
    /// Time difference between p_1 and p_0
    /// </summary>
    double DeltaPacketTime,

// e.g song time elapsed between sending packetA and packetB
// reported by the server
    /// <summary>
    /// Song time last reported by server
    /// </summary>
    DateTime ServerTime,
    /// <summary>
    /// Time difference between time reported by server t_1 and time reported by server t_0
    /// </summary>
    TimeSpan TimeElapsedServer,

    Vector3 HeadPosition,
    Quaternion HeadRotation,
    Vector3 LeftHandPosition,
    Quaternion LeftHandRotation,
    Vector3 RightHandPosition,
    Quaternion RightHandRotation
)
{
    /// <summary>
    /// Evaluates linear double comparison values from the native datetime stamp.
    /// Converts time of day signatures to simple continuous numeric values for timeline sorting and fraction rendering.
    /// </summary>
    public double ServerTimeSeconds => ServerTime.TimeOfDay.TotalSeconds;
}