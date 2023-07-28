using System;
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

    private DateTime _lastPacketTime;
    private TimeSpan _deltaPacketTime;

    private Transform _properCameraTransform = null!;

    public void Initialize()
    {
        _playerVRControllersManager.DisableAllVRControllers();
        
        // TODO: Replace with a GameObject and parent so we can disable/enable the offset
        _properCameraTransform = _mainCamera != null ? _mainCamera.transform : _playerTransforms._headTransform;
    }

    public void Tick()
    {
        var deltaPacketTime = (float)_deltaPacketTime.TotalSeconds;
        if (deltaPacketTime <= 0)
        {
            return;
        }

        var unityDeltaTime = Time.deltaTime;
        var deltaTime = unityDeltaTime / deltaPacketTime;
        // TODO: Needed? Add or Sub?
        // _deltaPacketTime = _deltaPacketTime.Add(TimeSpan.FromSeconds(unityDeltaTime));

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
            LerpProper(_properCameraTransform, _transformedHeadPosition, _transformedHeadRotation, deltaTime);
            LerpProper(_playerTransforms._headTransform, _transformedHeadPosition, _transformedHeadRotation,
                deltaTime);
        }

        LerpProper(_playerTransforms._rightHandTransform, _transformedRightPosition, _transformedRightRotation,
            deltaTime);
        LerpProper(_playerTransforms._leftHandTransform, _transformedLeftPosition, _transformedLeftRotation,
            deltaTime);
    }
    
    public void UpdateTransforms(Protos.Transform headTransform, Protos.Transform rightTransform,
        Protos.Transform leftTransform, Timestamp time)
    {
        _updated = true;
        _headTransform = headTransform;
        _rightHand = rightTransform;
        _leftHand = leftTransform;

        var dateTime = time.ToDateTime();

        _deltaPacketTime = dateTime.Subtract(_lastPacketTime);

        _lastPacketTime = dateTime;
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