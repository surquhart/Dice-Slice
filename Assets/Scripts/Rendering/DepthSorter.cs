using UnityEngine;

// Depth-sorts a sprite child by pinning its WORLD Y each frame based on the parent's world Z.
// Using world Y (not local Y) means parent scale does not skew the sort key — a child inside
// a scaled prefab is treated identically to one inside an unscaled prefab.
// Front-of-room (lower world Z) → higher world Y → closer to overhead camera → rendered on top.
public class DepthSorter : MonoBehaviour
{
    [Tooltip("World Y for all sprites when parent Z = 0. Use the same value across every prefab.")]
    [SerializeField] float _sortingBaseWorldY = 0.05f;

    [Tooltip("World Y shift per unit of parent world Z. Negative Z (front) raises Y; positive Z (back) lowers it.")]
    [SerializeField] float _sortingScale = 0.005f;

    void LateUpdate()
    {
        float sortZ = transform.parent != null
            ? transform.parent.position.z
            : transform.position.z;

        Vector3 worldPos = transform.position;
        worldPos.y = _sortingBaseWorldY - sortZ * _sortingScale;
        transform.position = worldPos;
    }
}
