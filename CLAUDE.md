# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Dice Slice** — Unity 6 (6000.5.2f1), Universal Render Pipeline. A 2.5D isometric rogue-like dungeon crawling game in which the player can click to roll dice onto the board in specific locations. Their pawn can then dash to and between dice to deal damage to any enemy the pawn crosses. The damage dealt is based on the number on the die being dashed to. Dashes can be chained. Chaining a dash causes the current dash to inherit the damage of the previous dash. There are many different dice which have unique face and number configurations and unique effects. Instead of using regular physics, dice are pre-simulated and then kinematically launched to deterministically display results. This allows for potential future networking and allows the system to have more control over the dice.

## Working with the Editor

This project has UnityMCP configured, so you can interact with the Unity Editor directly via tools (`mcp__UnityMCP__*`). Always:
1. Use `read_console` after any script change to check for compilation errors before proceeding.
2. Poll `editor_state` resource (`mcpforunity://editor/state`) to confirm `isCompiling` is false before using new types.
3. Use `manage_scene` to inspect scene state, `manage_gameobject` / `manage_components` to inspect/modify GameObjects.

## Running & Testing

There is no automated test suite for gameplay logic. Testing is done by entering Play mode:
- Via MCP: `manage_editor` with `action: "set_play_mode_state"`.
- Left-click in the Game view to call `DiceManager.RollDie()` via `LevelClickHandler`.

The Unity Test Framework package is installed (`com.unity.test-framework 1.7.0`) but no tests exist yet.

## Architecture: Pre-Simulated Kinematic Dice

**Core invariant:** Dice never use live Unity physics for visible motion. Every roll runs a full physics simulation in a hidden local physics scene first, records the trajectory, then plays it back frame-by-frame on a kinematic Rigidbody. This guarantees in-bounds landings and deterministic results.

### Data flow

```
LevelClickHandler (mouse click at roll plane)
  → DiceManager.RollDie(worldTarget)       // uses _diePrefabs[_activeDieIndex]
      → Instantiate Die prefab (DieController on a UV-mapped FBX mesh)
      → DieController.Roll()
          → PickWeightedFaceIndex()         // weighted random from DieFace list
          → DiceManager.GetSettledDicePoses()     // static obstacles
          → DiceManager.GetRollingDiceStates()    // kinematic obstacles (in-flight dice)
          → DieSimulator.Run()                    // two-pass simulation
          → Compute Q = FromToRotation(desiredNormal, simTopNormal)
          → Pre-multiply Q into ALL trajectory rotations (correctedRotations[])
          → PlaybackTrajectory coroutine          // WaitForFixedUpdate per step
          → OnRollComplete.Invoke(value)
```

### Key files

