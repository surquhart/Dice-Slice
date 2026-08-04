using UnityEngine;

// One-way entrance mechanism for any wall or ceiling dice must pass through.
// The solid collider starts open; it locks after each die has fully crossed.
// Set _outwardDir to the world-space direction pointing away from the room interior:
//   Front wall → (0, 0, -1)   Ceiling → (0, 1, 0)
[RequireComponent(typeof(BoxCollider))]
public class FrontWallGate : MonoBehaviour
{
    [SerializeField] Vector3 _outwardDir = new Vector3(0f, 0f, -1f);

    private BoxCollider _solid;
    private BoxCollider _trigger;
    private int         _diceCrossing;

    void Awake()
    {
        _solid         = GetComponent<BoxCollider>();
        _solid.enabled = false;

        // Trigger extends 2 world units outward from the solid, centred 1 world unit out.
        // BoxCollider size/center are in local space, so divide by lossyScale to convert.
        Vector3 scale = transform.lossyScale;
        Vector3 absDir = new Vector3(
            Mathf.Abs(_outwardDir.x),
            Mathf.Abs(_outwardDir.y),
            Mathf.Abs(_outwardDir.z));
        Vector3 localExtension = new Vector3(
            scale.x > 0f ? absDir.x * 2f / scale.x : 0f,
            scale.y > 0f ? absDir.y * 2f / scale.y : 0f,
            scale.z > 0f ? absDir.z * 2f / scale.z : 0f);
        Vector3 localOffset = new Vector3(
            scale.x > 0f ? _outwardDir.x / scale.x : 0f,
            scale.y > 0f ? _outwardDir.y / scale.y : 0f,
            scale.z > 0f ? _outwardDir.z / scale.z : 0f);

        _trigger           = gameObject.AddComponent<BoxCollider>();
        _trigger.isTrigger = true;
        _trigger.size      = _solid.size + localExtension;
        _trigger.center    = localOffset;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Die")) return;
        _diceCrossing++;
        _solid.enabled = false; // stay open while die is crossing
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Die")) return;
        _diceCrossing = Mathf.Max(0, _diceCrossing - 1);
        if (_diceCrossing == 0) _solid.enabled = true; // lock once all dice are inside
    }
}
