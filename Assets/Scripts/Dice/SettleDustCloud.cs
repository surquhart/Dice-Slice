using System.Collections;
using UnityEngine;

// Expanding ring VFX spawned at die landing position to signal that the die has settled
// and is now eligible for a dash. Expands outward then fades.
public class SettleDustCloud : MonoBehaviour
{
    public static void Spawn(Vector3 diePos, float floorY, int layer,
        float maxRadius, float expandDuration, float fadeDuration)
    {
        var go = new GameObject("SettleDustCloud");
        go.layer = layer;
        go.transform.position = new Vector3(diePos.x, floorY + 0.02f, diePos.z);
        go.AddComponent<SettleDustCloud>().Init(maxRadius, expandDuration, fadeDuration, layer);
    }

    void Init(float maxRadius, float expandDuration, float fadeDuration, int layer)
    {
        const int segments = 32;
        var lr = gameObject.AddComponent<LineRenderer>();
        lr.loop             = true;
        lr.positionCount    = segments;
        lr.useWorldSpace    = false; // local space; transform scale drives ring radius
        lr.startWidth       = 0.12f;
        lr.endWidth         = 0.12f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows   = false;

        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }

        var shader = Shader.Find("Sprites/Default")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null) lr.material = new Material(shader);

        Color dustColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        lr.startColor = dustColor;
        lr.endColor   = dustColor;

        StartCoroutine(Animate(lr, maxRadius, expandDuration, fadeDuration, dustColor));
    }

    IEnumerator Animate(LineRenderer lr, float maxRadius,
        float expandDuration, float fadeDuration, Color dustColor)
    {
        float elapsed = 0f;
        while (elapsed < expandDuration)
        {
            float radius = Mathf.Lerp(0f, maxRadius, elapsed / expandDuration);
            transform.localScale = new Vector3(radius, 1f, radius);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.localScale = new Vector3(maxRadius, 1f, maxRadius);

        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            Color c = dustColor;
            c.a = 1f - elapsed / fadeDuration;
            lr.startColor = c;
            lr.endColor   = c;
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }
}
