using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class DieController : MonoBehaviour
{
    [SerializeField] DiceSettings _settings;

    [Tooltip("Fired when the die settles; argument is the rolled value (1-6).")]
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

    private Rigidbody     _rb;
    private DiePipBuilder _pipBuilder;
    private Transform     _pipRoot;

    void Awake()
    {
        _rb             = GetComponent<Rigidbody>();
        _pipBuilder     = GetComponent<DiePipBuilder>();
        _rb.isKinematic = true;
    }

    public void Initialize(DiceSettings settings, int rollOrder)
    {
        _settings = settings;
        RollOrder = rollOrder;
        transform.localScale = Vector3.one * (_settings ? _settings.dieSize : 1f);
        _pipBuilder?.Build();
        _pipRoot = transform.Find("PipRoot");

        // Move die and all pip children onto the Dice layer so the overlay
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

    void Start()
    {
        if (_pipRoot == null)
        {
            _pipBuilder?.Build();
            _pipRoot = transform.Find("PipRoot");
        }
    }

    public void Roll(Vector3 targetWorldPos, int forcedValue = -1)
    {
        if (IsRolling) return;

        int desired = (forcedValue >= 1 && forcedValue <= 6) ? forcedValue : Random.Range(1, 7);

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

        // Pip remap is set BEFORE the die starts moving — faces never change after this point.
        if (_pipRoot != null)
        {
            int simTop = DieFaceMapper.GetTopFaceValue(sim.finalRotation);
            _pipRoot.localRotation = DieFaceMapper.PipRemapRotation(simTop, desired);
        }

        transform.position = sim.startPos;
        transform.rotation = sim.startRotation;

        // Pre-compute world-space positions so a subsequent die's simulation can treat
        // this die as a kinematic obstacle while it's still mid-playback.
        float offX = sim.startPos.x;
        float offZ = sim.startPos.z;
        Vector3[]    simPositions = sim.positions ?? System.Array.Empty<Vector3>();
        Quaternion[] simRotations = sim.rotations ?? System.Array.Empty<Quaternion>();
        WorldPositions = new Vector3[simPositions.Length];
        for (int k = 0; k < simPositions.Length; k++)
            WorldPositions[k] = new Vector3(
                simPositions[k].x + offX,
                simPositions[k].y,
                simPositions[k].z + offZ);
        SimRotations = simRotations;
        PlaybackStep = 0;

        _rolledValue = desired;
        IsRolling   = true;

        StartCoroutine(PlaybackTrajectory(simPositions, simRotations, offX, offZ));
    }

    private IEnumerator PlaybackTrajectory(Vector3[] positions, Quaternion[] rotations,
                                           float offsetX, float offsetZ)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            PlaybackStep = i;
            transform.position = new Vector3(positions[i].x + offsetX,
                                             positions[i].y,
                                             positions[i].z + offsetZ);
            transform.rotation = rotations[i];
            yield return new WaitForFixedUpdate();
        }

        IsRolling = false;
        OnRollComplete?.Invoke(RolledValue);
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

    void OnDestroy() => DiceManager.Instance?.UnregisterDie(this);
}
