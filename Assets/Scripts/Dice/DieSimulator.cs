using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DieSimulator
{
    public struct SimResult
    {
        public bool         success;
        public Quaternion   startRotation;
        public Quaternion   finalRotation;
        public Vector3      startPos;
        public Vector3      landingPos;
        public Vector3[]    positions;
        public Quaternion[] rotations;
    }

    // Consumed by DiceManager.GetRollingDiceStates and passed back into Run().
    public struct RollingDieState
    {
        public Vector3[]    worldPositions;  // world-space position at each playback step
        public Quaternion[] rotations;
        public int          currentStep;     // index the die is currently at in its playback
        public float        dieSize;
    }

    private static Scene        _simScene;
    private static PhysicsScene _physicsScene;
    private static GameObject   _simFloor;
    private static BoxCollider  _simFloorCol;
    private static bool         _ready;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _ready       = false;
        _simFloor    = null;
        _simFloorCol = null;
    }

    private static void EnsureReady(float floorY, PhysicsMaterial floorMat)
    {
        if (!_ready)
        {
            _simScene     = SceneManager.CreateScene("__DiceSimulation",
                                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physicsScene = _simScene.GetPhysicsScene();
            _simFloor     = new GameObject("SimFloor");
            SceneManager.MoveGameObjectToScene(_simFloor, _simScene);
            _simFloorCol      = _simFloor.AddComponent<BoxCollider>();
            _simFloorCol.size = new Vector3(400f, 0.1f, 400f);
            _ready = true;
        }
        _simFloor.transform.position = new Vector3(0f, floorY - 0.05f, 0f);
        if (_simFloorCol != null && floorMat != null)
            _simFloorCol.material = floorMat;
    }

    public static SimResult Run(
        DiceSettings              settings,
        Bounds                    boxBounds,
        Vector3                   targetWorldPos,
        Vector3                   throwDir,
        int                       desiredValue,
        int                       seed         = -1,
        (Vector3, Quaternion, float)[] settledDice  = null,
        RollingDieState[]         rollingDice  = null)
    {
        float floorY  = settings ? settings.rollHeight : 0.5f;
        float dieSize = settings ? settings.dieSize    : 1f;
        float dieHalf = dieSize * 0.5f;

        EnsureReady(floorY, settings ? settings.wallBounce : null);

        if (seed < 0) seed = Random.Range(0, int.MaxValue);
        Random.State savedState = Random.state;
        Random.InitState(seed);

        // ── Adaptive speed and loft ──────────────────────────────────────────────
        // Measure depth of the target from the front (camera-near) wall.
        float boxExtentAlong = Mathf.Abs(boxBounds.extents.x * throwDir.x)
                             + Mathf.Abs(boxBounds.extents.z * throwDir.z);
        float targetDepth    = Vector3.Dot(targetWorldPos - boxBounds.center, throwDir)
                             + boxExtentAlong;
        targetDepth = Mathf.Max(targetDepth, 0.5f);
        float normalizedDepth = Mathf.Clamp01(targetDepth / (boxExtentAlong * 2f));

        float loft = Mathf.Lerp(
            settings ? settings.launchLoftNear : 0.15f,
            settings ? settings.launchLoftFar  : 0.55f,
            normalizedDepth);

        float g        = Mathf.Abs(Physics.gravity.y);
        float margin   = settings ? settings.launchSpeedMargin : 1.5f;
        float speedCap = settings ? settings.launchSpeedMax    : 22f;
        float speedMin = settings ? settings.launchSpeed       : 6f;
        float hzNeeded = Mathf.Sqrt(targetDepth * margin * g / (2f * loft));
        float hz       = Mathf.Clamp(Mathf.Max(speedMin, hzNeeded), 0f, speedCap);

        Vector3 linearVel = throwDir * hz + Vector3.up * (hz * loft);

        float heightBoost = settings
            ? Mathf.Lerp(settings.launchHeightBoostNear, 0f, normalizedDepth)
            : 0f;
        // ────────────────────────────────────────────────────────────────────────

        int   maxAttempts = settings ? settings.maxSimAttempts  : 20;
        float inset       = settings ? settings.wallInsetMargin : 0.6f;

        Bounds insetBounds = new Bounds(
            boxBounds.center,
            boxBounds.size - new Vector3(inset * 2f, 0f, inset * 2f));

        float jitter = settings ? Random.Range(-settings.rollHeightJitter, settings.rollHeightJitter) : 0f;
        float startY = floorY + dieHalf + 0.01f + jitter + heightBoost;

        SimResult result    = default;
        Vector3   winAngVel = Vector3.zero;

        // ── Pass 1: find an in-bounds trajectory without any obstacle awareness ──
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var angVel   = new Vector3(Random.Range(-15f, 15f), Random.Range(-15f, 15f), Random.Range(-15f, 15f));
            var startRot = Random.rotation;

            var (finalRot, simLanding, positions, rotations) =
                RunOnce(settings, dieSize, linearVel, angVel, startRot, floorY, startY);

            float offsetX = targetWorldPos.x - simLanding.x;
            float offsetZ = targetWorldPos.z - simLanding.z;

            bool inBounds = true;
            foreach (Vector3 p in positions)
            {
                float rx = p.x + offsetX;
                float rz = p.z + offsetZ;
                if (rx < insetBounds.min.x || rx > insetBounds.max.x || rz > insetBounds.max.z)
                {
                    inBounds = false;
                    break;
                }
            }

            if (inBounds)
            {
                winAngVel            = angVel;
                result.success       = true;
                result.startRotation = startRot;
                result.finalRotation = finalRot;
                result.startPos      = new Vector3(offsetX, startY, offsetZ);
                result.landingPos    = targetWorldPos;
                result.positions     = positions;
                result.rotations     = rotations;
                break;
            }
        }

        // ── Pass 2: re-run with all other dice as obstacles ──────────────────────
        bool hasSettled = settledDice  != null && settledDice.Length  > 0;
        bool hasRolling = rollingDice  != null && rollingDice.Length  > 0;

        if (result.success && (hasSettled || hasRolling))
        {
            float offsetX = result.startPos.x;
            float offsetZ = result.startPos.z;

            var allProxies     = new List<GameObject>();
            var rollingProxies = new List<(GameObject go, RollingDieState state)>();

            // Settled dice → static BoxCollider obstacles
            if (hasSettled)
            {
                foreach (var (wPos, wRot, wSize) in settledDice)
                {
                    var obs = SpawnProxy("SimObstacle",
                        new Vector3(wPos.x - offsetX, wPos.y, wPos.z - offsetZ),
                        wRot, wSize, settings, kinematic: false);
                    allProxies.Add(obs);
                }
            }

            // Rolling dice → kinematic Rigidbody proxies that step through their trajectories
            if (hasRolling)
            {
                foreach (var state in rollingDice)
                {
                    if (state.worldPositions == null
                        || state.currentStep >= state.worldPositions.Length) continue;

                    int     s0   = state.currentStep;
                    Vector3 wp0  = state.worldPositions[s0];
                    var proxyGO  = SpawnProxy("SimRollingProxy",
                        new Vector3(wp0.x - offsetX, wp0.y, wp0.z - offsetZ),
                        state.rotations[s0], state.dieSize, settings, kinematic: true);

                    allProxies.Add(proxyGO);
                    rollingProxies.Add((proxyGO, state));
                }
            }

            try
            {
                var proxyArr = rollingProxies.Count > 0 ? rollingProxies.ToArray() : null;
                var (finalRot2, _, positions2, rotations2) =
                    RunOnce(settings, dieSize, linearVel, winAngVel, result.startRotation,
                            floorY, startY, proxyArr, offsetX, offsetZ);

                result.finalRotation = finalRot2;
                result.positions     = positions2;
                result.rotations     = rotations2;
            }
            finally
            {
                foreach (var obj in allProxies)
                    Object.DestroyImmediate(obj);
            }
        }

        Random.state = savedState;
        return result;
    }

    // Creates a collider-only proxy in the sim scene.
    // kinematic=true adds a Rigidbody so physics can impart forces on collision.
    private static GameObject SpawnProxy(string name, Vector3 simPos, Quaternion rot,
        float size, DiceSettings settings, bool kinematic)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, _simScene);
        go.transform.position   = simPos;
        go.transform.rotation   = rot;
        go.transform.localScale = Vector3.one * size;
        var col  = go.AddComponent<BoxCollider>();
        col.size = Vector3.one;
        if (settings && settings.dieBounce) col.material = settings.dieBounce;
        if (kinematic)
        {
            var rb         = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
        return go;
    }

    private static (Quaternion finalRot, Vector3 landingPos, Vector3[] positions, Quaternion[] rotations)
        RunOnce(DiceSettings settings, float dieSize,
                Vector3 linearVel, Vector3 angVel, Quaternion startRot,
                float floorY, float startY,
                (GameObject go, RollingDieState state)[] rollingProxies = null,
                float simOffX = 0f, float simOffZ = 0f)
    {
        var dieGO = new GameObject("SimDie");
        SceneManager.MoveGameObjectToScene(dieGO, _simScene);
        dieGO.transform.position   = new Vector3(0f, startY, 0f);
        dieGO.transform.rotation   = startRot;
        dieGO.transform.localScale = Vector3.one * dieSize;

        var col  = dieGO.AddComponent<BoxCollider>();
        col.size = Vector3.one;
        if (settings && settings.dieBounce) col.material = settings.dieBounce;

        var rb = dieGO.AddComponent<Rigidbody>();
        rb.mass               = settings ? settings.dieMass          : 0.5f;
        rb.linearDamping      = settings ? settings.dieLinearDrag    : 0.5f;
        rb.angularDamping     = settings ? settings.dieAngularDrag   : 0.5f;
        rb.maxAngularVelocity = settings ? settings.dieMaxAngularVel : 50f;
        rb.linearVelocity     = linearVel;
        rb.angularVelocity    = angVel;

        int   maxSteps   = settings ? settings.maxSimSteps            : 600;
        float dt         = Time.fixedDeltaTime;
        float vThr       = settings ? settings.settleSpeedThreshold   : 0.05f;
        float aThr       = settings ? settings.settleAngularThreshold : 0.1f;
        float alignLimit = settings ? settings.settleAlignThreshold   : 5f;

        var  positions  = new List<Vector3>();
        var  rotations  = new List<Quaternion>();
        var  finalRot   = startRot;
        var  landingPos = dieGO.transform.position;
        bool settled    = false;

        for (int i = 0; i < maxSteps; i++)
        {
            // Advance each rolling proxy to its position for this sim step BEFORE simulating.
            // This synchronizes the proxy's motion with the real playback cadence — both
            // advance one FixedUpdate step at a time using the same dt.
            if (rollingProxies != null)
            {
                foreach (var (proxyGO, state) in rollingProxies)
                {
                    int step = state.currentStep + i;
                    step = step < state.worldPositions.Length
                        ? step
                        : state.worldPositions.Length - 1;
                    Vector3 wp = state.worldPositions[step];
                    proxyGO.transform.position = new Vector3(wp.x - simOffX, wp.y, wp.z - simOffZ);
                    proxyGO.transform.rotation = state.rotations[step];
                }
            }

            _physicsScene.Simulate(dt);
            positions.Add(dieGO.transform.position);
            rotations.Add(dieGO.transform.rotation);

            if (rb.linearVelocity.magnitude < vThr && rb.angularVelocity.magnitude < aThr)
            {
                if (IsFaceAligned(dieGO.transform.rotation, alignLimit))
                {
                    finalRot   = dieGO.transform.rotation;
                    landingPos = dieGO.transform.position;
                    settled    = true;
                    break;
                }
            }
        }

        if (!settled)
        {
            finalRot   = dieGO.transform.rotation;
            landingPos = dieGO.transform.position;
        }

        Object.DestroyImmediate(dieGO);
        return (finalRot, landingPos, positions.ToArray(), rotations.ToArray());
    }

    private static bool IsFaceAligned(Quaternion q, float alignThresholdDeg)
    {
        float bestDot = 0f;
        bestDot = Mathf.Max(bestDot, Mathf.Abs(Vector3.Dot(q * Vector3.right,   Vector3.up)));
        bestDot = Mathf.Max(bestDot, Mathf.Abs(Vector3.Dot(q * Vector3.up,      Vector3.up)));
        bestDot = Mathf.Max(bestDot, Mathf.Abs(Vector3.Dot(q * Vector3.forward, Vector3.up)));
        return Mathf.Acos(Mathf.Clamp01(bestDot)) * Mathf.Rad2Deg < alignThresholdDeg;
    }
}
