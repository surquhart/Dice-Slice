using UnityEngine;

// Builds pip sphere geometry on each face of the die cube at runtime.
public class DiePipBuilder : MonoBehaviour
{
    [SerializeField] public Material pipMaterial;
    [SerializeField] float pipRadius        = 0.06f;
    [SerializeField] float pipSurfaceOffset = 0.005f;

    public void Build()
    {
        Transform existing = transform.Find("PipRoot");
        if (existing != null) DestroyImmediate(existing.gameObject);

        var root = new GameObject("PipRoot");
        root.transform.SetParent(transform, false);
        var t = root.transform;

        // Face normal → pip UV offsets; order matches DieFaceMapper convention.
        CreateFacePips(t, Vector3.up,      Pips1);
        CreateFacePips(t, Vector3.down,    Pips6);
        CreateFacePips(t, Vector3.forward, Pips2);
        CreateFacePips(t, Vector3.back,    Pips5);
        CreateFacePips(t, Vector3.right,   Pips3);
        CreateFacePips(t, Vector3.left,    Pips4);
    }

    private void CreateFacePips(Transform parent, Vector3 faceNormal, Vector2[] offsets)
    {
        // Build orthonormal tangent frame for this face
        Vector3 u = (Mathf.Abs(Vector3.Dot(faceNormal, Vector3.up)) > 0.9f)
            ? Vector3.Cross(faceNormal, Vector3.forward).normalized
            : Vector3.Cross(faceNormal, Vector3.up).normalized;
        Vector3 v = Vector3.Cross(faceNormal, u).normalized;

        foreach (Vector2 offset in offsets)
        {
            Vector3 pos = faceNormal * (0.5f + pipSurfaceOffset)
                        + u * offset.x + v * offset.y;

            var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pip.name = "Pip";
            pip.transform.SetParent(parent, false);
            pip.transform.localPosition = pos;
            pip.transform.localScale    = Vector3.one * (pipRadius * 2f);
            Destroy(pip.GetComponent<SphereCollider>());

            if (pipMaterial != null)
                pip.GetComponent<MeshRenderer>().sharedMaterial = pipMaterial;
        }
    }

    // UV offsets (in face-local u/v space) for pip counts 1–6.
    private static readonly Vector2[] Pips1 = { new(0f, 0f) };
    private static readonly Vector2[] Pips2 = { new(-0.2f,  0.2f), new(0.2f, -0.2f) };
    private static readonly Vector2[] Pips3 = { new(-0.2f,  0.2f), new(0f,    0f),   new(0.2f, -0.2f) };
    private static readonly Vector2[] Pips4 = { new(-0.2f,  0.2f), new(0.2f,  0.2f), new(-0.2f, -0.2f),  new(0.2f, -0.2f) };
    private static readonly Vector2[] Pips5 = { new(-0.2f,  0.2f), new(0.2f,  0.2f), new(0f,     0f),    new(-0.2f, -0.2f), new(0.2f, -0.2f) };
    private static readonly Vector2[] Pips6 = { new(-0.2f,  0.25f), new(0.2f, 0.25f), new(-0.2f,  0f),   new(0.2f,  0f),    new(-0.2f,-0.25f), new(0.2f,-0.25f) };
}
