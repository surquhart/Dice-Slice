using UnityEngine;

public class Enemy : DamageableEntity
{
    [Header("Enemy")]
    [Tooltip("Optional ScriptableObject holding display name, lore, and art for this enemy type.")]
    [SerializeField] ScriptableObject _compendiumData;
}
