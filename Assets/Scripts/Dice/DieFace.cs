using UnityEngine;

// One face on a die: which direction it points in local space, what value it shows,
// and how likely it is to be selected when the die rolls.
[System.Serializable]
public struct DieFace
{
    [Tooltip("Local-space face normal — the direction this face points in the die's rest orientation. " +
             "Axis-aligned for d6 (+Y, -Y, etc.). For other polyhedra, read normals from your modelling app.")]
    public Vector3 normal;

    [Tooltip("Value shown on this face and used as damage when it lands on top.")]
    public int value;

    [Tooltip("Relative probability weight. 1 = normal chance, 2 = twice as likely, 0 = never rolls.")]
    [Min(0f)]
    public float weight;
}
