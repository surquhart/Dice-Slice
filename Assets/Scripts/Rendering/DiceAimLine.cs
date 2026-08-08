using UnityEngine;

// Draws light blue guide lines from the player to the next 3 dashes in the chain.
// Rendered by the main camera (Default layer) so it appears below entities and the player.
public class DiceAimLine : MonoBehaviour
{
    [SerializeField] Color _lineColor = new Color(0.35f, 0.80f, 1f, 0.55f);
    [SerializeField] float _lineWidth = 0.05f;

    [Tooltip("Y position of the lines in world space. Set just above the floor so they don't clip.")]
    [SerializeField] float _lineWorldY = 0.06f;

    const int MaxSegments = 3;
    LineRenderer[] _lines;

    void Start()
    {
        _lines = new LineRenderer[MaxSegments];
        for (int i = 0; i < MaxSegments; i++)
            _lines[i] = BuildLine();
    }

    LineRenderer BuildLine()
    {
        var go = new GameObject("AimLine");
        go.transform.SetParent(transform, false);
        go.layer = 0; // Default layer — rendered by main camera, below Dice overlay

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace     = true;
        lr.positionCount     = 2;
        lr.startWidth        = _lineWidth;
        lr.endWidth          = _lineWidth;
        lr.startColor        = _lineColor;
        lr.endColor          = _lineColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        lr.enabled           = false;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null) lr.material = new Material(shader);

        return lr;
    }

    void Update()
    {
        if (DiceManager.Instance == null || PlayerController.Instance == null)
        {
            SetAllEnabled(false);
            return;
        }

        var allDice = DiceManager.Instance.GetDiceInRollOrder();
        var playerPos = PlayerController.Instance.transform.position;

        // Collect up to MaxSegments settled dice in roll order.
        var drawDice = new System.Collections.Generic.List<DieController>(MaxSegments);
        foreach (var d in allDice)
        {
            if (d == null || d.IsBeingRemoved || d.IsRolling) continue;
            drawDice.Add(d);
            if (drawDice.Count >= MaxSegments) break;
        }

        Vector3 prev = new Vector3(playerPos.x, _lineWorldY, playerPos.z);

        for (int i = 0; i < MaxSegments; i++)
        {
            if (i < drawDice.Count)
            {
                Vector3 diePos = new Vector3(
                    drawDice[i].transform.position.x, _lineWorldY,
                    drawDice[i].transform.position.z);
                _lines[i].SetPosition(0, prev);
                _lines[i].SetPosition(1, diePos);
                _lines[i].enabled = true;
                prev = diePos;
            }
            else
            {
                _lines[i].enabled = false;
            }
        }
    }

    void SetAllEnabled(bool on)
    {
        if (_lines == null) return;
        foreach (var lr in _lines) if (lr != null) lr.enabled = on;
    }
}
