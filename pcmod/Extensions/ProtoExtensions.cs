using UnityEngine;

namespace LiveStreamQuest.Extensions;

public static class ProtoExtensions
{
    public static UnityEngine.Vector3 ToVector3(this Protos.Vector3 ov)
    {
        return new Vector3(ov.X, ov.Y, ov.Z);
    } 
    public static UnityEngine.Quaternion ToQuaternion(this Protos.Quaternion ov)
    {
        return new Quaternion(ov.X, ov.Y, ov.Z, ov.W);
    }
}