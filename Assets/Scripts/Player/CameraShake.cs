using System.Collections;
using UnityEngine;

// Attach to the Main Camera. Applies a decaying random offset on the X and Z axes only.
// Call Shake() from PlayerController after a dash completes.
public class CameraShake : MonoBehaviour
{
    Vector3    _restLocalPos;
    Coroutine  _routine;

    void Awake() => _restLocalPos = transform.localPosition;

    public void Shake(float magnitudeX, float magnitudeZ, float duration = 0.25f)
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(DoShake(magnitudeX, magnitudeZ, duration));
    }

    IEnumerator DoShake(float magX, float magZ, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float decay = 1f - elapsed / duration;
            float ox = Random.Range(-magX, magX) * decay;
            float oz = Random.Range(-magZ, magZ) * decay;
            transform.localPosition = _restLocalPos + new Vector3(ox, 0f, oz);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.localPosition = _restLocalPos;
        _routine = null;
    }
}