| File | Role |
|---|---|
| `Assets/Scripts/Dice/DiceSettings.cs` | `ScriptableObject` — single source of truth for all tuning (launch speed, physics, settle thresholds). Asset lives at `Assets/Settings/Dice/DiceSettings.asset`. |
| `Assets/Scripts/Room/Room.cs` | `MonoBehaviour` on the Room prefab root. Stores `_width`, `_depth`, `_wallHeight`. `GetBounds()` returns the play-area `Bounds` consumed by `DiceManager` and `DieSimulator`. Changing any dimension field in the Inspector immediately reshapes all child geometry via `OnValidate`. |
| `DiceManager.cs` | Singleton orchestrator. Holds `_diePrefabs[10]` — one prefab per digit key (slot 0 = key 1, slot 9 = key 0). Pressing 1–0 calls `TrySetActive()` which only switches when the slot is filled. `RollDie()` instantiates `_diePrefabs[_activeDieIndex]`. Tracks `List<DieController>` in roll order; supplies obstacle data to the simulator. |
| `DieController.cs` | Per-die behaviour. Holds `List<DieFace> _faces` (per-prefab face layout). On `Roll()`: picks a face by weighted random, computes correction quaternion Q, pre-multiplies Q into every trajectory rotation step so the die tumbles through the corrected arc from frame 1. `SimRotations` kept original for obstacle tracking; only `correctedRotations` is used for visual playback. |
| `DieFace.cs` | Serializable struct: `Vector3 normal` (local-space), `int value`, `float weight`. Stored as `List<DieFace>` on each `DieController` prefab. Supports any number of faces and duplicate values (e.g. three 1s). `DefaultD6Faces()` provides the Western standard as a fallback. |
| `LevelClickHandler.cs` | Converts mouse clicks to world targets. Uses `Physics.Raycast` (not `Plane.Raycast`) against all layers except Dice so wall clicks project correctly to the roll plane. Clamps the target using `Mathf.Max(halfDiag, wallInsetMargin)` — this is critical: when `dieSize` is small, `halfDiag < wallInsetMargin`, and clamping only by `halfDiag` would produce targets that always fail the sim's in-bounds check. |
| `DieSimulator.cs` | Static class. Owns a persistent `LocalPhysicsMode.Physics3D` scene (`__DiceSimulation`). Runs up to `maxSimAttempts` random attempts (Pass 1), then re-runs the winner with all other dice as collider proxies (Pass 2). `SimResult.positions` is **null** when all attempts fail — callers must null-check. |
| `DieFaceMapper.cs` | Static utility (no longer called by DieController). Kept as a reference for the Western d6 convention. |
| `FrontWallGate.cs` | One-way passthrough for walls or ceiling. Solid collider starts disabled; a trigger zone opens it while dice are crossing, then closes once they're through. Configure `_outwardDir` to point away from the room interior: `(0,0,-1)` for front wall, `(0,1,0)` for ceiling. Trigger size is computed in world space via `lossyScale`, so it works correctly on scaled Cubes. |

## Architecture: Dice Rendering

Dice must always appear on top of room geometry (walls, floor) to stay readable, while still self-occluding correctly (back-face pips hidden behind the die body). This is achieved with **URP camera stacking**, not a RenderObjects pass.

### Setup

- **Main Camera** — culling mask excludes the `Dice` layer (layer 8, `0xFFFFFEFF`). Renders everything except dice.
- **DiceCamera** — child of Main Camera. Type: Overlay. `clearDepth = true`. Culling mask: Dice layer only (`0x100`).

`clearDepth = true` on the overlay camera discards the main camera's depth buffer before the overlay renders. Dice then draw into a fresh depth buffer, so they always composite on top of scene geometry. Self-occlusion (pip vs die body) is handled correctly by the fresh depth buffer without any special sorting tricks.

### Why not RenderObjects?

A `RenderObjects` ScriptableRendererFeature with `ZTest Always` was tried first. It caused two visible artifacts:
1. Back-face pips rendered in front of the die body (URP opaque pass sorts front-to-back; farther fragments win with `ZTest Always`).
2. Overlapping dice/wall areas appeared darker (ambient lighting accumulated twice).

Camera stacking avoids both issues. Do not revert to RenderObjects.

### Die layer assignment

`DieController.Initialize()` calls `SetLayerRecursively()` to put the die body and all pip children on the Dice layer. This must happen before the die is visible.

## Architecture: Rooms

Rooms are self-contained Prefabs (`Assets/Prefabs/Room/Room.prefab`). Each room contains:

| Child | Visible | Purpose |
|---|---|---|
| `Floor` | Yes | Sandy brown cube, top surface at `rollHeight` |
| `Wall_Back` | Yes | Dirt brown cube at far Z edge |
| `Wall_Left` / `Wall_Right` | Yes | Dirt brown cubes at side X edges |
| `Wall_Front` | No | Invisible cube at near Z edge; has `FrontWallGate` (`_outwardDir=(0,0,-1)`) |
| `Ceiling` | No | Invisible cube above room; has `FrontWallGate` (`_outwardDir=(0,1,0)`) |

