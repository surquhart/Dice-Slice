using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance { get; private set; }

    // ── Movement ───────────────────────────────────────────────────────────────
    [Header("Movement")]
    [Tooltip("Maximum movement speed in world units per second.")]
    [SerializeField] float _topSpeed     = 5f;

    [Tooltip("How quickly the player ramps up to top speed when a direction key is held, in units/s².")]
    [SerializeField] float _acceleration = 20f;

    [Tooltip("How quickly overall velocity bleeds off when no direction key is held at all, applied to the velocity magnitude. Units/s².")]
    [SerializeField] float _deceleration = 15f;

    [Tooltip("How quickly left/right (X) velocity bleeds off when no horizontal key is held but vertical input is still active — e.g., during a forward-to-strafe 90° turn. Higher values produce a tighter turn. Units/s².")]
    [SerializeField] float _axisDecelerationX = 25f;

    [Tooltip("How quickly forward/backward (Z) velocity bleeds off when no vertical key is held but horizontal input is still active. Higher values produce a tighter turn. Units/s².")]
    [SerializeField] float _axisDecelerationZ = 25f;

    // ── Dash — Core ────────────────────────────────────────────────────────────
    [Header("Dash — Core")]
    [Tooltip("Key that initiates a dash.")]
    [SerializeField] Key _dashKey = Key.Space;

    [Tooltip("Seconds between pressing the dash key and movement beginning. Player is considered dashing during this time and cannot move normally.")]
    [SerializeField] float _dashDelay = 0.05f;

    [Tooltip("Seconds for the player to travel from their position to the target die, regardless of distance. Set to 0 for instantaneous teleport.")]
    [SerializeField] float _dashDuration = 0.1f;

    [Tooltip("Seconds after the dash movement ends before normal movement resumes. Another dash can still be initiated during this window.")]
    [SerializeField] float _postDashDelay = 0.1f;

    [Tooltip("If true, the player keeps their pre-dash velocity after the dash. If false, velocity is zeroed. Either way, dashEndVelocity is added.")]
    [SerializeField] bool _retainVelocity = false;

    [Tooltip("Speed added in the dash direction at the end of every dash, regardless of retainVelocity.")]
    [SerializeField] float _dashEndVelocity = 3f;

    [Tooltip("Seconds within which a new dash adds the previous dash's total damage to its own (fully compounding).")]
    [SerializeField] float _chainWindow = 0.5f;

    // ── Dash — Line ────────────────────────────────────────────────────────────
    [Header("Dash — Line")]
    [Tooltip("Colour of the debug line drawn for a completed dash.")]
    [SerializeField] Color _lineColor          = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Tooltip("Colour of the line when the dash is interrupted.")]
    [SerializeField] Color _interruptLineColor = new Color(0.78f, 0.78f, 0.78f, 1f);

    [Tooltip("Base width of the dash line in world units. LineRenderer default is 0.1.")]
    [SerializeField] float _lineBaseWidth      = 0.1f;

    [Tooltip("Multiplier applied per point of damage to increase line width (multiplicative: width = base × (1 + damage × multiplier)). Default is one tenth of base width.")]
    [SerializeField] float _lineDamageWidthMult = 0.01f;

    [Tooltip("Maximum line width regardless of damage.")]
    [SerializeField] float _lineWidthCap       = 0.5f;

    [Tooltip("Seconds the line stays fully opaque before it begins to fade.")]
    [SerializeField] float _lineLifetime       = 0.8f;

    [Tooltip("Seconds over which the line fades out after its lifetime expires.")]
    [SerializeField] float _lineFadeDuration   = 0.5f;

    // ── Dash — Number ──────────────────────────────────────────────────────────
    [Header("Dash — Number")]
    [Tooltip("Colour of the damage number. Should be noticeably darker than the line colour for readability.")]
    [SerializeField] Color _numberColor          = new Color(0.30f, 0.30f, 0.30f, 1f);

    [Tooltip("Colour of the damage number when the dash is interrupted.")]
    [SerializeField] Color _interruptNumberColor = new Color(0.62f, 0.62f, 0.62f, 1f);

    [Tooltip("Base world-space scale of the damage number. At 1, the number is approximately 1 unit tall along the floor (depth axis).")]
    [SerializeField] float _numberBaseSize       = 1f;

    [Tooltip("Scale increase per point of damage (multiplicative: scale = base × (1 + damage × multiplier)).")]
    [SerializeField] float _numberDamageScaleMult = 0.1f;

    [Tooltip("Maximum scale of the damage number regardless of damage.")]
    [SerializeField] float _numberSizeCap        = 3f;

    [Tooltip("Position along the dash line where the number appears. 0 = dash start, 1 = die position.")]
    [SerializeField] float _numberLinePosition   = 0.5f;

    [Tooltip("Seconds the number stays fully opaque before it begins to fade.")]
    [SerializeField] float _numberLifetime       = 1f;

    [Tooltip("Seconds over which the number fades out after its lifetime expires.")]
    [SerializeField] float _numberFadeDuration   = 0.3f;

    [Tooltip("How much maximum opacity is lost each time a number's lifetime is reset during a chain. 0 = no change; 0.2 = each chain step fades the number by another 20%.")]
    [SerializeField] float _numberChainAlphaReduction = 0.2f;

    // ── Dash — Screen Shake ────────────────────────────────────────────────────
    [Header("Dash — Screen Shake")]
    [Tooltip("Base camera shake magnitude on the X axis.")]
    [SerializeField] float _shakeBaseX          = 0.05f;

    [Tooltip("Base camera shake magnitude on the Z axis.")]
    [SerializeField] float _shakeBaseZ          = 0.05f;

    [Tooltip("X shake increase per point of damage (multiplicative).")]
    [SerializeField] float _shakeDamageMultX    = 0.1f;

    [Tooltip("Z shake increase per point of damage (multiplicative).")]
    [SerializeField] float _shakeDamageMultZ    = 0.1f;

    [Tooltip("Maximum camera shake magnitude on the X axis regardless of damage.")]
    [SerializeField] float _shakeCapX           = 0.3f;

    [Tooltip("Maximum camera shake magnitude on the Z axis regardless of damage.")]
    [SerializeField] float _shakeCapZ           = 0.3f;

    // ── Dash — Interrupt ───────────────────────────────────────────────────────
    [Header("Dash — Interrupt")]
    [Tooltip("Scale applied to both the line width and number size when a dash is interrupted (e.g., 0.5 = half size).")]
    [SerializeField] float _interruptShrink = 0.5f;

    // ── Runtime ────────────────────────────────────────────────────────────────

    enum DashPhase { None, Delaying, Moving, PostDash }
    DashPhase _dashPhase = DashPhase.None;

    // IsDashing is true only during Delay + Moving; other systems gate on this.
    public bool IsDashing => _dashPhase == DashPhase.Delaying || _dashPhase == DashPhase.Moving;

    Rigidbody   _rb;
    BoxCollider _col;
    Vector3     _velocity;
    Vector2     _input;
    Coroutine   _dashRoutine;

    // Chain damage tracking
    float _lastDashEndTime = -999f;
    int   _lastDashTotalDamage;

    // Active feedback objects — lifetime reset when a chain occurs.
    readonly List<DashFeedback> _activeFeedback = new();

    CameraShake _cameraShake;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;

        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<BoxCollider>();

        _rb.isKinematic            = true;
        _rb.useGravity             = false;
        _rb.interpolation          = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.constraints            = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

        int diceLayer   = LayerMask.NameToLayer("Dice");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (diceLayer >= 0 && playerLayer >= 0)
            Physics.IgnoreLayerCollision(playerLayer, diceLayer, true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    void Start()
    {
        _cameraShake = Camera.main != null ? Camera.main.GetComponent<CameraShake>() : null;
    }

    void Update()
    {
        _input = ReadInput();

        // Dash input: allowed from None and PostDash states.
        if (_dashPhase == DashPhase.None || _dashPhase == DashPhase.PostDash)
        {
            if (Keyboard.current != null && Keyboard.current[_dashKey].wasPressedThisFrame)
                TryInitiateDash();
        }
    }

    void FixedUpdate()
    {
        // Movement is suspended during all dash phases. Do NOT call MovePosition here
        // while dashing — the WaitForFixedUpdate coroutine owns position during Moving,
        // and calling it here would be resolved by physics before the coroutine's call,
        // permanently overriding the dash trajectory every frame.
        if (_dashPhase != DashPhase.None) return;

        Vector3 moveDir = new Vector3(_input.x, 0f, _input.y);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        if (moveDir.sqrMagnitude > 0.001f)
        {
            _velocity += moveDir * (_acceleration * Time.fixedDeltaTime);
            if (_velocity.magnitude > _topSpeed)
                _velocity = _velocity.normalized * _topSpeed;

            // Per-axis deceleration during turns.
            if (Mathf.Abs(_input.x) < 0.001f)
            {
                float stepX = _axisDecelerationX * Time.fixedDeltaTime;
                if (Mathf.Abs(_velocity.x) <= stepX) _velocity.x = 0f;
                else _velocity.x -= Mathf.Sign(_velocity.x) * stepX;
            }
            if (Mathf.Abs(_input.y) < 0.001f)
            {
                float stepZ = _axisDecelerationZ * Time.fixedDeltaTime;
                if (Mathf.Abs(_velocity.z) <= stepZ) _velocity.z = 0f;
                else _velocity.z -= Mathf.Sign(_velocity.z) * stepZ;
            }
        }
        else
        {
            float drag = _deceleration * Time.fixedDeltaTime;
            if (_velocity.magnitude <= drag) _velocity = Vector3.zero;
            else _velocity -= _velocity.normalized * drag;
        }

        _velocity.y = 0f;
        Vector3 newPos = _rb.position + _velocity * Time.fixedDeltaTime;
        newPos = ClampToRoomBounds(newPos);
        _rb.MovePosition(newPos);
    }

    // ── Dash ───────────────────────────────────────────────────────────────────

    void TryInitiateDash()
    {
        if (DiceManager.Instance == null) return;
        var target = DiceManager.Instance.GetOldestActiveDie(_rb.position);
        if (target == null) return;

        if (_dashRoutine != null) StopCoroutine(_dashRoutine);
        _dashRoutine = StartCoroutine(DashRoutine(target));
    }

    IEnumerator DashRoutine(DieController target)
    {
        _dashPhase = DashPhase.Delaying;

        yield return new WaitForSeconds(_dashDelay);

        // ── Setup ──────────────────────────────────────────────────────────────
        Vector3 dashStart = _rb.position;
        Vector3 dashEnd   = new Vector3(target.transform.position.x,
                                        dashStart.y,
                                        target.transform.position.z);

        // Chain damage: add previous dash's total if within the chain window.
        bool chaining = (Time.time - _lastDashEndTime) <= _chainWindow;
        int  chainBonus = chaining ? _lastDashTotalDamage : 0;
        int  damage     = target.RolledValue + chainBonus;

        // Remove the die from the active list immediately.
        target.TriggerRemoval();

        // Spawn visual feedback (line + number).
        // Line and number sit slightly above the floor so they don't clip.
        float feedbackY = dashStart.y + 0.05f;
        var lineStart = new Vector3(dashStart.x, feedbackY, dashStart.z);
        var lineEnd   = new Vector3(dashEnd.x,   feedbackY, dashEnd.z);

        // If chaining, reset lifetimes of previous feedback before spawning new one.
        if (chaining) ResetAllFeedbackLifetimes();

        var feedback = SpawnFeedback(lineStart, lineEnd, damage);

        // Screen shake.
        ApplyScreenShake(damage);

        // Cache pre-dash velocity for retainVelocity logic.
        Vector3 preDashVelocity = _velocity;
        Vector3 dashDir         = (dashEnd - dashStart);
        if (dashDir.sqrMagnitude > 0.0001f) dashDir.Normalize();

        _dashPhase = DashPhase.Moving;

        // ── Movement ───────────────────────────────────────────────────────────
        if (_dashDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < _dashDuration)
            {
                float t = elapsed / _dashDuration;
                _rb.MovePosition(Vector3.Lerp(dashStart, dashEnd, t));
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }
        _rb.MovePosition(dashEnd);

        // ── Post-dash velocity ─────────────────────────────────────────────────
        _velocity  = _retainVelocity ? preDashVelocity : Vector3.zero;
        _velocity += dashDir * _dashEndVelocity;

        _lastDashEndTime     = Time.time;
        _lastDashTotalDamage = damage;

        _dashPhase = DashPhase.PostDash;
        yield return new WaitForSeconds(_postDashDelay);
        _dashPhase = DashPhase.None;
    }

    // Publicly interruptible — call this when the dash is blocked by a future hazard/enemy.
    public void InterruptDash(Vector3 stopPos)
    {
        if (_dashPhase != DashPhase.Moving) return;
        if (_dashRoutine != null) { StopCoroutine(_dashRoutine); _dashRoutine = null; }
        _rb.MovePosition(stopPos);
        _velocity  = Vector3.zero;
        _dashPhase = DashPhase.None;
        foreach (var fb in _activeFeedback) fb?.Interrupt(stopPos);
    }

    // ── Visual helpers ─────────────────────────────────────────────────────────

    DashFeedback SpawnFeedback(Vector3 lineStart, Vector3 lineEnd, int damage)
    {
        int diceLayer = LayerMask.NameToLayer("Dice");

        var go = new GameObject("DashFeedback");
        var fb = go.AddComponent<DashFeedback>();

        fb.Setup(lineStart, lineEnd, damage, BuildFeedbackConfig(), diceLayer);

        _activeFeedback.RemoveAll(x => x == null);
        _activeFeedback.Add(fb);
        return fb;
    }

    void ResetAllFeedbackLifetimes()
    {
        _activeFeedback.RemoveAll(x => x == null);
        foreach (var fb in _activeFeedback) fb.ResetLifetime();
    }

    void ApplyScreenShake(int damage)
    {
        if (_cameraShake == null) return;
        float magX = Mathf.Min(_shakeBaseX * (1f + damage * _shakeDamageMultX), _shakeCapX);
        float magZ = Mathf.Min(_shakeBaseZ * (1f + damage * _shakeDamageMultZ), _shakeCapZ);
        _cameraShake.Shake(magX, magZ);
    }

    DashFeedback.Config BuildFeedbackConfig() => new DashFeedback.Config
    {
        lineColor           = _lineColor,
        interruptLineColor  = _interruptLineColor,
        lineBaseWidth       = _lineBaseWidth,
        lineDamageWidthMult = _lineDamageWidthMult,
        lineWidthCap        = _lineWidthCap,
        lineLifetime        = _lineLifetime,
        lineFadeDuration    = _lineFadeDuration,

        numberColor           = _numberColor,
        interruptNumberColor  = _interruptNumberColor,
        numberBaseSize        = _numberBaseSize,
        numberDamageScaleMult = _numberDamageScaleMult,
        numberSizeCap         = _numberSizeCap,
        numberLifetime             = _numberLifetime,
        numberFadeDuration         = _numberFadeDuration,
        numberLinePosition         = _numberLinePosition,
        numberChainAlphaReduction  = _numberChainAlphaReduction,

        interruptShrink = _interruptShrink,
    };

    // ── Room clamping ──────────────────────────────────────────────────────────

    Vector3 ClampToRoomBounds(Vector3 pos)
    {
        if (DiceManager.Instance == null) return pos;
        Bounds room  = DiceManager.Instance.GetBoxBounds();
        float  halfX = _col.size.x * 0.5f;
        float  halfZ = _col.size.z * 0.5f;
        pos.x = Mathf.Clamp(pos.x, room.min.x + halfX - _col.center.x, room.max.x - halfX - _col.center.x);
        pos.z = Mathf.Clamp(pos.z, room.min.z + halfZ - _col.center.z, room.max.z - halfZ - _col.center.z);
        return pos;
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    Vector2 ReadInput()
    {
        if (Keyboard.current == null) return Vector2.zero;
        float x = 0f, z = 0f;
        if (Keyboard.current.dKey.isPressed) x += 1f;
        if (Keyboard.current.aKey.isPressed) x -= 1f;
        if (Keyboard.current.wKey.isPressed) z += 1f;
        if (Keyboard.current.sKey.isPressed) z -= 1f;
        return new Vector2(x, z);
    }
}
