using UnityEngine;

[CreateAssetMenu(fileName = "DiceSettings", menuName = "Dice Slice/Dice Settings")]
public class DiceSettings : ScriptableObject
{
    [Header("Die Size")]
    [Tooltip("Die edge length in world units. Scales the visual mesh, physics collider, and simulation.")]
    public float dieSize = 1f;

    [Header("Roll Plane")]
    [Tooltip("Y world position of the floor surface where dice come to rest. Drives Room floor placement, die spawn heights, and room bounds.")]
    public float rollHeight = 0.5f;
    [Tooltip("±randomisation applied to die spawn height per throw, so consecutive throws don't arc identically.")]
    public float rollHeightJitter = 0.1f;

    [Header("Launch")]
    [Tooltip("Minimum horizontal launch speed. Actual speed scales up automatically with throw distance.")]
    public float launchSpeed = 6f;
    [Tooltip("How much further than the exact minimum the die travels (1.0 = just off-screen, 1.5 = comfortable margin)")]
    public float launchSpeedMargin = 1.5f;
    [Tooltip("Hard cap on computed launch speed")]
    public float launchSpeedMax = 22f;
    [Tooltip("Upward-to-horizontal speed ratio for short (near camera) throws")]
    public float launchLoftNear = 0.15f;
    [Tooltip("Upward-to-horizontal speed ratio for long (far from camera) throws")]
    public float launchLoftFar  = 0.55f;
    [Tooltip("Extra Y added to spawn position for short-range throws so the die arcs down to nearby targets")]
    public float launchHeightBoostNear = 1f;

    [Header("Simulation")]
    [Tooltip("Max attempts to find a trajectory that stays in-bounds")]
    public int   maxSimAttempts  = 20;
    [Tooltip("Die half-extent inset from walls when checking trajectory bounds")]
    public float wallInsetMargin = 0.6f;
    [Tooltip("Max physics steps per simulation attempt before giving up")]
    public int   maxSimSteps     = 600;
    [Tooltip("Max linear speed (units/s) below which a die is considered stopped for settle detection.")]
    public float settleSpeedThreshold   = 0.05f;
    [Tooltip("Max angular speed (rad/s) below which a die is considered stopped for settle detection.")]
    public float settleAngularThreshold = 0.1f;
    [Tooltip("Max degrees of tilt from flat for a die face to be considered horizontal. Prevents a die balanced on an edge from being marked as settled.")]
    public float settleAlignThreshold   = 5f;

    [Header("Die Physics")]
    [Tooltip("Mass of the die Rigidbody in the simulation scene. Affects momentum when dice collide.")]
    public float dieMass          = 0.5f;
    [Tooltip("Linear drag in the simulation scene. Higher values bleed off speed faster, producing shorter slides after landing.")]
    public float dieLinearDrag    = 0.5f;
    [Tooltip("Angular drag in the simulation scene. Higher values reduce spin faster, producing fewer rolling rotations after landing.")]
    public float dieAngularDrag   = 0.5f;
    [Tooltip("Hard cap on the die's angular speed in the simulation. Prevents extreme spinning that can skip settle detection.")]
    public float dieMaxAngularVel = 50f;

    [Header("Physics Materials")]
    [Tooltip("PhysicsMaterial used on the die collider — controls bounciness and friction against the floor and other dice.")]
    public PhysicsMaterial dieBounce;
    [Tooltip("PhysicsMaterial used on wall and floor colliders in the simulation — controls how much the die bounces off walls.")]
    public PhysicsMaterial wallBounce;

    [Header("Die Removal")]
    [Tooltip("Seconds the die stays visible (coloured red) after being removed from play. 0 = instant.")]
    public float dieRemovalDelay = 0.2f;

    [Header("Rolling Transparency")]
    [Tooltip("Opacity of a die while it is mid-flight (0 = invisible, 1 = fully opaque). Linearly fades to full opacity as the die nears its landing position.")]
    public float rollingStartOpacity = 0.25f;

    [Header("Playback Speed")]
    [Tooltip("Trajectory playback speed multiplier. 1 = real-time physics speed. 2 = advances two trajectory positions per fixed frame. Values below 1 slow the die down.")]
    public float playbackSpeedMultiplier = 1f;

    [Header("Settle VFX")]
    [Tooltip("How many trajectory positions before the final rest position at which the settle ring VFX is triggered. Tune this so the ring finishes expanding as the die arrives.")]
    public int   dustCloudEarlyFrames    = 5;
    [Tooltip("World-unit radius the dust ring grows to when a die settles.")]
    public float dustCloudMaxRadius      = 1f;
    [Tooltip("Seconds for the dust ring to expand from zero to its full radius.")]
    public float dustCloudExpandDuration = 0.3f;
    [Tooltip("Seconds for the dust ring to fade out after it finishes expanding.")]
    public float dustCloudFadeDuration   = 0.2f;
}
