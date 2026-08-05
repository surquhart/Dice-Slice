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
  → DiceManager.RollDie(worldTarget)
      → Instantiate Die prefab (DieController + DiePipBuilder)
      → DieController.Roll()
          → DiceManager.GetSettledDicePoses()     // static obstacles
          → DiceManager.GetRollingDiceStates()    // kinematic obstacles (in-flight dice)
          → DieSimulator.Run()                    // two-pass simulation
          → DieFaceMapper.PipRemapRotation()      // lock pip face BEFORE playback starts
          → PlaybackTrajectory coroutine          // WaitForFixedUpdate per step
          → OnRollComplete.Invoke(value)
```

### Key files

| File | Role |
|---|---|
| `Assets/Scripts/Dice/DiceSettings.cs` | `ScriptableObject` — single source of truth for all tuning (launch speed, physics, settle thresholds). Asset lives at `Assets/Settings/Dice/DiceSettings.asset`. |
| `Assets/Scripts/Room/Room.cs` | `MonoBehaviour` on the Room prefab root. Stores `_width`, `_depth`, `_wallHeight`. `GetBounds()` returns the play-area `Bounds` consumed by `DiceManager` and `DieSimulator`. Changing any dimension field in the Inspector immediately reshapes all child geometry via `OnValidate`. |
| `DiceManager.cs` | Singleton orchestrator. Tracks `List<DieController>` in roll order; supplies obstacle data to the simulator. Finds the active `Room` in `Start()` to provide bounds. |
| `DieController.cs` | Per-die behaviour. Holds the recorded trajectory arrays (`WorldPositions[]`, `SimRotations[]`). Rigidbody is always kinematic. When `DieSimulator.Run()` returns `success=false`, `sim.positions` is null — code uses `?? System.Array.Empty<Vector3>()` to avoid a NullReferenceException. |
| `LevelClickHandler.cs` | Converts mouse clicks to world targets. Uses `Physics.Raycast` (not `Plane.Raycast`) against all layers except Dice so wall clicks project correctly to the roll plane. Clamps the target using `Mathf.Max(halfDiag, wallInsetMargin)` — this is critical: when `dieSize` is small, `halfDiag < wallInsetMargin`, and clamping only by `halfDiag` would produce targets that always fail the sim's in-bounds check. |
| `DieSimulator.cs` | Static class. Owns a persistent `LocalPhysicsMode.Physics3D` scene (`__DiceSimulation`). Runs up to `maxSimAttempts` random attempts (Pass 1), then re-runs the winner with all other dice as collider proxies (Pass 2). `SimResult.positions` is **null** when all attempts fail — callers must null-check. |
| `DieFaceMapper.cs` | Static class. Encodes the Western die convention (+Y=1, -Y=6, +Z=2, -Z=5, +X=3, -X=4). Computes a `PipRoot` rotation offset so the die always shows the desired value regardless of where the sim landed. |
| `DiePipBuilder.cs` | Procedurally builds pip sphere geometry under a `PipRoot` child. Pips are visual only — `SphereCollider` is destroyed immediately after creation. |
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

The `PipRoot` rotation offset is applied **before** the playback coroutine starts. The die mesh rotates freely during playback, but the pips are counter-rotated relative to the die body so the correct value faces up at the end. Editing `DieFaceMapper` or `DieController.Roll()` must preserve this ordering.

### Settle detection

A sim step is considered settled only when **both** velocity conditions are met **and** a face is within `settleAlignThreshold` degrees of horizontal (`IsFaceAligned()`). The alignment check prevents the sim from treating a die balanced on an edge as settled.

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
