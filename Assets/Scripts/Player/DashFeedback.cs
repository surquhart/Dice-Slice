using TMPro;
using UnityEngine;

// Spawned once per dash. Manages the debug line and damage number for that dash.
// Destroyed automatically once both the line and number have fully faded.
// Call Interrupt() to cut the line short and switch to interrupt styling.
public class DashFeedback : MonoBehaviour
{
    public struct Config
    {
        // Line
        public Color lineColor;
        public Color interruptLineColor;
        public float lineBaseWidth;
        public float lineDamageWidthMult;
        public float lineWidthCap;
        public float lineLifetime;
        public float lineFadeDuration;

        // Number
        public Color numberColor;
        public Color interruptNumberColor;
        public float numberBaseSize;
        public float numberDamageScaleMult;
        public float numberSizeCap;
        public float numberLifetime;
        public float numberFadeDuration;
        public float numberLinePosition; // 0 = dash start, 1 = die position

        // Interrupt
        public float interruptShrink;
    }

    // ── runtime state ──────────────────────────────────────────────────────────
    LineRenderer  _line;
    TextMeshPro   _tmp;
    Color         _lineColor;
    float         _lineSpawnTime;
    float         _numberSpawnTime;
    bool          _interrupted;
    Config        _cfg;

    // ── public API ─────────────────────────────────────────────────────────────

    public void Setup(Vector3 lineStart, Vector3 lineEnd, int damage, Config cfg, int diceLayer)
    {
        _cfg       = cfg;
        _lineColor = cfg.lineColor;

        float lineW = Mathf.Min(
            cfg.lineBaseWidth * (1f + damage * cfg.lineDamageWidthMult),
            cfg.lineWidthCap);

        float numScale = Mathf.Min(
            cfg.numberBaseSize * (1f + damage * cfg.numberDamageScaleMult),
            cfg.numberSizeCap);

        BuildLine(lineStart, lineEnd, lineW, diceLayer);
        BuildNumber(lineStart, lineEnd, damage, numScale, diceLayer);

        _lineSpawnTime   = Time.time;
        _numberSpawnTime = Time.time;
    }

    // Resets both lifetimes to their full durations (called when a chain dash occurs).
    public void ResetLifetime()
    {
        _lineSpawnTime   = Time.time;
        _numberSpawnTime = Time.time;
        SetLineAlpha(1f);
        if (_tmp != null) _tmp.alpha = 1f;
    }

    // Cuts the line at interruptPos and applies interrupt visual styling.
    public void Interrupt(Vector3 interruptPos)
    {
        if (_interrupted) return;
        _interrupted = true;

        if (_line != null)
        {
            _line.SetPosition(1, interruptPos);
            _lineColor = _cfg.interruptLineColor;
            float shrunkW = _line.startWidth * _cfg.interruptShrink;
            _line.startWidth = shrunkW;
            _line.endWidth   = shrunkW;
            SetLineAlpha(1f);
        }
        if (_tmp != null)
        {
            _tmp.color = _cfg.interruptNumberColor;
            _tmp.transform.localScale *= _cfg.interruptShrink;
        }
    }

    // ── Unity loop ─────────────────────────────────────────────────────────────

    void Update()
    {
        bool lineDead   = UpdateLine();
        bool numberDead = UpdateNumber();
        if (lineDead && numberDead) Destroy(gameObject);
    }

    // Returns true when the line has fully faded and been destroyed.
    bool UpdateLine()
    {
        if (_line == null) return true;
        float age = Time.time - _lineSpawnTime;
        float total = _cfg.lineLifetime + _cfg.lineFadeDuration;
        if (age >= total) { Destroy(_line.gameObject); _line = null; return true; }
        if (age >= _cfg.lineLifetime)
            SetLineAlpha(1f - (age - _cfg.lineLifetime) / _cfg.lineFadeDuration);
        return false;
    }

    bool UpdateNumber()
    {
        if (_tmp == null) return true;
        float age = Time.time - _numberSpawnTime;
        float total = _cfg.numberLifetime + _cfg.numberFadeDuration;
        if (age >= total) { Destroy(_tmp.gameObject); _tmp = null; return true; }
        if (age >= _cfg.numberLifetime)
            _tmp.alpha = 1f - (age - _cfg.numberLifetime) / _cfg.numberFadeDuration;
        return false;
    }

    // ── construction helpers ───────────────────────────────────────────────────

    void BuildLine(Vector3 start, Vector3 end, float width, int layer)
    {
        var go = new GameObject("DashLine");
        go.layer = layer;
        go.transform.SetParent(transform, false);

        _line = go.AddComponent<LineRenderer>();
        _line.useWorldSpace  = true;
        _line.positionCount  = 2;
        _line.startWidth     = width;
        _line.endWidth       = width;
        _line.startColor     = _cfg.lineColor;
        _line.endColor       = _cfg.lineColor;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows    = false;

        // Sprites/Default supports vertex-color alpha; always available in Unity.
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null) _line.material = new Material(shader);

        _line.SetPosition(0, start);
        _line.SetPosition(1, end);
    }

    void BuildNumber(Vector3 lineStart, Vector3 lineEnd, int damage, float scale, int layer)
    {
        Vector3 pos = Vector3.Lerp(lineStart, lineEnd, _cfg.numberLinePosition);

        var go = new GameObject("DashNumber");
        go.layer = layer;
        go.transform.SetParent(transform, false);
        go.transform.position    = pos;
        // Rotate to lie flat on the floor, readable from this camera angle.
        go.transform.rotation    = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale  = Vector3.one * scale;

        _tmp = go.AddComponent<TextMeshPro>();
        _tmp.text      = damage.ToString();
        _tmp.fontSize  = 36f;
        _tmp.alignment = TextAlignmentOptions.Center;
        _tmp.color     = _cfg.numberColor;

        // Ensure all TMP child meshes share the Dice layer.
        SetLayerRecursive(go, layer);
    }

    void SetLineAlpha(float a)
    {
        if (_line == null) return;
        Color c = _lineColor;
        c.a = a;
        _line.startColor = c;
        _line.endColor   = c;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
