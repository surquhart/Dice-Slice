using UnityEngine;

// Maps die face normals to pip values and computes pip-root remapping.
public static class DieFaceMapper
{
    // Standard Western die in local space: +Y=1, -Y=6, +Z=2, -Z=5, +X=3, -X=4
    private static readonly (Vector3 normal, int value)[] FaceTable =
    {
        (Vector3.up,      1),
        (Vector3.down,    6),
        (Vector3.forward, 2),
        (Vector3.back,    5),
        (Vector3.right,   3),
        (Vector3.left,    4),
    };

    // Which pip value is pointing up given the die's world rotation.
    public static int GetTopFaceValue(Quaternion dieWorldRotation)
    {
        int best = 1;
        float bestDot = float.MinValue;
        foreach (var (normal, value) in FaceTable)
        {
            float dot = Vector3.Dot(dieWorldRotation * normal, Vector3.up);
            if (dot > bestDot) { bestDot = dot; best = value; }
        }
        return best;
    }

    // Local normal direction for a given pip value.
    public static Vector3 NormalForValue(int v)
    {
        foreach (var (normal, value) in FaceTable)
            if (value == v) return normal;
        return Vector3.up;
    }

    // Returns the localRotation to apply to PipRoot so that desiredValue ends up
    // face-up after the die reaches the simulated final rotation.
    public static Quaternion PipRemapRotation(int simulatedTopValue, int desiredValue)
    {
        if (simulatedTopValue == desiredValue) return Quaternion.identity;
        Vector3 simNormal     = NormalForValue(simulatedTopValue);
        Vector3 desiredNormal = NormalForValue(desiredValue);
        // Q such that Q * desiredNormal = simNormal
        return Quaternion.FromToRotation(desiredNormal, simNormal);
    }
}
