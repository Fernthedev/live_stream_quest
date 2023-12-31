﻿using System;
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

public class VRControllerManager : IInitializable, ITickable
{
    [Inject] private readonly PlayerVRControllersManager _playerVRControllersManager;

    [Inject] private readonly PlayerTransforms _playerTransforms;
    [Inject] private readonly SiraLog _siraLog;
    [Inject] private readonly PauseController _pauseController;
    [Inject(Optional = true)] private readonly MainCamera? _mainCamera;


    // Set late
    private Protos.Transform? _headTransform, _rightHand, _leftHand;
    private bool _updated;

    private Vector3 _transformedHeadPosition;
    private Quaternion _transformedHeadRotation;

    private Vector3 _transformedLeftPosition;
    private Quaternion _transformedLeftRotation;

    private Vector3 _transformedRightPosition;
    private Quaternion _transformedRightRotation;

    // PC Time
    // e.g time between receiving packetA and packetB
    private DateTime _lastPacketTime = DateTime.MinValue;
    private TimeSpan _deltaPacketTime;

    // Movement duration is quest time 
    // e.g time elapsed between sending packetA and packetB
    private DateTime _lastMovementTime;
    private TimeSpan _movementDuration;

    private Transform _properCameraTransform = null!;

    public void Initialize()
    {
        _playerVRControllersManager.DisableAllVRControllers();

        // TODO: Replace with a GameObject and parent so we can disable/enable the offset
        _properCameraTransform = _mainCamera != null ? _mainCamera.transform : _playerTransforms._headTransform;
    }

    public void Tick()
    {
        // THIS SOMEHOW WORKS AND IT'S FLAWLESS (totally)
        
        // total time the animation took on Quest
        // basically time between packetA and packetB being **sent**
        var movementDurationTime =_movementDuration.TotalSeconds;
        if (movementDurationTime <= 0)
        {
            return;
        }

        var totalTime = _deltaPacketTime.TotalSeconds;

        // Time it took to **receive** packetA and packetB
        // subtracted by totalTime to get the time difference the network adds
        // aka get latency
        var latencyTime = Math.Abs(_deltaPacketTime.TotalSeconds - movementDurationTime);
        
        // divided by the time the animation takes
        // so this becomes a percent
        // e.g latency took 2% of the animation time away
        var latencyOffset = latencyTime / totalTime;
        
        // frame time
        var unityDeltaTime = Time.deltaTime; 
        
        // unity frame time / totalTime =>
        // percent of the animation
        var percentDeltaTime = (float)(latencyOffset + unityDeltaTime / movementDurationTime);
        
        // subtract the packet time and movement duration the unity frame time
        // since we're doing that now
        // think of this as time remaining
        _deltaPacketTime -= TimeSpan.FromSeconds(unityDeltaTime);
        _movementDuration -= TimeSpan.FromSeconds(unityDeltaTime);

        if (_updated)
        {
            ConvertProto(ref _headTransform, ref _transformedHeadPosition, ref _transformedHeadRotation);
            ConvertProto(ref _leftHand, ref _transformedLeftPosition, ref _transformedLeftRotation);
            ConvertProto(ref _rightHand, ref _transformedRightPosition, ref _transformedRightRotation);
            _updated = false;
        }


        // only move if not paused
        if (!_pauseController._paused)
        {
            LerpProper(_properCameraTransform, _transformedHeadPosition, _transformedHeadRotation, percentDeltaTime);
            LerpProper(_playerTransforms._headTransform, _transformedHeadPosition, _transformedHeadRotation,
                percentDeltaTime);
        }

        LerpProper(_playerTransforms._rightHandTransform, _transformedRightPosition, _transformedRightRotation,
            percentDeltaTime);
        LerpProper(_playerTransforms._leftHandTransform, _transformedLeftPosition, _transformedLeftRotation,
            percentDeltaTime);
    }

    public void UpdateTransforms(Protos.Transform headTransform, Protos.Transform rightTransform,
        Protos.Transform leftTransform, Timestamp newMovemenTimestamp)
    {
        _updated = true;
        _headTransform = headTransform;
        _rightHand = rightTransform;
        _leftHand = leftTransform;

        var dateTime = DateTime.Now; // time.ToDateTime();
        var movementTimestamp = newMovemenTimestamp.ToDateTime(); // time.ToDateTime();

        _deltaPacketTime = dateTime.Subtract(_lastPacketTime);
        _movementDuration = movementTimestamp.Subtract(_lastMovementTime);

        _lastPacketTime = dateTime;
        _lastMovementTime = movementTimestamp;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConvertProto(ref Protos.Transform? transform, ref Vector3 vec3, ref Quaternion quat)
    {
        if (transform == null) return;

        vec3 = TransformPointProper(transform.Position);
        quat = TransformRotationProper(transform.Rotation);

        transform = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LerpProper(Transform transform, in Vector3 pos, in Quaternion rot, float t)
    {
        if (_playerTransforms._useOriginParentTransformForPseudoLocalCalculations)
        {
            transform.LerpToWorldSpace(pos, rot, t);
        }
        else
        {
            transform.LerpToRelativeSpace(pos, rot, t);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 TransformPointProper(Protos.Vector3? protoVec)
    {
        if (protoVec == null) return Vector3.zero;


        var vec = protoVec.ToVector3();

        return _playerTransforms._useOriginParentTransformForPseudoLocalCalculations
            ? _playerTransforms._originParentTransform.TransformPoint(vec)
            : vec;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Quaternion TransformRotationProper(Protos.Quaternion? protoQuat)
    {
        if (protoQuat == null) return Quaternion.identity;


        var quat = protoQuat.ToQuaternion();

        return _playerTransforms._useOriginParentTransformForPseudoLocalCalculations
            ? _playerTransforms._originParentTransform.TransformRotation(quat)
            : quat;
    }
}