using UnityEngine;

// One-way entrance mechanism for the front (near-camera) wall of RollBoundsBox.
// The wall starts open; it locks solid after each die has fully entered the box.
[RequireComponent(typeof(BoxCollider))]
public class FrontWallGate : MonoBehaviour
{
    private BoxCollider _solid;
    private BoxCollider _trigger;
    private int         _diceCrossing;

    void Awake()
    {
        _solid         = GetComponent<BoxCollider>();
        _solid.enabled = false; // open on start

        // Trigger zone extends outward so the die is fully inside before OnTriggerExit fires.
        _trigger           = gameObject.AddComponent<BoxCollider>();
        _trigger.isTrigger = true;
        _trigger.size      = _solid.size + new Vector3(0f, 0f, 2f);
        _trigger.center    = new Vector3(0f, 0f, -1f); // lean outward (away from box interior)
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
