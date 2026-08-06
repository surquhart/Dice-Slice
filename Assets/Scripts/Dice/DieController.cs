using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class DieController : MonoBehaviour
{
    [SerializeField] DiceSettings _settings;

    [Header("Face Layout")]
    [Tooltip("One entry per face. Each face has a local-space normal, a value, and a probability weight. " +
             "Defaults to the standard Western d6 layout if left empty.")]
    [SerializeField] List<DieFace> _faces;

    [Tooltip("Fired when the die settles; argument is the rolled value.")]
    public UnityEvent<int> OnRollComplete;

    [SerializeField] int _rolledValue;
    public int  RolledValue    => _rolledValue;
    public int  RollOrder      { get; private set; }
    public bool IsRolling      { get; private set; }
    public bool IsBeingRemoved { get; private set; }

    // Exposed so DiceManager can provide this die's trajectory to DieSimulator
    // when a later die rolls while this one is still mid-playback.
    public int          PlaybackStep   { get; private set; }
    public Vector3[]    WorldPositions { get; private set; }   // world-space, offset already applied
    public Quaternion[] SimRotations   { get; private set; }
    public float        DieSize        => _settings ? _settings.dieSize : 1f;

    private Rigidbody _rb;

    // Renderer base colors cached before we make materials transparent, so
    // SetRenderersAlpha can tint with the correct original hue at any alpha.
    readonly Dictionary<Renderer, Color> _baseColors = new();

    void Awake()
    {
        _rb             = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        if (_faces == null || _faces.Count == 0)
            _faces = DefaultD6Faces();
    }

    // Called when component is first added in the Editor — seeds the default layout.
    void Reset() => _faces = DefaultD6Faces();

    // Called on recompile / inspector change — ensures existing prefabs get the
    // default layout populated in the Inspector without entering Play mode.
    void OnValidate()
    {
        if (_faces == null || _faces.Count == 0)
            _faces = DefaultD6Faces();
    }

    public void Initialize(DiceSettings settings, int rollOrder)
    {
        _settings = settings;
        RollOrder = rollOrder;
        transform.localScale = Vector3.one * (_settings ? _settings.dieSize : 1f);

        // Move die and all children onto the Dice layer so the overlay
        // camera can render them above all scene geometry.
        int diceLayer = LayerMask.NameToLayer("Dice");
        if (diceLayer >= 0) SetLayerRecursively(gameObject, diceLayer);
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    public void Roll(Vector3 targetWorldPos, int forcedValue = -1)
    {
        if (IsRolling) return;

        // Pick the desired face by weighted random, or find the first face with forcedValue.
        // Falls back to weighted random when forcedValue matches no face in the layout.
        int desiredFaceIdx = (forcedValue >= 0) ? FindFaceIndexByValue(forcedValue) : -1;
        if (desiredFaceIdx < 0) desiredFaceIdx = PickWeightedFaceIndex();
        int desired = _faces[desiredFaceIdx].value;

        Bounds boxBounds = DiceManager.Instance != null
            ? DiceManager.Instance.GetBoxBounds()
            : new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f));

        Vector3 throwDir = Vector3.forward;
        if (Camera.main != null)
        {
            Vector3 projected = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up);
            if (projected.sqrMagnitude > 0.001f) throwDir = projected.normalized;
        }

        var settledPoses  = DiceManager.Instance?.GetSettledDicePoses(exclude: this);
        var rollingStates = DiceManager.Instance?.GetRollingDiceStates(exclude: this);

        DieSimulator.SimResult sim = DieSimulator.Run(
            _settings, boxBounds, targetWorldPos, throwDir, desired,
            settledDice:  settledPoses,
            rollingDice:  rollingStates);

        if (!sim.success)
            Debug.LogWarning("[DieController] No in-bounds simulation found; rolling anyway.");

        // Find which face is naturally on top after simulation, then pick whichever
        // face with the desired value has a normal closest to that — minimises the
        // correction rotation, so the trajectory looks as natural as possible.
        // Apply Q to EVERY rotation step so the die tumbles through the corrected
        // arc from frame 1 — no visible snap at landing.
        Vector3    simTopNormal   = GetTopFaceNormal(sim.finalRotation);
        Vector3    desiredNormal  = BestNormalForValue(desired, simTopNormal);
        Quaternion Q              = Quaternion.FromToRotation(desiredNormal, simTopNormal);

        float offX = sim.startPos.x;
        float offZ = sim.startPos.z;
        Vector3[]    simPositions = sim.positions ?? System.Array.Empty<Vector3>();
        Quaternion[] simRotations = sim.rotations ?? System.Array.Empty<Quaternion>();

        // correctedRotations: visual playback with face correction baked in.
        // SimRotations: kept original so obstacle-tracking proxies use the true physics arc.
        Quaternion[] correctedRotations = new Quaternion[simRotations.Length];
        for (int k = 0; k < simRotations.Length; k++)
            correctedRotations[k] = simRotations[k] * Q;

        // Pre-compute world-space positions for obstacle tracking by later dice.
        WorldPositions = new Vector3[simPositions.Length];
        for (int k = 0; k < simPositions.Length; k++)
            WorldPositions[k] = new Vector3(
                simPositions[k].x + offX,
                simPositions[k].y,
                simPositions[k].z + offZ);
        SimRotations = simRotations;
        PlaybackStep = 0;

        transform.position = sim.startPos;
        transform.rotation = sim.startRotation * Q;  // corrected start rotation

        _rolledValue = desired;
        IsRolling   = true;

        StartCoroutine(PlaybackTrajectory(simPositions, correctedRotations, offX, offZ));
    }

    private IEnumerator PlaybackTrajectory(Vector3[] positions, Quaternion[] rotations,
                                           float offsetX, float offsetZ)
    {
        MakeRenderersTransparent();
        int   totalSteps  = positions.Length;
        float startOpacity = _settings ? _settings.rollingStartOpacity : 0.25f;

        // Speed multiplier: how many trajectory positions to advance per fixed frame.
        // Values > 1 speed up; values < 1 slow down (hold each position for multiple frames).
        float speedMult   = _settings ? Mathf.Max(0.01f, _settings.playbackSpeedMultiplier) : 1f;

        // Dust ring fires this many positions before the trajectory ends.
        int   earlyPositions = _settings ? _settings.dustCloudEarlyFrames : 5;
        int   dustStep       = Mathf.Max(0, totalSteps - 1 - earlyPositions);
        bool  dustSpawned    = false;
        int   diceLayer      = LayerMask.NameToLayer("Dice");

        // Pre-compute final world position so the ring always marks the landing spot,
        // even when spawned while the die is still a few positions away.
        Vector3 finalWorldPos = totalSteps > 0
            ? new Vector3(positions[totalSteps - 1].x + offsetX,
                          positions[totalSteps - 1].y,
                          positions[totalSteps - 1].z + offsetZ)
            : transform.position;

        SetRenderersAlpha(startOpacity);

        // Accumulator: each fixed frame, advance `speedMult` worth of positions.
        // When accum >= 1, process one position and subtract 1 (repeat while >= 1).
        // When accum < 1, hold the current position for this frame.
        float accum = 0f;
        int   step  = 0;

        while (step < totalSteps)
        {
            accum += speedMult;
            while (accum >= 1f && step < totalSteps)
            {
                transform.position = new Vector3(positions[step].x + offsetX,
                                                 positions[step].y,
                                                 positions[step].z + offsetZ);
                transform.rotation = rotations[step];
                PlaybackStep       = step;
                accum             -= 1f;
                step++;
            }

            float t = totalSteps > 1 ? (float)PlaybackStep / (totalSteps - 1) : 1f;
            SetRenderersAlpha(Mathf.Lerp(startOpacity, 1f, t));

            if (!dustSpawned && PlaybackStep >= dustStep && _settings != null)
            {
                dustSpawned = true;
                SettleDustCloud.Spawn(finalWorldPos, _settings.rollHeight, diceLayer,
                    _settings.dustCloudMaxRadius,
                    _settings.dustCloudExpandDuration,
                    _settings.dustCloudFadeDuration);
            }

            yield return new WaitForFixedUpdate();
        }

        SetRenderersAlpha(1f);
        IsRolling = false;
        OnRollComplete?.Invoke(RolledValue);
    }

    // Converts every renderer's material to a transparent instance so alpha changes
    // take effect. Caches the original base color so hue is preserved at any opacity.
    void MakeRenderersTransparent()
    {
        _baseColors.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            Color origColor = Color.white;
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor"))
                origColor = r.sharedMaterial.GetColor("_BaseColor");
            _baseColors[r] = origColor;

            var mat = r.material; // creates a per-renderer instance
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }

    void SetRenderersAlpha(float alpha)
    {
        if (IsBeingRemoved) return; // RemovalSequence owns the visual from here on
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!_baseColors.TryGetValue(r, out var baseColor))
                baseColor = Color.white;
            baseColor.a = alpha;
            mpb.SetColor("_BaseColor", baseColor);
            r.SetPropertyBlock(mpb);
        }
    }

    // Removes the die from the active list immediately, then visually destroys it
    // after dieRemovalDelay so exit animations/VFX can play without affecting gameplay.
    public void TriggerRemoval()
    {
        if (IsBeingRemoved) return;
        IsBeingRemoved = true;
        DiceManager.Instance?.UnregisterDie(this);
        StartCoroutine(RemovalSequence());
    }

    IEnumerator RemovalSequence()
    {
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", Color.red);
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.SetPropertyBlock(mpb);

        float delay = _settings ? _settings.dieRemovalDelay : 0.2f;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }

    // ── Face layout helpers ────────────────────────────────────────────────────

    // Weighted random: selects a face index proportional to each face's weight.
    int PickWeightedFaceIndex()
    {
        float total = 0f;
        foreach (var f in _faces) total += Mathf.Max(0f, f.weight);
        if (total <= 0f) return 0;
        float roll = Random.Range(0f, total);
        float acc  = 0f;
        for (int i = 0; i < _faces.Count; i++)
        {
            acc += Mathf.Max(0f, _faces[i].weight);
            if (roll < acc) return i;
        }
        return _faces.Count - 1;
    }

    // Returns the index of the first face whose value matches, or -1 if none.
    int FindFaceIndexByValue(int value)
    {
        for (int i = 0; i < _faces.Count; i++)
            if (_faces[i].value == value) return i;
        return -1;
    }

    // Local-space normal of the face most aligned with world-up at the given rotation.
    Vector3 GetTopFaceNormal(Quaternion worldRotation)
    {
        Vector3 best    = Vector3.up;
        float   bestDot = float.MinValue;
        foreach (var f in _faces)
        {
            Vector3 n   = f.normal.normalized;
            float   dot = Vector3.Dot(worldRotation * n, Vector3.up);
            if (dot > bestDot) { bestDot = dot; best = n; }
        }
        return best;
    }

    // Among all faces with the given value, return the local normal closest to
    // referenceNormal — this minimises the correction rotation Q.
    Vector3 BestNormalForValue(int value, Vector3 referenceNormal)
    {
        Vector3 best    = _faces[0].normal.normalized;
        float   bestDot = float.MinValue;
        bool    found   = false;
        foreach (var f in _faces)
        {
            if (f.value != value) continue;
            Vector3 n   = f.normal.normalized;
            float   dot = Vector3.Dot(n, referenceNormal);
            if (!found || dot > bestDot) { bestDot = dot; best = n; found = true; }
        }
        return best;
    }

    // Standard Western d6: +Y=1, -Y=6, +Z=2, -Z=5, +X=3, -X=4.
    static List<DieFace> DefaultD6Faces() => new List<DieFace>
    {
        new DieFace { normal = Vector3.up,      value = 1, weight = 1f },
        new DieFace { normal = Vector3.down,    value = 6, weight = 1f },
        new DieFace { normal = Vector3.forward, value = 2, weight = 1f },
        new DieFace { normal = Vector3.back,    value = 5, weight = 1f },
        new DieFace { normal = Vector3.right,   value = 3, weight = 1f },
        new DieFace { normal = Vector3.left,    value = 4, weight = 1f },
    };

    void OnDestroy() => DiceManager.Instance?.UnregisterDie(this);
}
