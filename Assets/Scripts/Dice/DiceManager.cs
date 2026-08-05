using System.Collections.Generic;
using UnityEngine;

public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance { get; private set; }

    public static event System.Action<int> OnDieRolled;

    [SerializeField] DiceSettings _settings;
    [SerializeField] GameObject   _diePrefab;

    private readonly List<DieController> _dice           = new();
    private int                          _rollOrderCounter;
    private Room                         _room;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _room = FindAnyObjectByType<Room>();
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
        OnDieRolled?.Invoke(die.RolledValue);
        return die;
    }

    public void UnregisterDie(DieController die) => _dice.Remove(die);

    public Bounds GetBoxBounds() => _room != null
        ? _room.GetBounds()
        : new Bounds(Vector3.zero, new Vector3(15f, 10f, 10f));

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

    // Returns the active die with the lowest RollOrder (spawned earliest).
    // tieBreakPos breaks ties among dice with the same order by nearest XZ distance.
    public DieController GetOldestActiveDie(Vector3 tieBreakPos)
    {
        DieController result  = null;
        int   bestOrder = int.MaxValue;
        float bestDist  = float.MaxValue;
        foreach (var d in _dice)
        {
            if (d == null || d.IsBeingRemoved) continue;
            float dist = XZDist(d.transform.position, tieBreakPos);
            if (d.RollOrder < bestOrder || (d.RollOrder == bestOrder && dist < bestDist))
            {
                result = d; bestOrder = d.RollOrder; bestDist = dist;
            }
        }
        return result;
    }

    // Returns the active die with the highest RollOrder (spawned most recently).
    public DieController GetNewestActiveDie(Vector3 tieBreakPos)
    {
        DieController result  = null;
        int   bestOrder = int.MinValue;
        float bestDist  = float.MaxValue;
        foreach (var d in _dice)
        {
            if (d == null || d.IsBeingRemoved) continue;
            float dist = XZDist(d.transform.position, tieBreakPos);
            if (d.RollOrder > bestOrder || (d.RollOrder == bestOrder && dist < bestDist))
            {
                result = d; bestOrder = d.RollOrder; bestDist = dist;
            }
        }
        return result;
    }

    static float XZDist(Vector3 a, Vector3 b) =>
        Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

    public void ClearAllDice()
    {
        foreach (var d in _dice)
            if (d != null) Destroy(d.gameObject);
        _dice.Clear();
        _rollOrderCounter = 0;
    }
}