Default room size: **15 × 10 units** (width × depth), walls 10 units tall. Controlled by `Room._width`, `Room._depth`, `Room._wallHeight`.

### Geometry is driven by Room.cs

`Room.ApplyDimensions()` is called from both `Awake()` (play mode) and `OnValidate()` (editor). It repositions and rescales all six child cubes to match `_width`, `_depth`, `_wallHeight`, and `DiceSettings.rollHeight`. Changing any of those fields in the Inspector immediately reshapes the room. The child transform formulas are:

```
floorCenter  = (0,  rollHeight - 0.05,         0)   scale = (width, 0.1,        depth)
wallCenterY  = rollHeight + wallHeight / 2
Wall_Back    = (0,  wallCenterY,      depth/2)       scale = (width, wallHeight, 0.1)
Wall_Front   = (0,  wallCenterY,     -depth/2)       scale = (width, wallHeight, 0.1)
Wall_Left    = (-width/2, wallCenterY, 0)             scale = (0.1,  wallHeight, depth)
Wall_Right   = ( width/2, wallCenterY, 0)             scale = (0.1,  wallHeight, depth)
Ceiling      = (0,  rollHeight + wallHeight,   0)    scale = (width, 0.1,        depth)
```

**Reinstantiation for procedural rooms**: instantiate a new `Room.prefab`, position it, and wire its `DoorSlot` children (future) to a `RoomManager` singleton. `DiceManager` finds the active `Room` automatically via `FindAnyObjectByType` on `Start()`. Only one Room should be active at a time.

**Materials**: `Assets/Materials/Room/FloorMaterial.mat` (sandy brown) and `WallMaterial.mat` (dirt brown). Replace with sprite-based materials as art is produced.

**Camera**: Position `(0, 12, -2)`, Rotation `(75, 0, 0)`, FOV 60°. This is a tuning starting point — the bottom frustum edge is designed to align with the near floor edge. Adjust `transform.position` and `fieldOfView` without touching `DiceSettings.rollHeight`; the dice system adapts automatically because `LevelClickHandler` raycasts against the roll plane and the sim uses `Room.GetBounds()`.

### Two-pass simulation detail

**Pass 1** finds an in-bounds trajectory ignoring other dice:
- Launch speed is derived from target depth: `sqrt(targetDepth × margin × g / (2 × loft))`, clamped to `[launchSpeed, launchSpeedMax]`.
- Loft and height boost are lerped by normalized target depth.
- Each attempt randomises angular velocity and start rotation. First attempt whose trajectory stays inside `wallInsetMargin` walls wins.

**Pass 2** re-runs the winning `linearVel`/`angVel`/`startRot` with obstacles:
- Settled dice → static `BoxCollider` proxies (no Rigidbody).
- In-flight dice → kinematic `Rigidbody` proxies stepped through their recorded `WorldPositions[]` in lock-step with the sim, indexed by `state.currentStep + simStep`.

### Face locking (important non-obvious behaviour)

Dice use UV-mapped FBX meshes — there are no procedural pips. Face locking is done by rotating the entire die body so the desired face is on top at the end of the sim.

`DieController.Roll()` computes `Q = Quaternion.FromToRotation(desiredNormal, simTopNormal)` and then **pre-multiplies Q into every rotation in the trajectory array** before playback starts:

```csharp
for (int k = 0; k < simRotations.Length; k++)
    correctedRotations[k] = simRotations[k] * Q;
transform.rotation = sim.startRotation * Q;  // corrected start rotation
```

This is critical: applying Q only to the final frame causes a visible snap at landing. Pre-multiplying into all steps makes the die tumble through the corrected arc from frame 1, with no discontinuity. `SimRotations` are kept at their original (uncorrected) values so kinematic obstacle proxies in Pass 2 use the true physics arc.

