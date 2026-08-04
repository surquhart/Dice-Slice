using UnityEngine;
using UnityEngine.InputSystem;

// Converts mouse clicks into die rolls. Raycasts against room geometry so that
// clicking a wall projects the hit point onto the floor and inserts inward.
public class LevelClickHandler : MonoBehaviour
{
    [SerializeField] DiceSettings _settings;

    void Update()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        Camera cam = Camera.main;
        if (cam == null || DiceManager.Instance == null) return;

        float   rollY = _settings ? _settings.rollHeight : 0.5f;
        Vector2 mPos  = Mouse.current.position.ReadValue();
        Ray     ray   = cam.ScreenPointToRay(new Vector3(mPos.x, mPos.y, 0f));

        Vector3 target;

        // Raycast against room geometry; exclude Dice layer so dice don't intercept clicks.
        int diceLayer = LayerMask.NameToLayer("Dice");
        int notDice   = diceLayer >= 0 ? ~(1 << diceLayer) : Physics.DefaultRaycastLayers;

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, notDice))
        {
            // Project the hit point (wall or floor) down to the roll plane.
            target = new Vector3(hit.point.x, rollY, hit.point.z);
        }
        else
        {
            // Fallback for clicks into the void: intersect the infinite roll plane.
            Plane plane = new Plane(Vector3.up, new Vector3(0f, rollY, 0f));
            if (!plane.Raycast(ray, out float dist)) return;
            target = ray.GetPoint(dist);
            target.y = rollY;
        }

        // Inset from every wall by the larger of: half the die's face diagonal (prevents wall
        // clipping) and wallInsetMargin (the sim's own acceptance zone — targets beyond it
        // always fail all 20 attempts, especially when dieSize < wallInsetMargin * sqrt(2)).
        if (_settings != null)
        {
            Bounds b       = DiceManager.Instance.GetBoxBounds();
            float halfDiag = _settings.dieSize * Mathf.Sqrt(2f) * 0.5f;
            float inset    = Mathf.Max(halfDiag, _settings.wallInsetMargin);
            target.x = Mathf.Clamp(target.x, b.min.x + inset, b.max.x - inset);
            target.z = Mathf.Clamp(target.z, b.min.z + inset, b.max.z - inset);
        }

        DiceManager.Instance.RollDie(target);
    }
}
