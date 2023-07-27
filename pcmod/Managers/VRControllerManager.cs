using System;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using LiveStreamQuest.Extensions;
using SiraUtil.Logging;
using SiraUtil.Tools.FPFC;
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
    private bool updated;

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
                _playerTransforms._originParentTransform.TransformPoint(_headTransform?.Position.ToVector3() ??
                                                                        Vector3.zero);
            _transformedHeadRotation =
                _playerTransforms._originParentTransform.TransformRotation(_headTransform?.Rotation.ToQuaternion() ??
                                                                           Quaternion.identity);

            _transformedRightPosition =
                _playerTransforms._originParentTransform.TransformPoint(_rightHand?.Position.ToVector3() ??
                                                                        Vector3.zero);
            _transformedRightRotation =
                _playerTransforms._originParentTransform.TransformRotation(_rightHand?.Rotation.ToQuaternion() ??
                                                                           Quaternion.identity);

            _transformedLeftPosition =
                _playerTransforms._originParentTransform.TransformPoint(_leftHand?.Position.ToVector3() ??
                                                                        Vector3.zero);
            _transformedLeftRotation =
                _playerTransforms._originParentTransform.TransformRotation(_leftHand?.Rotation.ToQuaternion() ??
                                                                           Quaternion.identity);
            updated = false;
        }

        if (!_pauseController._paused)
        {
            _properCameraTransform.LerpToWorldSpace(_transformedHeadPosition, _transformedHeadRotation, deltaTime);
            // var headResult =
            //     _playerTransforms._headTransform.LerpToWorldSpace(_transformedHeadPosition, _transformedHeadRotation,
            //         deltaTime);
            //
            // if (_properCameraTransform != null)
            // {
            //     _properCameraTransform.SetPositionAndRotation(headResult.Item1, headResult.Item2);
            // }
        }

        _playerTransforms._rightHandTransform.LerpToWorldSpace(_transformedRightPosition, _transformedRightRotation,
            deltaTime);
        _playerTransforms._leftHandTransform.LerpToWorldSpace(_transformedLeftPosition, _transformedLeftRotation,
            deltaTime);
    }

    private void LocalTransformsUpdate(float deltaTime)
    {
        if (updated)
        {
            _transformedHeadPosition = _headTransform?.Position.ToVector3() ?? Vector3.zero;
            _transformedHeadRotation = _headTransform?.Rotation.ToQuaternion() ?? Quaternion.identity;

            _transformedLeftPosition = _leftHand?.Position.ToVector3() ?? Vector3.zero;
            _transformedLeftRotation = _leftHand?.Rotation.ToQuaternion() ?? Quaternion.identity;

            _transformedRightPosition = _rightHand?.Position.ToVector3() ?? Vector3.zero;
            _transformedRightRotation = _rightHand?.Rotation.ToQuaternion() ?? Quaternion.identity;
            updated = false;
        }

        // only move if not paused
        if (!_pauseController._paused)
        {
            _properCameraTransform.LerpToRelativeSpace(_transformedHeadPosition, _transformedHeadRotation, deltaTime);
        }

        _playerTransforms._rightHandTransform.LerpToRelativeSpace(_transformedRightPosition, _transformedRightRotation,
            deltaTime);
        _playerTransforms._leftHandTransform.LerpToRelativeSpace(_transformedLeftPosition, _transformedLeftRotation,
            deltaTime);
    }

    public void UpdateTransforms(Protos.Transform headTransform, Protos.Transform rightTransform,
        Protos.Transform leftTransform, Timestamp time)
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