**`BestNormalForValue()`**: when a die has duplicate-value faces (e.g. three 1s), picks the face whose local normal is closest to `simTopNormal`. This minimises the magnitude of Q, making the trajectory look as natural as possible.

### Settle detection

A sim step is considered settled only when **both** velocity conditions are met **and** a face is within `settleAlignThreshold` degrees of horizontal (`IsFaceAligned()`). The alignment check prevents the sim from treating a die balanced on an edge as settled.

## Architecture: Die Configuration

All per-die configuration lives on the prefab — no ScriptableObjects for individual dice. This means each die type is self-contained and adding a new die type is purely additive (duplicate a prefab, swap the mesh/material, edit the face layout in Inspector).

### DieFace struct (`DieFace.cs`)

```csharp
[System.Serializable]
public struct DieFace
{
    public Vector3 normal;   // local-space face normal (axis-aligned for d6)
    public int     value;    // damage value when this face lands on top
    [Min(0f)]
    public float   weight;   // relative probability (0 = never, 2 = twice as likely)
}
```

`DieController._faces` is a `List<DieFace>` serialized on the prefab. It supports any number of faces and duplicate values. If left empty at runtime, `DefaultD6Faces()` fills in the Western d6 standard (`+Y=1, –Y=6, +Z=2, –Z=5, +X=3, –X=4`).

**Important — current die models use a non-standard +Z/–Z convention**: both `Die.prefab` and `Die_d6_111456.prefab` are built in Blender with `+Z=5, –Z=2` (opposite of the Western default). The face layouts stored on those prefabs already reflect this. If you add a new model built the same way in Blender, swap the +Z and –Z values in its face layout.

### Die selection (keys 1–0)

`DiceManager._diePrefabs` is a 10-element array. Slot 0 = key 1, slot 9 = key 0. Pressing a digit key calls `TrySetActive(index)`, which only switches when the slot is non-null. Currently:
- Key 1 → `Die.prefab` (standard d6, values 1–6)
- Key 2 → `Die_d6_111456.prefab` (three 1s, 4, 5, 6)

### Standalone materials

Die materials must be **standalone `.mat` files**, not the embedded materials inside the FBX. Embedded FBX materials lose their texture reference whenever the FBX is reimported (the binary bakes the old texture path). Standalone `.mat` files are immune.

Current materials:
- `Assets/Materials/Dice/Die_White.mat` — texture `Textures/Dice/Texture_Die_White.jpg`
- `Assets/Materials/Dice/Die_White_No_111456.mat` — texture `Textures/Dice/Texture_Die_White_No_111456.jpg`

When adding a new die: create a new `.mat` by duplicating an existing one, assign the correct texture, and assign that `.mat` to the new prefab's `MeshRenderer`. Do **not** use the FBX's embedded material directly.

## Architecture: Player

The player character is a kinematic Rigidbody pawn that moves with WASD and is **visually flat** (2.5D sprite convention) but occupies a 3D footprint for collision.

### Prefab structure (`Assets/Prefabs/Player/Player.prefab`)

| Node | Layer | Components | Purpose |
|---|---|---|---|
| Root (Player) | Player (9) | Rigidbody, BoxCollider, PlayerController | Physics root and movement controller |
| Sprite child | Dice (8) | Cube mesh scale (0.5, 0.01, 1.0), pink material | Flat visual; on Dice layer so DiceCamera overlay renders it above floor |

- **BoxCollider** on root: size `(0.4, 0.1, 0.8)`, center `(0, 0.05, -0.1)`. Bottom flush with floor (`rollHeight`).
- **Rigidbody**: `isKinematic=true`, `ContinuousSpeculative`, `Interpolate`, `FreezePositionY|FreezeRotation`.
- `Physics.IgnoreLayerCollision(Player, Dice)` is called in `Awake()` so dice never push the player.

### Movement model (`PlayerController.cs`)

Input is read from `UnityEngine.InputSystem.Keyboard.current` in `Update()` and consumed in `FixedUpdate()` via `MovePosition`.

