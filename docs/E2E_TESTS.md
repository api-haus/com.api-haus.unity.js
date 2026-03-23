# unity.js E2E Test Outline

All tests use `SceneFixture` + TypeScript fixtures + `EnterPlayMode/ExitPlayMode`.
No mocking except the single TS fixture input layer per test.
Assertions derived from first principles.

## Notation

- **Fixture**: `.ts` file in `Assets/StreamingAssets/unity.js/tests/`
- **Input**: what the fixture hardcodes (speeds, positions, values)
- **Output**: what C# reads from ECS or JS globals after N frames
- **Assertion**: mathematical relationship between input and output

---

## 1. Component Lifecycle (`ComponentLifecycleE2ETests.cs`)

**Fixture**: `tests/components/lifecycle_probe.ts` (DONE)

Counts start/update/fixedUpdate/lateUpdate/onDestroy invocations via `_e2e_lifecycle[eid]`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `Start_CalledExactlyOnce` | Spawn 1 entity | `startCount` | `== 1` |
| `Update_CalledEveryFrame` | Run N frames after reset | `updateCount` | `== N` |
| `Update_DeltaTimePositive` | Run frames | `lastDt` | `> 0` |
| `OnDestroy_CalledOnEntityDestruction` | Destroy entity | `destroyCount` | `== 1` |
| `OnDestroy_NotCalledBeforeDestruction` | Before destroy | `destroyCount` | `== 0` |
| `MultipleEntities_IndependentLifecycles` | Spawn 2 entities | each entity's `startCount` | both `== 1`, different `eid` |
| `PropertyOverrides_AppliedFromJson` | Spawn with `{"speed":42}` | fixture reads `this.speed` | `== 42` |

**Status**: First 3 tests DONE and passing.

---

## 2. System Execution (`SystemExecutionE2ETests.cs`)

**Fixture**: `tests/systems/execution_probe.ts`

System that increments `_e2e_sys.updateCount` each frame, records `state.deltaTime`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `System_AutoDiscovered` | Place `.ts` in `systems/` | `_e2e_sys.updateCount` | `> 0` after frames |
| `System_ReceivesDeltaTime` | Run frames | `_e2e_sys.lastDt` | `> 0 && < 1` |
| `System_ReceivesElapsedTime` | Run frames | `_e2e_sys.lastElapsed` | `> 0` |
| `System_RunsEveryFrame` | Reset counter, run N frames | `_e2e_sys.updateCount` | `== N` |

---

## 3. ECS Queries (`QueryPipelineE2ETests.cs`)

**Fixture**: `tests/systems/query_probe.ts`

System that queries `LocalTransform` entities, stores results in `_e2e_query`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `Query_WithAll_MatchesEntities` | Spawn 3 entities with LocalTransform | `_e2e_query.matchCount` | `>= 3` |
| `Query_WriteBack_PersistsToECS` | System moves entities by `(1,0,0)*dt` | `LocalTransform.Position.x` after N frames | `> 0` (moved from origin) |
| `Query_AtModuleScope_StableAcrossFrames` | Run 10 frames | `_e2e_query.matchCount` frame 1 vs frame 10 | same count (query not rebuilt) |

**Fixture**: `tests/systems/query_filter_probe.ts`

System with `withAll(A).withNone(B)`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `Query_WithNone_ExcludesTaggedEntities` | 2 entities: one with tag, one without | `_e2e_qf.matchCount` | `== 1` (only untagged) |

---

## 4. Component Access (`ComponentAccessE2ETests.cs`)

**Fixture**: `tests/components/component_access_probe.ts`

Component that uses `ecs.get()`, `ecs.add()`, `ecs.has()`, `ecs.remove()` for JS-defined components in `start()`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `EcsDefine_CreatesComponent` | `ecs.define("TestHP", {current:100})` | `_e2e_comp.defined` | `== true` |
| `EcsAdd_AttachesToEntity` | `ecs.add(eid, "TestHP", {current:50})` | `_e2e_comp.hasBefore` / `hasAfter` | `false` / `true` |
| `EcsGet_ReturnsData` | Add then get | `_e2e_comp.getCurrent` | `== 50` |
| `EcsRemove_DetachesFromEntity` | Add then remove | `_e2e_comp.hasAfterRemove` | `== false` |
| `EcsHas_ChecksCSharpComponents` | Entity with `LocalTransform` | `_e2e_comp.hasTransform` | `== true` |

