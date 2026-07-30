using UnityEngine;

// Builds a camera-aligned bounding box (floor + 4 walls + ceiling) that keeps dice in view.
// Add [ExecuteAlways] so this rebuilds live in the editor when Inspector values change.
[ExecuteAlways]
public class RollBoundsBox : MonoBehaviour
{
    [SerializeField] DiceSettings _settings;
    [SerializeField] float        _wallThickness = 0.3f;
    [SerializeField] float        _wallHeight    = 4f;
    [SerializeField] bool         _autoFitCamera = true;

    [Header("Manual override (used when autoFitCamera = false)")]
    [SerializeField] Vector2 _manualSize = new(20f, 20f);

    [Header("Debug Visualization")]
    [Tooltip("Assign a material (e.g. semi-transparent Unlit) to make all walls visible.")]
    [SerializeField] Material _debugMaterial;

    private Bounds _bounds;

    void Start() => Rebuild();

    // Rebuild whenever any Inspector field changes, both in edit mode and play mode.
    void OnValidate()
    {
#if UNITY_EDITOR
        // Defer so we're not modifying the hierarchy during serialization.
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
#else
        Rebuild();
#endif
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        // Clear existing walls (DestroyImmediate required in edit mode).
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else                       DestroyImmediate(child);
        }

        float rollY = _settings ? _settings.rollHeight : 0.5f;
        Vector3 center;
        float halfW, halfD;

        if (_autoFitCamera)
            FitToCamera(rollY, out center, out halfW, out halfD);
        else
        {
            center = new Vector3(0f, rollY, 0f);
            halfW  = _manualSize.x * 0.5f;
            halfD  = _manualSize.y * 0.5f;
        }

        float halfH  = _wallHeight * 0.5f;
        float wallCY = rollY + halfH;
        float wt     = _wallThickness;
        float fw     = halfW * 2f;
        float fd     = halfD * 2f;

        Spawn("Floor",
            new Vector3(center.x, rollY - wt * 0.5f, center.z),
            new Vector3(fw + wt * 2f, wt, fd + wt * 2f));

        Spawn("Wall_Left",  new Vector3(center.x - halfW, wallCY, center.z),  new Vector3(wt, _wallHeight, fd + wt * 2f));
        Spawn("Wall_Right", new Vector3(center.x + halfW, wallCY, center.z),  new Vector3(wt, _wallHeight, fd + wt * 2f));
        Spawn("Wall_Back",  new Vector3(center.x, wallCY, center.z + halfD),  new Vector3(fw, _wallHeight, wt));
        Spawn("Ceiling",
            new Vector3(center.x, rollY + _wallHeight, center.z),
            new Vector3(fw + wt * 2f, wt, fd + wt * 2f));

        var frontGO = Spawn("Wall_Front",
            new Vector3(center.x, wallCY, center.z - halfD),
            new Vector3(fw, _wallHeight, wt));
        frontGO.AddComponent<FrontWallGate>();

        if (_settings && _settings.wallBounce != null)
        {
            foreach (Transform child in transform)
                foreach (var c in child.GetComponents<BoxCollider>())
                    c.material = _settings.wallBounce;
        }

        _bounds = new Bounds(
            new Vector3(center.x, rollY + halfH, center.z),
            new Vector3(fw, _wallHeight, fd));
    }

    private GameObject Spawn(string n, Vector3 pos, Vector3 size)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.AddComponent<BoxCollider>().size = size;   // kept as-is for FrontWallGate compatibility

        if (_debugMaterial != null)
        {
            // Child visual sized to match the collider's world footprint.
            var vis = new GameObject("Visual");
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = size;           // parent scale is (1,1,1), so world = size
            vis.AddComponent<MeshFilter>().sharedMesh =
                Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var mr = vis.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = _debugMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
        }

        return go;
    }

    // Uses the scene-view camera in edit mode so FitToCamera works without entering Play.
    private void FitToCamera(float rollY, out Vector3 center, out float halfW, out float halfD)
    {
        Camera cam = Camera.main;
#if UNITY_EDITOR
        if (cam == null && !Application.isPlaying
            && UnityEditor.SceneView.lastActiveSceneView != null)
            cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
        if (cam == null)
        {
            center = new Vector3(0f, rollY, 0f);
            halfW  = _manualSize.x * 0.5f;
            halfD  = _manualSize.y * 0.5f;
            return;
        }

        Plane plane = new Plane(Vector3.up, new Vector3(0f, rollY, 0f));
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < 4; i++)
        {
            float vx  = (i & 1) == 0 ? 0f : 1f;
            float vy  = (i & 2) == 0 ? 0f : 1f;
            Ray   ray = cam.ViewportPointToRay(new Vector3(vx, vy, 1f));

            if (plane.Raycast(ray, out float dist))
            {
                Vector3 p = ray.GetPoint(dist);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }
        }

        center = new Vector3((minX + maxX) * 0.5f, rollY, (minZ + maxZ) * 0.5f);
        halfW  = (maxX - minX) * 0.5f;
        halfD  = (maxZ - minZ) * 0.5f;
    }

    // Always-on gizmo so the bounds are visible in the Scene view even when not selected.
    void OnDrawGizmos()
    {
        if (_bounds.size.sqrMagnitude < 0.01f) return;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.15f);
        Gizmos.DrawCube(_bounds.center, _bounds.size);
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);
        Gizmos.DrawWireCube(_bounds.center, _bounds.size);
    }

    public Bounds GetBounds() => _bounds;
    public void   Refresh()   => Rebuild();
}