**Three deceleration modes** (all tunable in Inspector — every field has a `[Tooltip]`):

| Mode | Field | When it fires |
|---|---|---|
| Per-axis | `_axisDecelerationX` / `_axisDecelerationZ` | One axis has no input while the other does (mid-turn) |
| General | `_deceleration` | Both axes have no input (full stop) |

Per-axis deceleration exists specifically to keep 90° turns tight: when the player releases forward while holding strafe, Z velocity bleeds off at `_axisDecelerationZ` while X continues to accelerate. Without it, the player carries forward momentum through the turn, producing a wide arc.

### Wall collision

**Do not rely on physics for wall stopping.** The player's collider shares the exact `rollHeight` Y base with the walls, making physics contacts ambiguous under `FreezePositionY`. Instead, `ClampToRoomBounds()` explicitly clamps each new position so the collider faces cannot exit `DiceManager.GetBoxBounds()`. The formula accounts for `_col.center` offset:

```
root.x ∈ [room.min.x + halfX - col.center.x,  room.max.x - halfX - col.center.x]
root.z ∈ [room.min.z + halfZ - col.center.z,  room.max.z - halfZ - col.center.z]
```

This is called after velocity integration, before `MovePosition`, every `FixedUpdate`.

### Layers

- `Dice` (layer 8) — all dice bodies, pip children, **and the player sprite child** (so the overlay camera renders it on top of room geometry).
- `Player` (layer 9) — player root only. Configured in `ProjectSettings/TagManager.asset`.

### Dash system (`PlayerController.cs`, `DashFeedback.cs`, `CameraShake.cs`)

The dash is a state machine with four phases driven by coroutine timing and `FixedUpdate` position.

**Phases**: `None → Delaying → Moving → PostDash → None`

- **Delaying** (`_dashDelay`): player frozen, no movement. Feedback and die removal happen at the transition into Moving.
- **Moving** (`_dashDuration`): coroutine lerps `_rb.MovePosition()` toward the target each `WaitForFixedUpdate`. **Critical**: `FixedUpdate` must `return` immediately during all non-None phases without calling `_rb.MovePosition()`. The execution order is FixedUpdate → physics → WaitForFixedUpdate. If FixedUpdate calls MovePosition, physics resolves it first, and the coroutine's call queued after physics gets overridden by the *next* FixedUpdate. The player would never move.
- **PostDash** (`_postDashDelay`): normal movement locked, but another dash can be initiated. This allows dash chaining.
- `IsDashing` is true only during Delaying + Moving (not PostDash).

**Dash target**: `DiceManager.GetOldestActiveDie()` — lowest `RollOrder`, XZ distance tie-break.

**Right-click removes die**: `LevelClickHandler` calls `DiceManager.GetNewestActiveDie()?.TriggerRemoval()`. The die turns red for `DiceSettings.dieRemovalDelay` seconds then is destroyed. It is immediately unregistered from `DiceManager` so it cannot be dashed to.

**Chaining**: if a new dash starts within `_chainWindow` seconds of the last one, `_lastDashTotalDamage` is added to the new dash's damage (fully compounding). All active `DashFeedback` objects have their lifetimes reset on a chain.

**Damage formula**: `damage = target.RolledValue + chainBonus`

**Visual feedback** (`DashFeedback.cs`): spawned per dash. Contains a `LineRenderer` (using `Sprites/Default` shader for alpha support) and a world-space `TextMeshPro` lying flat on the floor. Both sit on the `Dice` layer so the overlay camera renders them above the floor. Number rotation is `Quaternion.Euler(90, 0, 0)` — tested to be correct for this camera angle.

**Scaling** (multiplicative): `value = base × (1 + damage × multiplier)`, capped per element.

**Screen shake** (`CameraShake.cs`): attached to Main Camera. Applies a decaying random XZ offset only (no Y). Scaled with damage, capped.
