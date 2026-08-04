using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class PlayerController : MonoBehaviour
{
    [Tooltip("Maximum movement speed in world units per second.")]
    [SerializeField] float _topSpeed    = 5f;

    [Tooltip("How quickly the player ramps up to top speed when a direction key is held, in units/s².")]
    [SerializeField] float _acceleration = 20f;

    [Tooltip("How quickly overall velocity bleeds off when no direction key is held at all, applied to the velocity magnitude. Units/s².")]
    [SerializeField] float _deceleration = 15f;

    [Tooltip("How quickly left/right (X) velocity bleeds off when no horizontal key is held but vertical input is still active — e.g., during a forward-to-strafe 90° turn. Higher values produce a tighter turn. Units/s².")]
    [SerializeField] float _axisDecelerationX = 25f;

    [Tooltip("How quickly forward/backward (Z) velocity bleeds off when no vertical key is held but horizontal input is still active — e.g., during a strafe-to-forward 90° turn. Higher values produce a tighter turn. Units/s².")]
    [SerializeField] float _axisDecelerationZ = 25f;

    Rigidbody   _rb;
    BoxCollider _col;
    Vector3     _velocity;
    Vector2     _input;

    void Awake()
    {
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

    // Read input in Update so the Input System processes keyboard events at the
    // render rate, keeping it decoupled from FixedUpdate.
    void Update()
    {
        _input = ReadInput();
    }

    void FixedUpdate()
    {
        Vector3 moveDir = new Vector3(_input.x, 0f, _input.y);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        if (moveDir.sqrMagnitude > 0.001f)
        {
            _velocity += moveDir * (_acceleration * Time.fixedDeltaTime);
            if (_velocity.magnitude > _topSpeed)
                _velocity = _velocity.normalized * _topSpeed;

            // Per-axis deceleration: if one axis has no input while the other does
            // (e.g., mid-turn), bleed off that axis's velocity independently so
            // 90° turns feel tight rather than wide.
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
            if (_velocity.magnitude <= drag)
                _velocity = Vector3.zero;
            else
                _velocity -= _velocity.normalized * drag;
        }

        _velocity.y = 0f;

        Vector3 newPos = _rb.position + _velocity * Time.fixedDeltaTime;
        newPos = ClampToRoomBounds(newPos);
        _rb.MovePosition(newPos);
    }

    // Clamps newPos so the collider faces stay within the room's XZ bounds.
    // Physics wall collision is unreliable because the player's collider shares the
    // exact Y=rollHeight base with the walls, generating ambiguous contacts. Explicit
    // clamping is simpler and always correct for this flat-on-floor setup.
    Vector3 ClampToRoomBounds(Vector3 pos)
    {
        if (DiceManager.Instance == null) return pos;

        Bounds room  = DiceManager.Instance.GetBoxBounds();
        float  halfX = _col.size.x * 0.5f;
        float  halfZ = _col.size.z * 0.5f;

        // _col.center is the local offset from root (no rotation, scale=1 on root,
        // so local == world direction). Solve for root pos given collider face limits:
        //   left face  = pos.x + center.x - halfX >= room.min.x  →  pos.x >= room.min.x + halfX - center.x
        //   right face = pos.x + center.x + halfX <= room.max.x  →  pos.x <= room.max.x - halfX - center.x
        pos.x = Mathf.Clamp(pos.x,
            room.min.x + halfX - _col.center.x,
            room.max.x - halfX - _col.center.x);
        pos.z = Mathf.Clamp(pos.z,
            room.min.z + halfZ - _col.center.z,
            room.max.z - halfZ - _col.center.z);
        return pos;
    }

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
