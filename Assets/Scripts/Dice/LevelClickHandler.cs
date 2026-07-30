using UnityEngine;
using UnityEngine.InputSystem;

// Converts mouse clicks into die rolls by raycasting against the horizontal roll plane.
public class LevelClickHandler : MonoBehaviour
{
    [SerializeField] DiceSettings _settings;

    void Update()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        Camera cam = Camera.main;
        if (cam == null || DiceManager.Instance == null) return;

        float rollY  = _settings ? _settings.rollHeight : 0.5f;
        Vector2 mPos = Mouse.current.position.ReadValue();
        Ray     ray  = cam.ScreenPointToRay(new Vector3(mPos.x, mPos.y, 0f));
        Plane   plane = new Plane(Vector3.up, new Vector3(0f, rollY, 0f));

        if (plane.Raycast(ray, out float dist))
            DiceManager.Instance.RollDie(ray.GetPoint(dist));
    }
}
