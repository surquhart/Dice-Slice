using UnityEngine;

// Defines the play area for a single room and provides bounds to the dice system.
public class Room : MonoBehaviour
{
    [SerializeField] float _width      = 15f;
    [SerializeField] float _depth      = 10f;
    [SerializeField] float _wallHeight = 10f;

    public float Width      => _width;
    public float Depth      => _depth;
    public float WallHeight => _wallHeight;

    void Awake() => ApplyDimensions();

#if UNITY_EDITOR
    void OnValidate() => ApplyDimensions();
#endif

    // Repositions and rescales every child cube to match the serialised dimensions.
    // Floor top surface is always at local Y=0 (the room prefab's coordinate origin).
    void ApplyDimensions()
    {
        float halfW    = _width  / 2f;
        float halfD    = _depth  / 2f;
        float wallCtrY = _wallHeight / 2f;

        SetChild("Floor",      new Vector3(0f,     -0.05f,    0f), new Vector3(_width,      0.1f,    _depth));
        SetChild("Wall_Back",  new Vector3(0f,    wallCtrY,  halfD), new Vector3(_width, _wallHeight,   0.1f));
        SetChild("Wall_Front", new Vector3(0f,    wallCtrY, -halfD), new Vector3(_width, _wallHeight,   0.1f));
        SetChild("Wall_Left",  new Vector3(-halfW, wallCtrY,    0f), new Vector3(0.1f,  _wallHeight, _depth));
        SetChild("Wall_Right", new Vector3( halfW, wallCtrY,    0f), new Vector3(0.1f,  _wallHeight, _depth));
        SetChild("Ceiling",    new Vector3(0f,  _wallHeight,   0f), new Vector3(_width,      0.1f,    _depth));
    }

    void SetChild(string childName, Vector3 localPos, Vector3 localScale)
    {
        Transform t = transform.Find(childName);
        if (t == null) return;
        t.localPosition = localPos;
        t.localScale    = localScale;
    }

    // Returns the play-area bounds in world space. Y base = room world Y (floor surface).
    public Bounds GetBounds()
    {
        Vector3 center = new Vector3(
            transform.position.x,
            transform.position.y + _wallHeight * 0.5f,
            transform.position.z);
        return new Bounds(center, new Vector3(_width, _wallHeight, _depth));
    }
}
