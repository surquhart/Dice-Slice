using UnityEngine;

[System.Serializable]
public struct LootEntry
{
    [Tooltip("Prefab to instantiate at the entity's position on death.")]
    public GameObject prefab;

    [Tooltip("Relative probability weight. Higher = more likely to be chosen.")]
    [Min(0f)]
    public float weight;
}
