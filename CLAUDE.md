# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Dice Slice** — Unity 6 (6000.5.2f1), Universal Render Pipeline. A 2.5D isometric rogue-like dungeon crawling game in which the player can click to roll dice onto the board in specific locations. Their pawn can then dash to and between dice to deal damage to any enemy the pawn crosses. The damage dealt is based on the number on the die being dashed to. Dashes can be chained. Chaining a dash causes the current dash to inherit the damage of the previous dash. There are many different dice which have unique face and number configurations and unique effects. Instead of using regular physics, dice are pre-simulated and then kinematically launched to deterministically display results. This allows for potential future networking and allows the system to have more control over the dice.

## Working with the Editor

This project has UnityMCP configured, so you can interact with the Unity Editor directly via tools (`mcp__UnityMCP__*`). Always:
1. Use `read_console` after any script change to check for compilation errors before proceeding.
2. Poll `editor_state` resource (`mcpforunity://editor_state`) to confirm `isCompiling` is false before using new types.
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
| `DiceManager.cs` | Singleton orchestrator. Tracks `List<DieController>` in roll order; supplies obstacle data to the simulator. |
| `DieController.cs` | Per-die behaviour. Holds the recorded trajectory arrays (`WorldPositions[]`, `SimRotations[]`). Rigidbody is always kinematic. |
| `DieSimulator.cs` | Static class. Owns a persistent `LocalPhysicsMode.Physics3D` scene (`__DiceSimulation`). Runs up to `maxSimAttempts` random attempts (Pass 1), then re-runs the winner with all other dice as collider proxies (Pass 2). |
| `DieFaceMapper.cs` | Static class. Encodes the Western die convention (+Y=1, -Y=6, +Z=2, -Z=5, +X=3, -X=4). Computes a `PipRoot` rotation offset so the die always shows the desired value regardless of where the sim landed. |
| `DiePipBuilder.cs` | Procedurally builds pip sphere geometry under a `PipRoot` child. Pips are visual only — `SphereCollider` is destroyed immediately after creation. |
| `RollBoundsBox.cs` | `[ExecuteAlways]`. Auto-fits wall colliders to camera frustum by raycasting viewport corners onto the roll plane. Adds `FrontWallGate` to the front wall automatically. |
| `FrontWallGate.cs` | Keeps the front wall collider disabled while any die is crossing it (trigger-counted), then enables it once all dice are inside. |

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
