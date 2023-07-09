using LiveStreamQuest.Extensions;
using LiveStreamQuest.Protos;
using Zenject;

namespace LiveStreamQuest.Managers;

public class VRControllerManager : IInitializable
{
    [Inject] private readonly PlayerVRControllersManager _playerVRControllersManager;

    [Inject] private readonly PlayerTransforms _playerTransforms;

    public void Initialize()
    {
        _playerVRControllersManager.DisableAllVRControllers();
    }

    public void UpdateTransforms(Protos.Transform headTransform, Transform rightTransform, Transform leftTransform)
    {
        var transformedHeadPosition =
            _playerTransforms._originParentTransform.TransformPoint(headTransform.Position.ToVector3());
        var transformedHeadRotation =
            _playerTransforms._originParentTransform.TransformRotation(headTransform.Rotation.ToQuaternion());

        var transformedRightPosition =
            _playerTransforms._originParentTransform.TransformPoint(rightTransform.Position.ToVector3());
        var transformedRightRotation =
            _playerTransforms._originParentTransform.TransformRotation(rightTransform.Rotation.ToQuaternion());

        var transformedLeftPosition =
            _playerTransforms._originParentTransform.TransformPoint(leftTransform.Position.ToVector3());
        var transformedLeftRotation =
            _playerTransforms._originParentTransform.TransformRotation(leftTransform.Rotation.ToQuaternion());

        _playerTransforms._headTransform.position = transformedHeadPosition;
        _playerTransforms._headTransform.rotation = transformedHeadRotation;

        _playerTransforms._rightHandTransform.position = transformedRightPosition;
        _playerTransforms._rightHandTransform.rotation = transformedRightRotation;

        _playerTransforms._leftHandTransform.position = transformedLeftPosition;
        _playerTransforms._leftHandTransform.rotation = transformedLeftRotation;
    }
}