---

## 5. Entity Operations (`EntityOperationsE2ETests.cs`)

**Fixture**: `tests/systems/entity_ops_probe.ts`

System that creates and destroys entities via `entities.create()` / `entities.destroy()`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `Create_ReturnsValidId` | `entities.create()` | `_e2e_ent.createdId` | `> 0` |
| `Create_WithPosition_SetsTransform` | `entities.create(float3(5,10,15))` | `LocalTransform.Position` of created entity | `== (5,10,15)` |
| `Destroy_RemovesEntity` | Create then destroy | entity exists check | `false` after ECB playback |
| `AddScript_AttachesComponent` | `entities.addScript(eid, "tests/components/lifecycle_probe")` | script fulfillment | `stateRef >= 0` |

---

## 6. Math Bridge (`MathBridgeE2ETests.cs`)

**Fixture**: `tests/systems/math_probe.ts`

System that evaluates math operations with known inputs, stores results in `_e2e_math`.

| Test | Input | Output | Assertion (first principles) |
|------|-------|--------|-----------|
| `Dot_Orthogonal` | `dot(float3(1,0,0), float3(0,1,0))` | `_e2e_math.dot_ortho` | `== 0` (definition of orthogonal) |
| `Dot_Parallel` | `dot(float3(3,0,0), float3(2,0,0))` | `_e2e_math.dot_para` | `== 6` (|a|*|b|*cos(0)) |
| `Length_Pythagorean` | `length(float3(3,4,0))` | `_e2e_math.len_345` | `== 5` (3-4-5 triangle) |
| `Normalize_UnitLength` | `normalize(float3(0,0,5))` | `_e2e_math.norm_z` | `== float3(0,0,1)` and `length == 1` |
| `Lerp_Boundaries` | `lerp(0,10,0)`, `lerp(0,10,1)`, `lerp(0,10,0.5)` | 3 values | `0`, `10`, `5` |
| `Cross_RightHand` | `cross(float3(1,0,0), float3(0,1,0))` | `_e2e_math.cross_xy` | `== float3(0,0,1)` (right-hand rule) |
| `Cross_Anticommutative` | `cross(y, x)` | `_e2e_math.cross_yx` | `== float3(0,0,-1)` |
| `Sin_KnownValues` | `sin(0)`, `sin(PI/2)` | 2 values | `0`, `1` |
| `Clamp_Boundaries` | `clamp(15,0,10)`, `clamp(-5,0,10)`, `clamp(5,0,10)` | 3 values | `10`, `0`, `5` |
| `Distance_Pythagorean` | `distance(float3(0,0,0), float3(3,4,0))` | `_e2e_math.dist` | `== 5` |
| `Float2_Constructors` | `float2(3,4)`, `float2(5)`, `float2.zero` | `.x`, `.y` | `(3,4)`, `(5,5)`, `(0,0)` |
| `Float3_Swizzle` | `float3(1,2,3).xz` | `_e2e_math.swiz` | `== float2(1,3)` |

---

## 7. Color Bridge (`ColorBridgeE2ETests.cs`)

**Fixture**: `tests/systems/color_probe.ts`

System that exercises color conversion functions with known values.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `HsvToRgb_Red` | `hsvToRgb(0, 1, 1)` | `_e2e_color.red` | `== float3(1,0,0)` |
| `HsvToRgb_Green` | `hsvToRgb(120/360, 1, 1)` | `_e2e_color.green` | `== float3(0,1,0)` |
| `HsvToRgb_Blue` | `hsvToRgb(240/360, 1, 1)` | `_e2e_color.blue` | `== float3(0,0,1)` |
| `RgbHsv_Roundtrip` | `rgbToHsv(hsvToRgb(h,s,v))` | roundtrip values | `≈ (h,s,v)` within tolerance |
| `OklabRgb_Roundtrip` | `rgbToOklab(oklabToRgb(L,a,b))` | roundtrip values | `≈ (L,a,b)` within tolerance |

---

## 8. Logging (`LogBridgeE2ETests.cs`)

**Fixture**: `tests/components/log_probe.ts`

