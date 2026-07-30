using System.Collections.Generic;
using UnityEngine;

public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance { get; private set; }

    [SerializeField] DiceSettings _settings;
    [SerializeField] GameObject   _diePrefab;

    private readonly List<DieController> _dice           = new();
    private int                          _rollOrderCounter;
    private RollBoundsBox                _boundsBox;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _boundsBox = FindFirstObjectByType<RollBoundsBox>();
    }

    public DieController RollDie(Vector3 worldTarget, int forcedValue = -1)
    {
        if (_diePrefab == null)
        {
            Debug.LogError("[DiceManager] _diePrefab is not assigned.");
            return null;
        }

        var die = Instantiate(_diePrefab).GetComponent<DieController>();
        if (die == null) { Destroy(die); return null; }

        die.Initialize(_settings, ++_rollOrderCounter);
        _dice.Add(die);
        die.Roll(worldTarget, forcedValue);
        return die;
    }

    public void UnregisterDie(DieController die) => _dice.Remove(die);

    public Bounds GetBoxBounds() => _boundsBox != null
        ? _boundsBox.GetBounds()
        : new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f));

    public IReadOnlyList<DieController> GetDiceInRollOrder() => _dice.AsReadOnly();

    // World-space transform + size of every die that has already settled.
    // Size is included so the simulation uses each die's actual collider footprint.
    public (Vector3, Quaternion, float)[] GetSettledDicePoses(DieController exclude = null)
    {
        var result = new List<(Vector3, Quaternion, float)>();
        foreach (var d in _dice)
            if (d != null && d != exclude && !d.IsRolling)
                result.Add((d.transform.position, d.transform.rotation, d.DieSize));
        return result.ToArray();
    }

    // Trajectory state of every die currently mid-playback.
    // Used by DieSimulator to animate kinematic proxies in Pass 2 so the new die
    // physically interacts with dice that are still rolling.
    public DieSimulator.RollingDieState[] GetRollingDiceStates(DieController exclude = null)
    {
        var result = new List<DieSimulator.RollingDieState>();
        foreach (var d in _dice)
        {
            if (d == null || d == exclude || !d.IsRolling) continue;
            if (d.WorldPositions == null || d.WorldPositions.Length == 0) continue;
            result.Add(new DieSimulator.RollingDieState
            {
                worldPositions = d.WorldPositions,
                rotations      = d.SimRotations,
                currentStep    = d.PlaybackStep,
                dieSize        = d.DieSize,
            });
        }
        return result.ToArray();
    }

    public void ClearAllDice()
    {
        foreach (var d in _dice)
            if (d != null) Destroy(d.gameObject);
        _dice.Clear();
        _rollOrderCounter = 0;
    }
}
