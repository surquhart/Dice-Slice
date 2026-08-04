using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Dev-only overlay that shows the most recently rolled value in the lower-left corner.
// Self-contained: creates its own Canvas and text at runtime, no scene setup required.
// Subscribe timing: OnDieRolled fires synchronously after die.Roll() so the value
// reflects the RNG choice at spawn, not after the playback coroutine finishes.
[DefaultExecutionOrder(100)]
public class DebugRollDisplay : MonoBehaviour
{
    TextMeshProUGUI _label;

    void Start()
    {
        var canvasGO = new GameObject("DebugRollCanvas");
        DontDestroyOnLoad(canvasGO);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var textGO = new GameObject("RollValueText");
        textGO.transform.SetParent(canvasGO.transform, false);

        _label           = textGO.AddComponent<TextMeshProUGUI>();
        _label.fontSize  = 48f;
        _label.color     = Color.white;
        _label.text      = "—";

        var rt           = _label.rectTransform;
        rt.anchorMin     = Vector2.zero;
        rt.anchorMax     = Vector2.zero;
        rt.pivot         = Vector2.zero;
        rt.anchoredPosition = new Vector2(24f, 24f);
        rt.sizeDelta     = new Vector2(120f, 72f);

        DiceManager.OnDieRolled += UpdateDisplay;
    }

    void OnDestroy()
    {
        DiceManager.OnDieRolled -= UpdateDisplay;
    }

    void UpdateDisplay(int value) => _label.text = value.ToString();
}