Component that calls `log.info("PROBE:hello")` in `start()`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `LogInfo_AppearsInUnityLog` | `log.info("PROBE:e2e_test_marker")` | `LogAssert.Expect(LogType.Log, ...)` | Message contains marker |
| `LogWarning_AppearsAsWarning` | `log.warning("PROBE:warn_marker")` | `LogAssert.Expect(LogType.Warning, ...)` | Warning with marker |
| `LogError_AppearsAsError` | `log.error("PROBE:error_marker")` | `LogAssert.Expect(LogType.Error, ...)` | Error with marker |

---

## 9. System Info (`SystemInfoE2ETests.cs`)

**Fixture**: `tests/systems/sysinfo_probe.ts`

System that reads `system.deltaTime()`, `system.time()`, `system.random()`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `DeltaTime_MatchesFrameTiming` | Run frames | `_e2e_sys.dt` | `> 0 && < 1` |
| `Time_Increases` | Run 10 frames | `_e2e_sys.time_early` vs `time_late` | `late > early` |
| `Random_InZeroOneRange` | Call 100 times | `_e2e_sys.randomMin`, `randomMax` | `min >= 0 && max < 1` |
| `RandomInt_InRequestedRange` | `randomInt(5, 10)` 100 times | `_e2e_sys.intMin`, `intMax` | `min >= 5 && max <= 10` |

---

## 10. Input Bridge (`InputBridgeE2ETests.cs`)

**Fixture**: `tests/components/input_probe.ts`

Component that calls `input.readValue("Move")` — returns null-safe default when no input device.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `ReadValue_NullSafe_WhenNoDevice` | No input device connected | `_e2e_input.moveValue` | `== null` or `0` (no crash) |

---

## 11. Spatial Queries (`SpatialQueryE2ETests.cs`)

**Fixture**: `tests/components/spatial_probe.ts`

Component that uses `spatial.query()` and `spatial.trigger()`.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `SphereQuery_FindsNearbyEntities` | 2 entities at distance 1, query radius 2 | `_e2e_spatial.queryCount` | `>= 1` |
| `SphereQuery_MissesDistantEntities` | 2 entities at distance 100, query radius 1 | `_e2e_spatial.queryCount` | `== 0` |
| `Trigger_EnterFires` | Move entity into trigger radius | `_e2e_spatial.enterFired` | `== true` |

---

## 12. Hot Reload (`HotReloadE2ETests.cs`)

**Fixture**: `tests/components/hot_reload_probe.ts` (mutated during test)

Component that returns a version number. Test mutates the file and verifies new version loads.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `Reload_UpdatesBehavior` | Mutate fixture → recompile | `_e2e_hot.version` | `== 2` (was 1) |
| `Reload_PreservesEntityId` | Reload script | `this.entity` | same eid before and after |
| `Reload_NoExceptions` | Reload 3 times rapidly | `vm.CapturedExceptions` | empty |

---

## 13. Domain Reload (`DomainReloadE2ETests.cs`)

No fixture — tests play mode cycling itself.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `TwoCycles_NoErrors` | Enter→spawn→verify→exit→enter→spawn→verify→exit | `AllFulfilled()` both times | `true` |
| `NoTDZErrors_AfterReload` | Enter→exit→enter | `vm.CapturedExceptions` | no "not initialized" errors |

---

## 14. Tick Groups (`TickGroupE2ETests.cs`)

**Fixture**: `tests/components/tick_group_probe.ts`

Component with `fixedUpdate()` and `lateUpdate()` that increment separate counters.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `FixedUpdate_CalledAtFixedRate` | Run for 0.5s real time | `fixedUpdateCount` | `> 0` |
| `LateUpdate_CalledAfterTransforms` | Run frames | `lateUpdateCount` | `> 0` |
| `UpdateOrder_UpdateBeforeLateUpdate` | Record frame-local ordinals | `updateOrdinal < lateUpdateOrdinal` | `true` |

---

## 15. Module Imports (`ModuleImportE2ETests.cs`)

**Fixture**: `tests/systems/import_probe.ts` + `tests/systems/import_helper.ts`

System that imports a helper module and uses its exported function.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `ESModule_ImportWorks` | `import { add } from './import_helper'` | `_e2e_import.result` | `== add(2,3) == 5` |
| `BuiltinModule_EcsImportWorks` | `import { query } from 'unity.js/ecs'` | `_e2e_import.hasQuery` | `== true` |
| `BuiltinModule_MathImportWorks` | `import { sin } from 'unity.js/math'` | `_e2e_import.hasSin` | `== true` |

