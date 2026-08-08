using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Spawned once per dash. Manages line(s) and damage number(s) for that dash.
// Setup() creates the grey line only. Numbers appear via RecordHit, FinalizeNoHit, or InterruptAt.
public class DashFeedback : MonoBehaviour
{
    public struct Config
    {
        // Line
        public Color lineColor;                      // grey — base / pre-hit segment
        public Color hitLineColor;                   // red  — post-hit segment (non-interrupted)
        public Color interruptNoDamageLineColor;     // lighter grey — no-damage interrupt
        public float lineBaseWidth;
        public float lineDamageWidthMult;
        public float lineWidthCap;
        public float lineLifetime;
        public float lineFadeDuration;

        // Number
        public Color numberColor;                    // grey — no-hit midpoint number
        public Color hitNumberColor;                 // red  — numbers at hit locations
        public Color interruptNoDamageNumberColor;   // lighter grey — no-damage interrupt number
        public float numberBaseSize;
        public float numberDamageScaleMult;
        public float numberSizeCap;
        public float interruptNoDamageSizeMult;      // e.g. 0.5 for half size (not damage-scaled)
        public float numberLifetime;
        public float numberFadeDuration;
        public float numberLinePosition;             // 0–1 placement for no-hit midpoint
        public float numberChainAlphaReduction;
        public float numberHitMaxOffset;             // XZ offset applied to hit numbers at damage=0
        public float numberHitMinOffset;             // XZ offset when damage*scaleMult >= sizeCap
    }

    // ── runtime state ──────────────────────────────────────────────────────────
    LineRenderer _greyLine;
    LineRenderer _redLine;                           // null until RecordHit (non-interrupted)
    Color        _greyLineColor;
    float        _lineWidth;
    float        _lineSpawnTime;
    float        _numberSpawnTime = float.MaxValue;  // float.MaxValue = not yet spawned
    float        _currentMaxAlpha = 1f;
    bool         _isFinalized;
    Vector3      _lineStart, _lineEnd;
    Config       _cfg;
    int          _diceLayer;

    readonly List<TextMeshPro> _hitNumbers = new();
    TextMeshPro _midpointNumber;

    // ── public API ─────────────────────────────────────────────────────────────

    public void Setup(Vector3 lineStart, Vector3 lineEnd, int damage, Config cfg, int diceLayer)
    {
        _cfg           = cfg;
        _diceLayer     = diceLayer;
        _lineStart     = lineStart;
        _lineEnd       = lineEnd;
        _greyLineColor = cfg.lineColor;

        _lineWidth = Mathf.Min(
            cfg.lineBaseWidth * (1f + damage * cfg.lineDamageWidthMult),
            cfg.lineWidthCap);

        _greyLine      = BuildLine(lineStart, lineEnd, _lineWidth, _greyLineColor, diceLayer);
        _lineSpawnTime = Time.time;
    }

    // Called per entity hit during a dash. Shortens the grey line to hitPos,
    // creates/extends the red segment from hitPos to dashEnd, places a red number.
    // entityCenter is used to compute the direction the number is offset away from the entity.
    public void RecordHit(Vector3 hitPos, int damage, Vector3 entityCenter)
    {
        hitPos.y = _lineStart.y; // keep all feedback geometry on the same flat Y plane

        if (_greyLine != null)
            _greyLine.SetPosition(1, hitPos);

        if (_redLine == null)
            _redLine = BuildLine(hitPos, _lineEnd, _lineWidth, _cfg.hitLineColor, _diceLayer);
        else
            _redLine.SetPosition(0, hitPos);

        float numScale = Mathf.Min(
            _cfg.numberBaseSize * (1f + damage * _cfg.numberDamageScaleMult),
            _cfg.numberSizeCap);

        Vector3 numPos = HitNumberPosition(hitPos, damage, entityCenter);
        _hitNumbers.Add(BuildNumber(numPos, damage, numScale, _cfg.hitNumberColor, _diceLayer));

        if (_numberSpawnTime == float.MaxValue)
            _numberSpawnTime = Time.time;
    }

    // Computes the XZ-offset position for a hit number: smaller damage → further from entity.
    Vector3 HitNumberPosition(Vector3 hitPos, int damage, Vector3 entityCenter)
    {
        Vector3 toHit = new Vector3(hitPos.x - entityCenter.x, 0f, hitPos.z - entityCenter.z);
        Vector3 dir   = toHit.sqrMagnitude > 0.0001f ? toHit.normalized : Vector3.forward;

        float t   = Mathf.Clamp01(damage * _cfg.numberDamageScaleMult / Mathf.Max(_cfg.numberSizeCap, 0.001f));
        float mag = Mathf.Lerp(_cfg.numberHitMaxOffset, _cfg.numberHitMinOffset, t);

        return new Vector3(hitPos.x + dir.x * mag, hitPos.y, hitPos.z + dir.z * mag);
    }

    // Called when the dash ends with zero hits. Places a grey midpoint number (not damage-scaled).
    public void FinalizeNoHit(int damage)
    {
        if (_isFinalized) return;
        _isFinalized = true;

        Vector3 mid = Vector3.Lerp(_lineStart, _lineEnd, _cfg.numberLinePosition);
        _midpointNumber = BuildNumber(mid, damage, _cfg.numberBaseSize, _cfg.numberColor, _diceLayer);

        if (_numberSpawnTime == float.MaxValue)
            _numberSpawnTime = Time.time;
    }

