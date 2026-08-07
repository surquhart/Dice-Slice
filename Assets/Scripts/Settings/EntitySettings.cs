using UnityEngine;

[CreateAssetMenu(fileName = "EntitySettings", menuName = "Dice Slice/Entity Settings")]
public class EntitySettings : ScriptableObject
{
    [Header("Push-Out")]
    [Tooltip("Speed at which the player is pushed out of an entity's space (units/second).")]
    public float pushOutSpeed = 10f;

    [Tooltip("How much larger the auto-generated trigger collider is than the solid collider on each axis.")]
    public float pushOutColliderExpand = 0.05f;

    [Header("Knockback")]
    [Tooltip("Distance the entity travels when knocked back by a dash.")]
    public float knockbackDistance = 1.5f;

    [Tooltip("Duration of the knockback movement in seconds.")]
    public float knockbackDuration = 0.12f;

    [Tooltip("Maximum angular jitter applied to the knockback direction in degrees.")]
    public float knockbackJitter = 12f;

    [Header("HP Bar")]
    [Tooltip("How far toward the camera (–Z) the HP bar is offset from the entity's position.")]
    public float hpBarZOffset = 0.7f;

    [Tooltip("Base width of the HP bar when the entity's transform scale is 1. Scales proportionally with entity size.")]
    public float hpBarBaseWidth = 0.9f;

    [Tooltip("Height (depth on the floor plane) of the HP bar as a fraction of its width.")]
    public float hpBarAspectRatio = 0.14f;
}