---

## 16. Multi-Script Entities (`MultiScriptE2ETests.cs`)

**Fixture**: `tests/components/multi_a.ts` + `tests/components/multi_b.ts`

Two components on one entity, each with independent lifecycle tracking.

| Test | Input | Output | Assertion |
|------|-------|--------|-----------|
| `TwoComponents_BothInitialized` | Spawn with `[multi_a, multi_b]` | both `startCount` | `== 1` each |
| `TwoComponents_IndependentUpdate` | Run N frames | both `updateCount` | `== N` each |
| `TwoComponents_BothDestroyed` | Destroy entity | both `destroyCount` | `== 1` each |

---

---

# Part B: Gameplay Lighthouse Tests

These are the real landmines. They exercise multiple features interacting together the way actual gameplay does. If any of these break, something real is broken — not an isolated bridge function, but the pipeline that ships gameplay.

Feature probe tests (Part A) tell you *which* function broke. Lighthouse tests (Part B) tell you *gameplay is broken* — which is what matters.

---

## G1. Wandering Slimes (`WanderingSlimesE2ETests.cs`)

**Purpose**: Proves a Component script can read and write `LocalTransform.Position` over sustained execution. This is the most fundamental gameplay operation — if entities can't move, nothing works.

**Fixtures**:
- `tests/components/e2e_wanderer.ts` — simplified `SlimeWander` with deterministic behavior (no randomness)

**Fixture design** (`e2e_wanderer.ts`):
```
- speed = 3 (units/sec)
- No pauses (pauseTimeMin = pauseTimeMax = 0)
- 5 hardcoded waypoints in a loop: (2,0,0) → (0,0,2) → (-2,0,0) → (0,0,-2) → (2,0,0)
- Advances to next waypoint when within 0.1 units
- Records waypointIndex in global for assertion
```

No randomness — waypoints are deterministic so we can predict exact behavior.

**Scene setup**:
- 10 entities, each with `e2e_wanderer` component
- All spawned at origin `(0, 0, 0)`
- No physics, no spatial — pure component movement test

| Test | Duration | Input | Output | Assertion |
|------|----------|-------|--------|-----------|
| `AllSlimes_Move` | 5 sec | 10 entities at origin | `LocalTransform.Position` per entity | Every entity moved `> 1.0` unit from origin |
| `AllSlimes_ReachWaypoints` | 5 sec | speed=3, waypoints 2 units apart | `_e2e_wander[eid].waypointIndex` | Every entity reached waypoint `>= 3` (at least 3 switches at speed=3, distance=2 → ~0.67s per leg, 5s total → ~7 legs) |
| `Positions_Diverge` | 5 sec | 10 entities, same start | pairwise distances | Not all at same position (entities start diverging after first waypoint due to frame-timing jitter on `< 0.1` check) |

**Analytical basis**:
- speed = 3 u/s, waypoint distance = 2 u → time per leg = 2/3 s ≈ 0.67s
- 5 seconds / 0.67s per leg ≈ 7.5 legs → `waypointIndex >= 5` (conservative, accounting for arrival threshold)
- Total displacement from origin: at any moment the entity is at most 2 units from origin, but the *path length* proves sustained movement

---

## G2. Slimes + Spatial + Dynamic Bodies (`SlimeSpatialE2ETests.cs`)

**Purpose**: Proves the full interaction pipeline: Component lifecycle + `LocalTransform` read/write + `spatial.trigger()` enter/exit callbacks + `PhysicsVelocity` write + multi-entity interaction. This is the "does the game work" test.

**Fixtures**:
- `tests/components/e2e_wanderer.ts` — reused from G1
- `tests/components/e2e_spatial_goo.ts` — simplified `SlimeSpatial` that applies velocity to nearby dynamic bodies
- `tests/components/e2e_body_tracker.ts` — Component that records cumulative displacement for assertion

**Fixture design** (`e2e_spatial_goo.ts`):
```
- Reuses same API as slime_spatial.ts but simplified:
  - Creates sphere trigger with radius 3.0 around entity
  - On fixedUpdate: for each overlapping entity, adds velocity toward slime center
  - strength = 5, no damping/buoyancy complexity
- Records overlap count in global for assertion
```

