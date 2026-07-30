using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class DieController : MonoBehaviour
{
    [SerializeField] DiceSettings _settings;

    [Tooltip("Fired when the die settles; argument is the rolled value (1-6).")]
    public UnityEvent<int> OnRollComplete;

    public int  RolledValue { get; private set; }
    public int  RollOrder   { get; private set; }
    public bool IsRolling   { get; private set; }

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
        WorldPositions = new Vector3[sim.positions.Length];
        for (int k = 0; k < sim.positions.Length; k++)
            WorldPositions[k] = new Vector3(
                sim.positions[k].x + offX,
                sim.positions[k].y,
                sim.positions[k].z + offZ);
        SimRotations = sim.rotations;
        PlaybackStep = 0;

        RolledValue = desired;
        IsRolling   = true;

        StartCoroutine(PlaybackTrajectory(sim.positions, sim.rotations, offX, offZ));
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

    void OnDestroy() => DiceManager.Instance?.UnregisterDie(this);
}
