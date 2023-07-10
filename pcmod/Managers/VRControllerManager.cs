using System;
using LiveStreamQuest.Extensions;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.Managers;

public class VRControllerManager : IInitializable, ITickable
{
    [Inject] private readonly PlayerVRControllersManager _playerVRControllersManager;

    [Inject] private readonly PlayerTransforms _playerTransforms;
    [Inject] private readonly SiraLog _siraLog;

    // Set late
    private Protos.Transform? _headTransform, _rightHand, _leftHand;
    private bool updated = false;

    private UnityEngine.Vector3 _transformedHeadPosition;
    private UnityEngine.Quaternion _transformedHeadRotation;

    private UnityEngine.Vector3 _transformedLeftPosition;
    private UnityEngine.Quaternion _transformedLeftRotation;

    private UnityEngine.Vector3 _transformedRightPosition;
    private UnityEngine.Quaternion _transformedRightRotation;

    private DateTime _lastPacketTime;
    private TimeSpan _deltaPacketTime;

    public void Initialize()
    {
        _playerVRControllersManager.DisableAllVRControllers();
    }

    public void Tick()
    {
        var deltaPacketTime = (float)_deltaPacketTime.TotalSeconds;
        if (deltaPacketTime <= 0)
        {
            return;
        }
        var unityDeltaTime = Time.deltaTime;
        float deltaTime = (float)(unityDeltaTime / deltaPacketTime);


        if (_playerTransforms._useOriginParentTransformForPseudoLocalCalculations)
        {
            PseudoLocalTransform(deltaTime);
        }
        else
        {
            LocalTransformsUpdate(deltaTime);
        }
    }

    private void PseudoLocalTransform(float deltaTime)
    {
        if (updated)
        {
            _transformedHeadPosition =
                _playerTransforms._originParentTransform.TransformPoint(_headTransform?.Position.ToVector3() ?? UnityEngine.Vector3.zero);
            _transformedHeadRotation =
                _playerTransforms._originParentTransform.TransformRotation(_headTransform?.Rotation.ToQuaternion() ?? UnityEngine.Quaternion.identity);

            _transformedRightPosition =
                _playerTransforms._originParentTransform.TransformPoint(_rightHand?.Position.ToVector3() ?? UnityEngine.Vector3.zero);
            _transformedRightRotation =
                _playerTransforms._originParentTransform.TransformRotation(_rightHand?.Rotation.ToQuaternion() ?? UnityEngine.Quaternion.identity);

            _transformedLeftPosition =
                _playerTransforms._originParentTransform.TransformPoint(_leftHand?.Position.ToVector3() ?? UnityEngine.Vector3.zero);
            _transformedLeftRotation =
                _playerTransforms._originParentTransform.TransformRotation(_leftHand?.Rotation.ToQuaternion() ?? UnityEngine.Quaternion.identity);
            updated = false;
        }


        _playerTransforms._headTransform.LerpToWorldSpace(_transformedHeadPosition, _transformedHeadRotation, deltaTime);
        _playerTransforms._rightHandTransform.LerpToWorldSpace(_transformedRightPosition, _transformedRightRotation, deltaTime);
        _playerTransforms._leftHandTransform.LerpToWorldSpace(_transformedLeftPosition, _transformedLeftRotation, deltaTime);
    }

    private void LocalTransformsUpdate(float deltaTime)
    {
        if (updated)
        {
            _transformedHeadPosition = _headTransform?.Position.ToVector3() ?? UnityEngine.Vector3.zero;
            _transformedHeadRotation = _headTransform?.Rotation.ToQuaternion() ?? UnityEngine.Quaternion.identity;

            _transformedLeftPosition = _leftHand?.Position.ToVector3() ?? UnityEngine.Vector3.zero;
            _transformedLeftRotation = _leftHand?.Rotation.ToQuaternion() ?? UnityEngine.Quaternion.identity;

            _transformedRightPosition = _rightHand?.Position.ToVector3() ?? UnityEngine.Vector3.zero;
            _transformedRightRotation = _rightHand?.Rotation.ToQuaternion() ?? UnityEngine.Quaternion.identity;
            updated = false;
        }

        _playerTransforms._headTransform.LerpToRelativeSpace(_transformedHeadPosition, _transformedHeadRotation, deltaTime);
        _playerTransforms._rightHandTransform.LerpToRelativeSpace(_transformedRightPosition, _transformedRightRotation, deltaTime);
        _playerTransforms._leftHandTransform.LerpToRelativeSpace(_transformedLeftPosition, _transformedLeftRotation, deltaTime);
    }

    public void UpdateTransforms(Protos.Transform headTransform, Protos.Transform rightTransform, Protos.Transform leftTransform, Google.Protobuf.WellKnownTypes.Timestamp time)
    {
        updated = true;
        _headTransform = headTransform;
        _rightHand = rightTransform;
        _leftHand = leftTransform;

        var dateTime = time.ToDateTime();

        _deltaPacketTime = dateTime.Subtract(_lastPacketTime);

        _lastPacketTime = dateTime;
    }

}