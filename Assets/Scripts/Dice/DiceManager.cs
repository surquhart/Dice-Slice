using System.Collections.Generic;
using UnityEngine;

public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance { get; private set; }

    public static event System.Action<int> OnDieRolled;

    [SerializeField] DiceSettings _settings;

    [Header("Die Prefabs (keys 1–0)")]
    [Tooltip("Slot 0 = key 1, Slot 1 = key 2, …, Slot 9 = key 0. Leave a slot empty to skip that key.")]
    [SerializeField] GameObject[] _diePrefabs = new GameObject[10];

    private int _activeDieIndex = 0;

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

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if      (kb.digit1Key.wasPressedThisFrame) TrySetActive(0);
        else if (kb.digit2Key.wasPressedThisFrame) TrySetActive(1);
        else if (kb.digit3Key.wasPressedThisFrame) TrySetActive(2);
        else if (kb.digit4Key.wasPressedThisFrame) TrySetActive(3);
        else if (kb.digit5Key.wasPressedThisFrame) TrySetActive(4);
        else if (kb.digit6Key.wasPressedThisFrame) TrySetActive(5);
        else if (kb.digit7Key.wasPressedThisFrame) TrySetActive(6);
        else if (kb.digit8Key.wasPressedThisFrame) TrySetActive(7);
        else if (kb.digit9Key.wasPressedThisFrame) TrySetActive(8);
        else if (kb.digit0Key.wasPressedThisFrame) TrySetActive(9);
    }

    // Only switches if the target slot has a prefab assigned.
    void TrySetActive(int index)
    {
        if (index < _diePrefabs.Length && _diePrefabs[index] != null)
            _activeDieIndex = index;
    }

    public DieController RollDie(Vector3 worldTarget, int forcedValue = -1)
    {
        var prefab = (_diePrefabs != null && _activeDieIndex < _diePrefabs.Length)
            ? _diePrefabs[_activeDieIndex]
            : null;

        if (prefab == null)
        {
            Debug.LogError($"[DiceManager] No prefab assigned to slot {_activeDieIndex} (key {(_activeDieIndex + 1) % 10}).");
            return null;
        }

        var die = Instantiate(prefab).GetComponent<DieController>();
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
    // Only settled dice are eligible — a rolling die's displayed value is not yet known.
    // tieBreakPos breaks ties among dice with the same order by nearest XZ distance.
    public DieController GetOldestActiveDie(Vector3 tieBreakPos)
    {
        DieController result  = null;
        int   bestOrder = int.MaxValue;
        float bestDist  = float.MaxValue;
        foreach (var d in _dice)
        {
            if (d == null || d.IsBeingRemoved || d.IsRolling) continue;
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