    // Called when a dash is interrupted.
    // damageDealt=true  → hit already recorded via RecordHit; destroy the red extension.
    // damageDealt=false → shorten/recolor grey line; add half-size lighter-grey midpoint number.
    public void InterruptAt(Vector3 pos, bool damageDealt, int damage)
    {
        if (_isFinalized) return;
        _isFinalized = true;

        pos.y = _lineStart.y; // keep all feedback geometry on the same flat Y plane

        if (damageDealt)
        {
            if (_redLine != null) { Destroy(_redLine.gameObject); _redLine = null; }
        }
        else
        {
            if (_greyLine != null)
            {
                _greyLine.SetPosition(1, pos);
                _greyLineColor = _cfg.interruptNoDamageLineColor;
                ApplyLineColor(_greyLine, _greyLineColor, 1f);
            }

            Vector3 mid   = Vector3.Lerp(_lineStart, pos, 0.5f);
            float   scale = _cfg.numberBaseSize * _cfg.interruptNoDamageSizeMult;
            _midpointNumber = BuildNumber(mid, damage, scale, _cfg.interruptNoDamageNumberColor, _diceLayer);

            if (_numberSpawnTime == float.MaxValue)
                _numberSpawnTime = Time.time;
        }
    }

    // Resets lifetimes and slightly reduces opacity of already-spawned numbers (chain visual).
    public void ResetLifetime()
    {
        _lineSpawnTime   = Time.time;
        _currentMaxAlpha = Mathf.Max(0f, _currentMaxAlpha - _cfg.numberChainAlphaReduction);
        ApplyLineColor(_greyLine, _greyLineColor, 1f);
        if (_redLine != null) ApplyLineColor(_redLine, _cfg.hitLineColor, 1f);

        if (_numberSpawnTime != float.MaxValue)
        {
            _numberSpawnTime = Time.time;
            foreach (var n in _hitNumbers) if (n != null) n.alpha = _currentMaxAlpha;
            if (_midpointNumber != null) _midpointNumber.alpha = _currentMaxAlpha;
        }
    }

    // ── Unity loop ─────────────────────────────────────────────────────────────

    void Update()
    {
        bool linesDead   = UpdateLines();
        bool numbersDead = UpdateNumbers();
        if (linesDead && numbersDead) Destroy(gameObject);
    }

    bool UpdateLines()
    {
        bool greyDead = UpdateOneLine(ref _greyLine, _greyLineColor);
        bool redDead  = _redLine == null || UpdateOneLine(ref _redLine, _cfg.hitLineColor);
        return greyDead && redDead;
    }

    bool UpdateOneLine(ref LineRenderer lr, Color baseColor)
    {
        if (lr == null) return true;
        float age   = Time.time - _lineSpawnTime;
        float total = _cfg.lineLifetime + _cfg.lineFadeDuration;
        if (age >= total) { Destroy(lr.gameObject); lr = null; return true; }
        if (age >= _cfg.lineLifetime)
            ApplyLineColor(lr, baseColor, 1f - (age - _cfg.lineLifetime) / _cfg.lineFadeDuration);
        return false;
    }

    bool UpdateNumbers()
    {
        if (_numberSpawnTime == float.MaxValue) return false;

        float age   = Time.time - _numberSpawnTime;
        float total = _cfg.numberLifetime + _cfg.numberFadeDuration;

        if (age >= total)
        {
            foreach (var n in _hitNumbers) if (n != null) Destroy(n.gameObject);
            _hitNumbers.Clear();
            if (_midpointNumber != null) { Destroy(_midpointNumber.gameObject); _midpointNumber = null; }
            return true;
        }

        foreach (var n in _hitNumbers) if (n != null) ApplyNumberAlpha(n, age);
        if (_midpointNumber != null) ApplyNumberAlpha(_midpointNumber, age);
        return false;
    }

    void ApplyNumberAlpha(TextMeshPro tmp, float age)
    {
        tmp.alpha = age >= _cfg.numberLifetime
            ? _currentMaxAlpha * (1f - (age - _cfg.numberLifetime) / _cfg.numberFadeDuration)
            : _currentMaxAlpha;
    }

    // ── construction helpers ───────────────────────────────────────────────────

    LineRenderer BuildLine(Vector3 start, Vector3 end, float width, Color color, int layer)
    {
        var go = new GameObject("DashLine");
        go.layer = layer;
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace     = true;
        lr.positionCount     = 2;
        lr.startWidth        = width;
        lr.endWidth          = width;
        lr.startColor        = color;
        lr.endColor          = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null) lr.material = new Material(shader);

        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        return lr;
    }

    TextMeshPro BuildNumber(Vector3 pos, int damage, float scale, Color color, int layer)
    {
        var go = new GameObject("DashNumber");
        go.layer = layer;
        go.transform.SetParent(transform, false);
        pos.y += 0.003f; // lift above the line to win the depth test
        go.transform.position   = pos;
        go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.one * scale;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text      = damage.ToString();
        tmp.fontSize  = 36f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = color;

        SetLayerRecursive(go, layer);
        return tmp;
    }

    void ApplyLineColor(LineRenderer lr, Color baseColor, float alpha)
    {
        if (lr == null) return;
        Color c = baseColor;
        c.a = alpha;
        lr.startColor = c;
        lr.endColor   = c;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
