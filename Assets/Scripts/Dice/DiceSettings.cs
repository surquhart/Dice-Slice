using UnityEngine;

[CreateAssetMenu(fileName = "DiceSettings", menuName = "Dice Slice/Dice Settings")]
public class DiceSettings : ScriptableObject
{
    [Header("Die Size")]
    [Tooltip("Die edge length in world units. Scales the visual mesh, physics collider, and simulation.")]
    public float dieSize = 1f;

    [Header("Roll Plane")]
    public float rollHeight = 0.5f;
    [Tooltip("+-randomisation applied to die spawn height per throw")]
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
    public float settleSpeedThreshold   = 0.05f;
    public float settleAngularThreshold = 0.1f;
    [Tooltip("Max degrees of tilt from flat for a die to be considered settled (prevents freezing mid-tumble)")]
    public float settleAlignThreshold   = 5f;

    [Header("Die Physics")]
    public float dieMass          = 0.5f;
    public float dieLinearDrag    = 0.5f;
    public float dieAngularDrag   = 0.5f;
    public float dieMaxAngularVel = 50f;

    [Header("Physics Materials")]
    public PhysicsMaterial dieBounce;
    public PhysicsMaterial wallBounce;
}
