using System;
using System.Runtime.CompilerServices;
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
    private readonly SiraLog _siraLog;
    private readonly AudioTimeSyncController _audioTimeSyncController;
    private readonly SnapshotManager _snapshotManager;
    private readonly TimeDesyncFixManager _timeDesyncFixManager;
    private readonly PlayerTransforms _playerTransforms;
    private readonly MainCamera? _mainCamera;
    private readonly PauseController _pauseController;

    private Transform _properCameraTransform = null!;
    private double _lastRenderingTimelineTime = double.MinValue;

    public VRControllerManager(
        AudioTimeSyncController audioTimeSyncController,
        SiraLog siraLog, 
        SnapshotManager snapshotManager, 
        TimeDesyncFixManager timeDesyncFixManager,
        PlayerTransforms playerTransforms, 
        PlayerVRControllersManager playerVRControllersManager, 
        [Inject(Optional = true)] MainCamera? mainCamera, 
        PauseController pauseController)
    {
        _audioTimeSyncController = audioTimeSyncController;
        _siraLog = siraLog;
        _snapshotManager = snapshotManager;
        _timeDesyncFixManager = timeDesyncFixManager;
        _playerVRControllersManager = playerVRControllersManager;
        _mainCamera = mainCamera;
        _pauseController = pauseController;
        _playerTransforms = playerTransforms;
    }
    
    public void Initialize()
    {
        _playerVRControllersManager.DisableAllVRControllers();

        // TODO: Replace with a GameObject and parent so we can disable/enable the offset
        _properCameraTransform = _mainCamera != null ? _mainCamera.transform : _playerTransforms._headTransform;
    }


    public void OnNetworkPacketReceived(Protos.Transform head, Protos.Transform left, Protos.Transform right, double songTime)
    {
        // Convert incoming proto coordinates directly using our mapping methods
        var snapshot = new VRSnapshot(
            SongTime: songTime,
            HeadPosition: TransformPointProper(head.Position),
            HeadRotation: TransformRotationProper(head.Rotation),
            LeftHandPosition: TransformPointProper(left.Position),
            LeftHandRotation: TransformRotationProper(left.Rotation),
            RightHandPosition: TransformPointProper(right.Position),
            RightHandRotation: TransformRotationProper(right.Rotation)
        );

        // Pass it along off to our stateless/state-managed helper wrapper
        _snapshotManager.AddSnapshot(snapshot);
    }

    public void Tick()
    {
        if (_snapshotManager.Count < 2) return;

        // 1. Use the stabilized, desync-corrected timeline (constrained to snapshot window, newest preferred)
        var renderingTimelineTime = _snapshotManager.CalculateRenderingTimelineTime(_timeDesyncFixManager.SmoothedSongTime);

        // 2. Continuous Discontinuity Checking (Handles user rewinding map or entering practice loops)
        if (_lastRenderingTimelineTime is not double.MinValue && renderingTimelineTime < _lastRenderingTimelineTime - 0.100)
        {
            _siraLog.Info("[VRControllerManager] Discontinuity detected. Flushing state engine.");
            _snapshotManager.Clear();
            _lastRenderingTimelineTime = renderingTimelineTime;
            return;
        }
        _lastRenderingTimelineTime = renderingTimelineTime;

        // 3. Extract perfectly bounded frame slices and the blend factor from the snapshot buffer
        var (snapshotA, snapshotB, t) = _snapshotManager.GetInterpolationData(renderingTimelineTime);

        // 4. Apply the interpolated results directly onto your transformation trees
        if (_pauseController._paused != PauseController.PauseState.Paused)
        {
            LerpProper(_properCameraTransform, snapshotA.HeadPosition, snapshotB.HeadPosition, snapshotA.HeadRotation, snapshotB.HeadRotation, t);
            LerpProper(_playerTransforms._headTransform, snapshotA.HeadPosition, snapshotB.HeadPosition, snapshotA.HeadRotation, snapshotB.HeadRotation, t);
        }

        LerpProper(_playerTransforms._rightHandTransform, snapshotA.RightHandPosition, snapshotB.RightHandPosition, snapshotA.RightHandRotation, snapshotB.RightHandRotation, t);
        LerpProper(_playerTransforms._leftHandTransform, snapshotA.LeftHandPosition, snapshotB.LeftHandPosition, snapshotA.LeftHandRotation, snapshotB.LeftHandRotation, t);
        
        // 5. Clean up our memory window trailing 1.0 second behind our current timeline frame pointer
        _snapshotManager.PruneOldSnapshots(Math.Min(snapshotA.SongTime, renderingTimelineTime) - 1.0);
    }

    /// <summary>
    /// Lerps the given transform to the target position and rotation based on the current settings.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LerpProper(Transform transform, Vector3 posA, Vector3 posB, Quaternion rotA, Quaternion rotB, float t)
    {
        var blendedPos = Vector3.Lerp(posA, posB, t);
        var blendedRot = Quaternion.Slerp(rotA, rotB, t);
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

        if (!_playerTransforms._useOriginParentTransformForPseudoLocalCalculations) return vec;
        
        if (_playerTransforms._originParentTransform == null)
        {
            _siraLog.Warn("Origin parent transform is null. Returning untransformed vector.");
            return vec;
        }

        return _playerTransforms._originParentTransform.TransformPoint(vec);

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
        
        if (!_playerTransforms._useOriginParentTransformForPseudoLocalCalculations) return quat;
        
        if (_playerTransforms._originParentTransform == null)
        {
            _siraLog.Warn("Origin parent transform is null. Returning untransformed quat.");
            return quat;
        }

        return _playerTransforms._originParentTransform.TransformRotation(quat);
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



}
