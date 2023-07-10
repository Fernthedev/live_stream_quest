using UnityEngine;

namespace LiveStreamQuest.Extensions;

public static class TransformExtensions
{
    public static Quaternion TransformRotation(this Transform trans, Quaternion targetRotation)
    {
        Quaternion originalRotation = Quaternion.Inverse(trans.rotation) * targetRotation;
        return originalRotation;
    }

    public static (Vector3, Quaternion) LerpToWorldSpace(this Transform trans, in Vector3 vec3, in Quaternion quaternion, float delta) {
        var resultPos = Vector3.Lerp(trans.position, vec3, delta);
        var resultRot = Quaternion.Slerp(trans.rotation, quaternion, delta);

        trans.SetPositionAndRotation(resultPos, resultRot);

        return (resultPos, resultRot);
    }
    public static (Vector3, Quaternion) LerpToRelativeSpace(this Transform trans, in Vector3 vec3, in Quaternion quaternion, float delta) {
        var resultPos = Vector3.Lerp(trans.localPosition, vec3, delta);
        var resultRot = Quaternion.Slerp(trans.localRotation, quaternion, delta);

        trans.SetLocalPositionAndRotation(resultPos, resultRot);

        return (resultPos, resultRot);
    }
}