**Fixture design** (`e2e_body_tracker.ts`):
```
- Reads own LocalTransform.Position each update
- Accumulates total displacement: totalDist += distance(pos, prevPos)
- Stores in _e2e_bodies[eid] = { totalDist, frameCount }
```

**Scene setup**:
- 5 wandering slimes at origin, each with `[e2e_wanderer, e2e_spatial_goo]`
- 20 body entities scattered in a 4x5 grid at positions `(x*2, 0, z*2)` for x in [-2..2], z in [-2..2] (subset), each with:
  - `e2e_body_tracker` component
  - `LocalTransform` at grid position
  - Spatial agent registered with tag `"dynamic_bodies"` (done by fixture's `start()`)

| Test | Duration | Input | Output | Assertion |
|------|----------|-------|--------|-----------|
| `Bodies_AreInfluenced` | 5 sec | 5 slimes + 20 bodies | `_e2e_bodies[eid].totalDist` per body | At least 50% of bodies moved `> 0.5` units total |
| `Slimes_StillMove` | 5 sec | 5 slimes with spatial goo | slime positions | All slimes moved `> 1.0` unit from origin (spatial doesn't break wandering) |
| `Triggers_Fire` | 5 sec | Slimes move through body grid | `_e2e_goo[slimeEid].overlapCount` | At least 1 slime had `> 0` overlaps |

**Analytical basis**:
- Slimes move at 3 u/s through a 4x4 unit grid of bodies
- Bodies within radius 3.0 of a slime get velocity applied
- At strength=5, a body 1 unit from slime center gets `5 * 1.0 * dt` velocity per fixedUpdate
- Over 5 seconds of proximity, even brief overlaps produce measurable displacement
- "50% of bodies moved > 0.5 units" is conservative — spatial trigger enter/exit must work, fixedUpdate must fire, PhysicsVelocity must be writable

---

## G3. Character Input + Movement (`CharacterMovementE2ETests.cs`)

**Purpose**: Proves the input bridge → component → transform pipeline. A component reads input, applies movement. Since we can't simulate real input devices in batch mode, we use a fixture that falls back to constant forward movement when no input device is available.

**Fixture**: `tests/components/e2e_character.ts`

```
- Reads input.readValue("Move") — returns null when no device
- If null: uses constant forward vector (0, 0, 1) as fallback
- Applies: position += direction * speed * dt
- speed = 4 u/s
- Records: _e2e_char[eid] = { frameCount, totalDist, inputWasNull }
```

**Scene setup**:
- 1 entity at origin with `e2e_character` component

| Test | Duration | Input | Output | Assertion |
|------|----------|-------|--------|-----------|
| `Character_MovesForward` | 3 sec | 1 entity, speed=4, constant forward | `LocalTransform.Position.z` | `> 10.0` (4 u/s * 3s = 12, with tolerance) |
| `Input_NullSafe` | 3 sec | No input device | `_e2e_char.inputWasNull` | `== true` (graceful fallback, no crash) |
| `Character_TotalDistance` | 3 sec | speed=4, constant dir | `_e2e_char.totalDist` | `≈ 12.0 ± 2.0` (speed * time) |

**Analytical basis**:
- Constant direction `(0,0,1)`, speed=4, duration=3s → displacement = `4 * 3 = 12` units on Z axis
- Tolerance: ±2 units for frame timing variance

---

## Implementation Order (Updated)

Tests will be developed one at a time, in this order:

**Priority 1 — Gameplay lighthouses (prove the pipeline works):**

| # | Test Class | Fixture(s) | What It Proves |
|---|-----------|------------|---------------|
| 1 | ComponentLifecycleE2ETests | `lifecycle_probe.ts` | **DONE** — start/update/destroy work |
| 2 | WanderingSlimesE2ETests | `e2e_wanderer.ts` | Components can move entities over time |
| 3 | SlimeSpatialE2ETests | `e2e_wanderer.ts` + `e2e_spatial_goo.ts` + `e2e_body_tracker.ts` | Multi-component + spatial + physics interaction |
| 4 | CharacterMovementE2ETests | `e2e_character.ts` | Input bridge + transform pipeline |

**Priority 2 — Feature probes (isolate which bridge broke):**

| # | Test Class | Fixture |
|---|-----------|---------|
| 5 | SystemExecutionE2ETests | `execution_probe.ts` |
| 6 | MathBridgeE2ETests | `math_probe.ts` |
| 7 | QueryPipelineE2ETests | `query_probe.ts` |
| 8 | ComponentAccessE2ETests | `component_access_probe.ts` |
| 9 | EntityOperationsE2ETests | `entity_ops_probe.ts` |
| 10 | ColorBridgeE2ETests | `color_probe.ts` |
| 11 | SystemInfoE2ETests | `sysinfo_probe.ts` |
| 12 | LogBridgeE2ETests | `log_probe.ts` |
| 13 | TickGroupE2ETests | `tick_group_probe.ts` |
| 14 | MultiScriptE2ETests | `multi_a.ts`, `multi_b.ts` |
| 15 | ModuleImportE2ETests | `import_probe.ts` |
| 16 | InputBridgeE2ETests | `input_probe.ts` |
| 17 | SpatialQueryE2ETests | `spatial_probe.ts` |

**Priority 3 — Stability stress tests:**

| # | Test Class | Fixture |
|---|-----------|---------|
| 18 | HotReloadE2ETests | `hot_reload_probe.ts` |
| 19 | DomainReloadE2ETests | (none) |

Tests will be developed one at a time, in this order (dependency-driven):

| # | Test Class | Depends On | Fixture |
|---|-----------|------------|---------|
| 1 | ComponentLifecycleE2ETests | — | `lifecycle_probe.ts` (DONE) |
| 2 | SystemExecutionE2ETests | — | `execution_probe.ts` |
| 3 | MathBridgeE2ETests | Systems working | `math_probe.ts` |
| 4 | QueryPipelineE2ETests | Systems + Math | `query_probe.ts` |
| 5 | ComponentAccessE2ETests | Lifecycle working | `component_access_probe.ts` |
| 6 | EntityOperationsE2ETests | Systems working | `entity_ops_probe.ts` |
| 7 | ColorBridgeE2ETests | Systems working | `color_probe.ts` |
| 8 | SystemInfoE2ETests | Systems working | `sysinfo_probe.ts` |
| 9 | LogBridgeE2ETests | Lifecycle working | `log_probe.ts` |
| 10 | TickGroupE2ETests | Lifecycle working | `tick_group_probe.ts` |
| 11 | MultiScriptE2ETests | Lifecycle working | `multi_a.ts`, `multi_b.ts` |
| 12 | ModuleImportE2ETests | Systems working | `import_probe.ts` |
| 13 | InputBridgeE2ETests | Lifecycle working | `input_probe.ts` |
| 14 | SpatialQueryE2ETests | Lifecycle + Queries | `spatial_probe.ts` |
| 15 | HotReloadE2ETests | Everything | `hot_reload_probe.ts` |
| 16 | DomainReloadE2ETests | Everything | (none) |

---

## Fixtures Directory Structure

```
Assets/StreamingAssets/unity.js/tests/
  components/
    lifecycle_probe.ts          (DONE)
    component_access_probe.ts
    log_probe.ts
    input_probe.ts
    spatial_probe.ts
    hot_reload_probe.ts
    tick_group_probe.ts
    multi_a.ts
    multi_b.ts
  systems/
    execution_probe.ts
    math_probe.ts
    query_probe.ts
    query_filter_probe.ts
    entity_ops_probe.ts
    color_probe.ts
    sysinfo_probe.ts
    import_probe.ts
    import_helper.ts
```

## Test Class Location

All in `Packages/com.api-haus.unity.js/Runtime/JsECS/Tests/EditMode/`:

```
SceneFixture.cs                     (DONE)
ComponentLifecycleE2ETests.cs       (DONE)
SystemExecutionE2ETests.cs
MathBridgeE2ETests.cs
QueryPipelineE2ETests.cs
ComponentAccessE2ETests.cs
EntityOperationsE2ETests.cs
ColorBridgeE2ETests.cs
SystemInfoE2ETests.cs
LogBridgeE2ETests.cs
TickGroupE2ETests.cs
MultiScriptE2ETests.cs
ModuleImportE2ETests.cs
InputBridgeE2ETests.cs
SpatialQueryE2ETests.cs
HotReloadE2ETests.cs
DomainReloadE2ETests.cs
```
