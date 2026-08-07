using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class DamageableEntity : MonoBehaviour, IDamageable
{
    // ── HP ─────────────────────────────────────────────────────────────────────

    [Header("HP")]
    [Tooltip("Maximum and starting HP for this entity.")]
    [SerializeField] protected int _maxHP = 10;

    [Tooltip("When to show the HP bar. 0 = show whenever HP is below max. " +
             "Positive = only show when HP is at or below this value. Negative = never show.")]
    [SerializeField] int _hpBarShowThreshold = 0;

    // ── HP Bar ─────────────────────────────────────────────────────────────────

    [Header("HP Bar")]
    [Tooltip("Overrides the bar's uniform width (and proportional height). " +
             "Leave at 0 to scale automatically with entity transform scale.")]
    [SerializeField] float _hpBarSizeOverride = 0f;

    // ── Collision ──────────────────────────────────────────────────────────────

    [Header("Collision")]
    [Tooltip("If true, a dash is interrupted when the player enters this entity's space. " +
             "The entity still takes damage from the interrupted dash.")]
    [SerializeField] bool _isImpassable = false;

    [Tooltip("Override push-out direction in LOCAL space. Leave at zero to push along " +
             "the shortest path out of the entity's space.")]
    [SerializeField] Vector3 _pushDirectionOverride = Vector3.zero;

    // ── Knockback ──────────────────────────────────────────────────────────────

    [Header("Knockback")]
    [Tooltip("Multiplier applied to the global knockback distance and jitter. " +
             "0 = no knockback. 2 = double. Default 1.")]
    [SerializeField] float _knockbackMultiplier = 1f;

    [Tooltip("Override the global knockback jitter. Negative = use global setting.")]
    [SerializeField] float _knockbackJitterOverride = -1f;

    // ── Death ──────────────────────────────────────────────────────────────────

    [Header("Death")]
    [Tooltip("Spawned at the entity's position on death. Leave empty for no VFX.")]
    [SerializeField] GameObject _deathVfxPrefab;

    [Tooltip("Seconds the entity flashes white before being destroyed. 0 = instant.")]
    [SerializeField] float _deathFlashDuration = 0.12f;

    // ── Loot ───────────────────────────────────────────────────────────────────

    [Header("Loot")]
    [Tooltip("One item is selected by weighted random from this table and spawned on death. " +
             "Leave empty for no drops.")]
    [SerializeField] List<LootEntry> _lootTable = new();

    // ── Settings ───────────────────────────────────────────────────────────────

    [Header("Settings")]
    [Tooltip("Assign the EntitySettings asset from Assets/Settings/.")]
    [SerializeField] EntitySettings _settings;

    // ── Public state ───────────────────────────────────────────────────────────

    public int  CurrentHP  { get; protected set; }
    public bool IsAlive    => CurrentHP > 0;
    public bool IsImpassable => _isImpassable;

    // ── Internal ───────────────────────────────────────────────────────────────

    BoxCollider _solidCollider;
    BoxCollider _triggerCollider;

    GameObject _hpBarRoot;
    Transform  _hpBarBg;
    Transform  _hpBarFg;

    protected IMovementBehavior _movement;
    protected IAttackBehavior   _attack;

    bool _isKnockingBack;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        CurrentHP = _maxHP;

        SetupColliders();
        SetupHPBar();

        _movement = GetComponent<IMovementBehavior>();
        _attack   = GetComponent<IAttackBehavior>();
        if (_movement != null) _movement.Initialize(this);
        if (_attack   != null) _attack.Initialize(this);
    }

    void OnDestroy()
    {
        if (_hpBarRoot != null) Destroy(_hpBarRoot);
    }

    // ── Collider setup ─────────────────────────────────────────────────────────

    void SetupColliders()
    {
        // First BoxCollider on this GameObject is the solid one.
        var cols = GetComponents<BoxCollider>();
        _solidCollider = cols.Length > 0 ? cols[0] : gameObject.AddComponent<BoxCollider>();
        _solidCollider.isTrigger = false;

        // Find or create the slightly-larger trigger collider.
        _triggerCollider = null;
        foreach (var c in cols)
            if (c.isTrigger) { _triggerCollider = c; break; }

        if (_triggerCollider == null)
            _triggerCollider = gameObject.AddComponent<BoxCollider>();

        float expand = _settings != null ? _settings.pushOutColliderExpand : 0.05f;
        _triggerCollider.isTrigger = true;
        _triggerCollider.center    = _solidCollider.center;
        _triggerCollider.size      = _solidCollider.size + Vector3.one * expand;
    }

    // ── HP Bar setup ───────────────────────────────────────────────────────────

    void SetupHPBar()
    {
        int diceLayer = LayerMask.NameToLayer("Dice");

        _hpBarRoot = new GameObject($"{name}_HPBar");
        _hpBarBg   = CreateBarSlice("BG", _hpBarRoot.transform, new Color(0.72f, 0.72f, 0.72f), diceLayer);
        _hpBarFg   = CreateBarSlice("FG", _hpBarRoot.transform, new Color(0.85f, 0.12f, 0.12f), diceLayer);
        _hpBarFg.localPosition = new Vector3(0f, 0.001f, 0f);
        _hpBarRoot.SetActive(false);
    }

    static Transform CreateBarSlice(string sliceName, Transform parent, Color color, int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name  = sliceName;
        go.layer = layer;
        Destroy(go.GetComponent<BoxCollider>());
        go.transform.SetParent(parent, false);

        var mr  = go.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mr.material = mat;

        return go.transform;
    }

    // ── FixedUpdate — push-out and movement ────────────────────────────────────

    protected virtual void FixedUpdate()
    {
        if (!IsAlive || _isKnockingBack) return;
        HandlePushOut();
        _movement?.Move(Time.fixedDeltaTime);
    }

    void HandlePushOut()
    {
        var player = PlayerController.Instance;
        if (player == null || player.IsDashing) return;

        // Approximate player center in world space using the collider center offset.
        Vector3 playerCenter = player.transform.position + player.ColliderCenter;
        if (!_triggerCollider.bounds.Contains(playerCenter)) return;

        Vector3 pushDir;
        if (_pushDirectionOverride != Vector3.zero)
        {
            pushDir = transform.TransformDirection(_pushDirectionOverride).normalized;
        }
        else
        {
            pushDir = playerCenter - transform.position;
            pushDir.y = 0f;
            pushDir = pushDir.sqrMagnitude > 0.0001f ? pushDir.normalized : Vector3.forward;
        }

        float speed    = _settings != null ? _settings.pushOutSpeed : 10f;
        float stepDist = speed * Time.fixedDeltaTime;
        Vector3 newPlayerPos = player.transform.position + pushDir * stepDist;

        if (IsInRoomBounds(newPlayerPos))
        {
            player.PushTo(newPlayerPos);
        }
        else
        {
            // Player is against a wall — push the entity the other way.
            Vector3 newEntityPos = transform.position - pushDir * stepDist;
            transform.position = ClampToRoom(newEntityPos);
        }
    }

    // ── LateUpdate — HP bar ────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (_hpBarRoot == null) return;

        bool show = ShouldShowHPBar();
        _hpBarRoot.SetActive(show);
        if (!show) return;

        float zOffset = _settings != null ? _settings.hpBarZOffset : 0.7f;
        _hpBarRoot.transform.position = transform.position + new Vector3(0f, 0.01f, -zOffset);
        _hpBarRoot.transform.rotation = Quaternion.identity;

        float baseWidth = _hpBarSizeOverride > 0f
            ? _hpBarSizeOverride
            : (_settings != null ? _settings.hpBarBaseWidth : 0.9f) * transform.lossyScale.x;
        float depth     = baseWidth * (_settings != null ? _settings.hpBarAspectRatio : 0.14f);
        float thickness = 0.015f;

        _hpBarBg.localScale = new Vector3(baseWidth, thickness, depth);

        float fill      = _maxHP > 0 ? Mathf.Clamp01((float)CurrentHP / _maxHP) : 0f;
        float fillWidth = Mathf.Max(0.001f, baseWidth * fill);
        _hpBarFg.localScale    = new Vector3(fillWidth, thickness + 0.001f, depth);
        _hpBarFg.localPosition = new Vector3((fill - 1f) * baseWidth * 0.5f, 0.001f, 0f);
    }

    bool ShouldShowHPBar()
    {
        if (_hpBarShowThreshold < 0) return false;
        if (!IsAlive || CurrentHP <= 0) return false;
        if (_hpBarShowThreshold == 0) return CurrentHP < _maxHP;
        return CurrentHP <= _hpBarShowThreshold;
    }

    // ── IDamageable ────────────────────────────────────────────────────────────

    public virtual void TakeDamage(int damage, Vector3 dashDirection)
    {
        if (!IsAlive) return;

        int actual = ProcessDamage(damage);
        CurrentHP = Mathf.Max(0, CurrentHP - actual);

        if (_knockbackMultiplier > 0f && _settings != null)
            StartKnockback(dashDirection);

        if (CurrentHP <= 0)
            OnDeath();
    }

    // Override to apply armor or other modifiers before HP is subtracted.
    protected virtual int ProcessDamage(int rawDamage) => rawDamage;

    protected virtual void OnDeath()
    {
        if (_hpBarRoot != null) { Destroy(_hpBarRoot); _hpBarRoot = null; }

        if (_deathVfxPrefab != null)
            Instantiate(_deathVfxPrefab, transform.position, Quaternion.identity);

        SpawnLoot();

        StopAllCoroutines();
        StartCoroutine(DeathSequence());
    }

    // ── Knockback ──────────────────────────────────────────────────────────────

    void StartKnockback(Vector3 dashDirection)
    {
        if (_isKnockingBack) StopCoroutine(nameof(KnockbackRoutine));
        StartCoroutine(KnockbackRoutine(dashDirection));
    }

    IEnumerator KnockbackRoutine(Vector3 dashDirection)
    {
        _isKnockingBack = true;

        float jitterMax = _knockbackJitterOverride >= 0f
            ? _knockbackJitterOverride
            : _settings.knockbackJitter;
        float jitterAngle = Random.Range(-jitterMax, jitterMax);

        Vector3 dir = Quaternion.Euler(0f, jitterAngle, 0f) * dashDirection;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        float distance = _settings.knockbackDistance * _knockbackMultiplier;
        Vector3 startPos  = transform.position;
        Vector3 targetPos = ClampToRoom(startPos + dir * distance);
        float   duration  = _settings.knockbackDuration;
        float   elapsed   = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            transform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }

        transform.position = targetPos;
        _isKnockingBack    = false;
    }

    // ── Death sequence ─────────────────────────────────────────────────────────

    IEnumerator DeathSequence()
    {
        if (_deathFlashDuration > 0f)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", Color.white);
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(mpb);
            yield return new WaitForSeconds(_deathFlashDuration);
        }
        Destroy(gameObject);
    }

    // ── Loot ───────────────────────────────────────────────────────────────────

    void SpawnLoot()
    {
        if (_lootTable == null || _lootTable.Count == 0) return;

        float total = 0f;
        foreach (var entry in _lootTable) total += Mathf.Max(0f, entry.weight);
        if (total <= 0f) return;

        float roll = Random.Range(0f, total);
        float acc  = 0f;
        foreach (var entry in _lootTable)
        {
            acc += Mathf.Max(0f, entry.weight);
            if (roll < acc)
            {
                if (entry.prefab != null)
                    Instantiate(entry.prefab, transform.position, Quaternion.identity);
                return;
            }
        }
    }

    // ── Room bounds helpers ────────────────────────────────────────────────────

    Vector3 ClampToRoom(Vector3 pos)
    {
        if (DiceManager.Instance == null) return pos;
        Bounds room = DiceManager.Instance.GetBoxBounds();
        float hx = _solidCollider != null
            ? _solidCollider.size.x * transform.lossyScale.x * 0.5f : 0.5f;
        float hz = _solidCollider != null
            ? _solidCollider.size.z * transform.lossyScale.z * 0.5f : 0.5f;
        pos.x = Mathf.Clamp(pos.x, room.min.x + hx, room.max.x - hx);
        pos.z = Mathf.Clamp(pos.z, room.min.z + hz, room.max.z - hz);
        pos.y = transform.position.y;
        return pos;
    }

    bool IsInRoomBounds(Vector3 pos)
    {
        if (DiceManager.Instance == null) return true;
        Bounds room = DiceManager.Instance.GetBoxBounds();
        return pos.x >= room.min.x && pos.x <= room.max.x &&
               pos.z >= room.min.z && pos.z <= room.max.z;
    }
}
