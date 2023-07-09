using UnityEngine;

namespace LiveStreamQuest.Extensions;

public static class TransformExtensions
{
    public static Quaternion TransformRotation(this Transform trans, Quaternion targetRotation)
    {
        var localRotation = Quaternion.Inverse(targetRotation) * trans.rotation;
        return localRotation;
    }
}