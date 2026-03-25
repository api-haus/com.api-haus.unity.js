# unity.js Architectural Refactoring Plan

## Context

### The Problem
Every conversation about unity.js follows the same cycle: **plan → implement → break everything → multi-hour fix marathon**. Analysis of 10 conversations and 100+ git commits reveals this isn't bad luck — it's architectural fragility. The package has grown organically into a tightly-coupled monolith where touching any subsystem creates ripple failures across the entire system.

### Root Cause Analysis (from 10 conversation histories)

**Conversation-by-conversation failure catalog:**

| # | Feature | What Broke | Fix Cycles | Root Cause |
|---|---------|-----------|------------|------------|
| 1 | Query batch iterators | `_nativeQuery()` returns 0 entities | 30+ | `EntityManager.CreateEntityQuery()` inside P/Invoke callback creates broken queries |
| 2 | System query init after reloads | TDZ `ReferenceError: charQuery not initialized` | 5+ | Module cache + HasScript guard skips re-evaluation; bridges not ready at module scope |
| 3 | Lua→JS migration (input) | `TypeError: not a function` at runtime | 3+ | Tests passed (shortcuts) but real play mode lifecycle failed |
| 4 | TDZ errors on play mode entry | `charQuery is not initialized` | 1 (user caught) | `TscWatchService.CleanOutDir()` deletes entire TscBuild/ while systems loading |
| 5 | Duplicate module guard | Random TDZ errors after script edits | 4+ | `LoadScriptAsModule` called per-entity with same filename → duplicate JSModuleDef |
| 6 | Test migration EditMode→PlayMode | Plan only | N/A | Acknowledged test organization was wrong |
| 7 | AutoScope RefRW queries | Interrupted | N/A | Stopped before implementation |
| 8 | Vector swizzles | Plan only | N/A | Complex generated code surface area |
| 9 | TypeScript migration | Tool rejection mid-edit | 2 | Read-before-write violation |
| 10 | JsEntityRegistry API reduction | Plan only | N/A | Identified test false confidence from shortcuts |

**Three systemic causes emerge:**

1. **God objects with hidden coupling** — JsECSBridge (600+ lines, 5 responsibilities), JsSystemRunner (discovery + codegen + tick dispatch), JsRuntimeManager (singleton doing everything). Any change ripples.

2. **Context pollution for AI** — The package is too interconnected for an AI to hold in context. Claude reads one file, makes a reasonable change, but breaks an assumption in an unread file. The architecture doesn't have clear boundaries that would make safe changes obvious.

3. **Tests that prove nothing** — Unit tests using `EvalVoid()` and `RegisterImmediate()` pass while the real play-mode lifecycle is broken. The gap between "bridge works when called directly" and "feature works in play mode" is exactly where every bug hides.

### What "Almost Working" Costs

The user's quote captures it: **"almost working code is worse than a solution that's not working at all."** Almost-working code:
- Passes unit tests but fails in play mode
- Works on first play mode entry but breaks on domain reload
- Functions after fresh compile but breaks after hot reload
- Runs with one entity but fails with multiple entities sharing a script

Each "almost" costs 2-8 hours of debugging across conversations.

---

## Feature Catalog by Layer

### Layer 1: User-Facing Features (what TS/JS developers use)

| Feature | JS API | Implementation |
|---------|--------|----------------|
| **Component scripting** | `class MyComp extends Component { start(), update(dt), fixedUpdate(dt), lateUpdate(dt), onDestroy() }` | `JsEntitiesBridge.cs`, ComponentGlue JS |
| **System scripting** | `export function onUpdate(state) {}` in `systems/*.ts` | `JsSystemRunner.cs` |
| **ECS queries** | `ecs.query().withAll(X).withNone(Y).build()` → iterable `[eid, ...comps]` | `JsQueryBridge.cs`, QueryBuilder JS |
| **Component access** | `ecs.get(accessor, eid)`, `ecs.add(eid, comp)`, `ecs.has()`, `ecs.remove()` | `JsEntitiesBridge.cs` |
| **Entity ops** | `entities.create(pos)`, `entities.destroy(eid)`, `entities.addScript()` | `JsEntitiesBridge.cs` |
| **Math** | `float2/3/4` constructors, swizzles, `math.sin/cos/lerp/dot/normalize/cross` etc. | `JsMathCompiled.cs` |
| **Colors** | `colors.hsvToRgb()`, `colors.oklabToRgb()`, `colors.rgbToHsv()`, `colors.rgbToOklab()` | `JsColorsBridge.cs` |
| **Logging** | `log.info()`, `log.warn()`, `log.error()`, `log.debug()`, `log.trace()` | `JsLogBridge.cs` |
| **Input** | `input.readValue(action)`, `input.wasPressed()`, `input.isHeld()`, `input.wasReleased()` | `JsInputBridge.cs` |
| **Drawing** | `draw.line()`, `draw.wireSphere()`, `draw.solidBox()`, `draw.label2d()`, etc. | `JsDrawBridge.cs` |
| **Spatial queries** | `spatial.query(tag, shape)`, `spatial.trigger(eid, tag, shape).on('enter', cb)` | `JsSpatialBridge.cs`, `JsSpatialTriggerBridge.cs` |
| **System info** | `system.deltaTime()`, `system.time()`, `system.random()` | `JsSystemBridge.cs` |
| **Type system** | `float2/3/4`, `entity`, `ComponentAccessor<T>`, `QueryBuilder` | `globals.d.ts`, `modules.d.ts` |
| **Authoring** | `JsComponentAuthoring` MonoBehaviour on GameObjects (editor) | `JsComponentAuthoring.cs`, `JsScriptBufferAuthoring.cs` |
| **Hot reload** | Edit `.ts` file → auto-recompile → live update in play mode | `TscCompiler`, `JsHotReloadSystem` |

### Layer 2: Architectural Features (internal systems)

| Feature | Responsibility | Key Files |
|---------|---------------|-----------|
| **VM lifecycle** | Create/dispose QuickJS runtime+context, singleton management | `JsRuntimeManager.cs` |
| **Module loading** | ES module resolution, `import` normalization, cache with `?v=N` invalidation | `JsModuleLoader.cs`, `JsBuiltinModules.cs` |
| **Script discovery** | Scan `systems/` and `components/` dirs, priority-ordered search paths | `JsScriptSearchPaths.cs`, `JsScriptSourceRegistry.cs` |
| **Bridge registration** | Register C# types/enums/functions as JS globals, `[JsBridge]` codegen | `JsComponentRegistry.cs`, `JsBridgeState.cs` |
| **Entity ID registry** | Persistent `int` IDs for entities (survives structural changes), bidirectional maps | `JsEntityRegistry.cs` |
| **Burst context** | SharedStatic data for Burst-compiled entity lookups, refreshed each frame | `JsECSBridge.cs` (UpdateBurstContext) |
| **Component init pipeline** | `JsScript(stateRef=-1)` → load module → `__componentInit` → store state handle | `JsComponentInitSystem.cs` |
| **Tick dispatch** | Route `update/fixedUpdate/lateUpdate` to correct tick group, call `__tickComponents` | `JsTickSystemBase.cs`, `JsVariableTickSystem.cs`, etc. |
| **Query bridge** | Cache EntityQuery objects, precompute results, defer query creation to system context | `JsQueryBridge.cs` |
| **Component store** | JS-defined component data (`__js_comp`), tag pool (`JsDynTag0..63`) | `JsComponentStore.cs` |
| **Event dispatch** | Buffer events from JS, play back via ECB | `JsEventDispatcher.cs` |
| **Script cleanup** | Call `onDestroy()`, release state on entity destruction | `JsScriptCleanupSystem.cs` |
| **Generated JS glue** | QueryBuilder, ComponentGlue — inline JS strings evaluated at VM startup | `JsSystemRunner.cs` (embedded) |
| **TSC compilation** | TypeScript → JavaScript compilation, watch mode, error reporting | `TscCompiler.cs` |
| **Type stub generation** | Auto-generate `.d.ts` files from `[JsBridge]` C# types | `JsTypeStubGenerator.cs` |
| **String interning** | Zero-per-frame string allocations for function/tick group names | `JsRuntimeManager.cs` (pre-cached byte arrays) |

### Layer 3: Native Libraries (external dependencies)

| Library | What unity.js uses from it | Binding |
|---------|---------------------------|---------|
| **QuickJS-ng** | JS runtime, context, eval, module API, value marshaling, GC | P/Invoke via `libquickjs.so/dll` → `QJS.cs` static methods |
| **Unity.Entities** | World, EntityManager, EntityCommandBuffer, ISystem, SystemBase, EntityQuery, ComponentLookup | Direct C# API |
| **Unity.Mathematics** | float2/3/4, quaternion, math.* functions | Direct C# API, mirrored to JS |
| **Unity.Burst** | SharedStatic, BurstCompile, MonoPInvokeCallback | Attributes + SharedStatic for hot-path data |
| **Unity.Collections** | NativeArray, NativeHashMap, NativeList, UnsafeList, FixedString | Direct C# API for unmanaged containers |
| **Unity.Transforms** | LocalTransform, LocalToWorld | ComponentLookup for position read/write |
| **Unity.Logging** | Log.Info/Warning/Error | Direct C# API |
| **Unity.Physics** | PhysicsVelocity, PhysicsDamping, collision events | Optional integration |
| **Unity.InputSystem** | InputAction asset reading | Optional integration via `JsInputBridge` |
| **ALINE** | CommandBuilder for debug drawing | Optional integration via `JsDrawBridge` |
| **System.Runtime.InteropServices** | Marshal, DllImport, GCHandle for P/Invoke | .NET runtime |

---

## Phase 1: Architectural Vision Document

**Goal:** Create `Packages/com.api-haus.unity.js/docs/ARCHITECTURE.md` — a technical document with mermaid diagrams that defines the ideal architecture. This document becomes the source of truth for all future work.

### Document Structure

#### 1.1 — System Overview Diagram
High-level mermaid diagram showing the 5 isolated layers:

```
Layer 1: QuickJS Engine (pure VM, no Unity knowledge)
Layer 2: Module System (script loading, resolution, caching)
Layer 3: Bridge Registry (C#↔JS type mapping, P/Invoke registration)
Layer 4: ECS Integration (entity lifecycle, component init, tick dispatch)
Layer 5: Editor Tools (hot reload, type stubs, TSC compilation)
```

Each layer depends ONLY on the layer below it. No layer reaches up.

#### 1.2 — Layer Boundary Contracts
For each layer boundary, document:
- What the upper layer may call (public API surface)
- What the lower layer guarantees (invariants)
- What crosses the boundary (data types only, no callbacks reaching up)

#### 1.3 — Component Lifecycle Sequence Diagram
Mermaid sequence diagram showing the full lifecycle:
```
Baker → JsScript(stateRef=-1) → JsComponentInitSystem → __componentInit →
__tickComponents deferred start() → update(dt) each frame → onDestroy()
```
Every step labeled with which layer owns it.

#### 1.4 — System Lifecycle Sequence Diagram
```
JsSystemRunner.OnCreate → scan systems/ → LoadScriptAsModule →
OnUpdate → UpdateBurstContext → __tickComponents → onUpdate(state)
```

#### 1.5 — Domain Reload / Hot Reload State Machine
Mermaid state diagram showing VM state transitions:
```
Fresh → Initialized → Running → [DomainReload] → Disposed → Fresh
Fresh → Initialized → Running → [HotReload] → ScriptInvalidated → Reloaded → Running
```
Document what state must be cleared at each transition.

#### 1.6 — Query Pipeline Diagram
```
Module scope: ecs.query().withAll(X).build() → QueryBuilder → BuiltQuery
Frame tick: BuiltQuery[Symbol.iterator] → _nativeQuery(P/Invoke) → entity IDs
Per entity: accessor.get(eid) → P/Invoke → ComponentLookup → data
Iteration end: accessor.set(eid, data) → P/Invoke → ECB writeback
```

#### 1.7 — Architectural Principles
Codify the rules that prevent the failure patterns:

1. **One-way dependencies** — Layer N may depend on Layer N-1, never on N+1 or N+2
2. **No static mutable state** — All state owned by a single manager, passed explicitly
3. **Initialization is deterministic** — Document exact ordering with `[UpdateBefore]`/`[UpdateAfter]`
4. **Module loading is idempotent** — Same script loaded twice = same result, no side effects
5. **Bridge registration is atomic** — All bridges registered before any script evaluates
6. **Query creation happens in system context** — Never inside P/Invoke callbacks
7. **ECB is the only write path** — No direct EntityManager mutations from JS

#### 1.8 — File Ownership Map
Table mapping each source file to exactly one layer. If a file belongs to two layers, it must be split.

---

## Phase 2: E2E Test Architecture

**Goal:** Replace unit tests with feature-organized E2E tests that use `EnterPlayMode`.

### Design Principles

1. **No unit tests for JS bridge code** — If a bridge function works in isolation but fails in play mode, the unit test gave false confidence. Delete it.
2. **Every test enters play mode** — Tests live in EditMode assemblies, use `[UnityTest]` with `yield return new EnterPlayMode()`, verify behavior, then `yield return new ExitPlayMode()`.
3. **Tests organized by feature, not by subsystem** — Not "JsMathBridgeTests" but "MathOperationsE2E" that tests math through a real component script.
4. **Each test proves a user-visible behavior** — "slimes move", "input reads correctly", "hot reload preserves state", "spatial queries return nearby entities".

### Test Organization

```
Tests/
  EditMode/
    Features/
      ComponentLifecycleE2ETests.cs    — start/update/destroy through real baking
      SystemExecutionE2ETests.cs        — system auto-discovery and onUpdate
      QueryPipelineE2ETests.cs          — ecs.query() through real components
      MathBridgeE2ETests.cs             — math ops through real component scripts
      EntityCreationE2ETests.cs         — entities.create() through real systems
      HotReloadE2ETests.cs              — file mutation → recompile → verify
      DomainReloadE2ETests.cs           — enter/exit play mode cycles
      SpatialQueryE2ETests.cs           — spatial.trigger() through components
      InputBridgeE2ETests.cs            — input reading through real systems
      DrawBridgeE2ETests.cs             — draw calls through real systems
    Stress/
      VmRecreationStressTests.cs        — rapid enter/exit cycles
      HotReloadStressTests.cs           — concurrent file mutations
    Verification/
      SlimeMovementE2ETests.cs          — real scene gate (existing)
      LiveReloadE2ETests.cs             — real scene gate (existing)
```

### What Gets Deleted

- `JsMathBridgeTests` — replaced by `MathBridgeE2ETests` that runs math through a real component
- `JsLogBridgeTests` — replaced by log verification inside lifecycle E2E tests
- `JsComponentStoreTests` — replaced by `ComponentLifecycleE2ETests`
- `JsBridgeMarshalTests` — replaced by testing marshaling through real queries
- `JsComponentClassTests` — the glue invariant tests stay IF they test something not covered by E2E; otherwise delete
- All `JsBridgeTestFixture`-based tests that use `EvalGlobal()` shortcuts

### What Stays (pure data structure tests)

- `KDTreeTests`, `ShapeQueryTests`, `ShapeOverlapTests`, `SpatialShapeTests` — pure math, no JS involvement
- `BurstIdAllocatorTests`, `BurstIdLookupTests` — pure data structures
- `QJSTests` — tests the native QuickJS bindings themselves (layer 1)

---

## Phase 3: Architectural Rebuild

**Goal:** Refactor the existing codebase to match the documented architecture. Same featureset, proper boundaries.

### 3.1 — Split God Objects

**JsECSBridge (600+ lines) → 4 focused classes:**
- `JsEntityBridge` — entity creation/destruction, ID allocation
- `JsEventBridge` — event dispatch and buffering
- `JsBurstContext` — SharedStatic context lifecycle (UpdateBurstContext)
- `JsBridgeBootstrap` — one-time initialization, bridge registration ordering

**JsSystemRunner → 2 classes:**
- `JsSystemDiscovery` — scan systems/ directories, track loaded systems
- `JsSystemTicker` — the actual OnUpdate that calls __tickComponents and system onUpdate

**JsRuntimeManager — reduce surface area:**
- Extract module loading into `JsModuleManager` (already partially exists)
- Extract entity state tracking into `JsEntityStateStore`
- JsRuntimeManager becomes a thin facade: create VM, dispose VM, get module manager

### 3.2 — Eliminate Static Mutable State

Current: `JsECSBridge` uses scattered `SharedStatic<T>` fields accessed from anywhere.

Target: All burst-accessible state owned by `JsBurstContext`, created fresh each frame in `OnUpdate`, passed explicitly to tick functions. No ambient static state that persists across frames.

### 3.3 — Deterministic Initialization

Current: Initialization happens across 3 systems with implicit ordering assumptions.

Target: Single `JsBootstrapSystem` (InitializationSystemGroup, OrderFirst):
1. Ensure VM exists
2. Register all bridges (atomic — all or nothing)
3. Load glue modules (QueryBuilder, ComponentGlue)
4. Mark "ready" flag

All other systems check the ready flag. No system touches bridges or glue.

### 3.4 — Idempotent Module Loading

Current: `LoadScriptAsModule` has side effects; duplicate loads corrupt state.

Target: Module loading is pure: `LoadOrGet(scriptId, source) → ModuleHandle`. Same input = same output. Cache keyed by (scriptId, sourceHash). No global state mutation.

---

## Phase 4: Verification

After each phase, run the full verification suite:

```bash
# All unity.js E2E tests (EditMode — these enter play mode internally)
scripts/run-tests.sh EditMode "UnityJS"

# All unity.js PlayMode tests (pure data structure tests that remain)
scripts/run-tests.sh PlayMode "UnityJS"

# Project verification gates
scripts/run-tests.sh EditMode "Project.Tests"
scripts/run-tests.sh PlayMode "Project.Tests"
```

Every test must pass. No "we'll fix that later."

---

## Progress

| Step | Status | Notes |
|------|--------|-------|
| 1. Write ARCHITECTURE.md | **DONE** | Mermaid diagrams, layer model, feature catalog, lifecycle sequences, god object decomposition, key invariants, test architecture with SceneFixture DSL |
| 2. Review with user | **DONE** | Three-layer feature catalog, E2E requirements, SceneFixture DSL — all reviewed and approved |
| 3. Implement SceneFixture | **DONE** | `Tests/EditMode/SceneFixture.cs` — programmatic scene DSL, `IDisposable`, replicates baker output |
| 4. Compile-verify SceneFixture | **NEXT** | Run EditMode tests to verify SceneFixture compiles |
| 5. Write TS test fixtures | TODO | `Tests~/Fixtures/components/` and `systems/` — the controlled inputs |
| 6. Write E2E test classes | TODO | Feature-organized, using SceneFixture + TS fixtures |
| 7. Run E2E tests GREEN | TODO | Verify tests pass against current code |
| 8. Refactor god objects | TODO | Split JsECSBridge, JsSystemRunner, JsRuntimeManager |
| 9. Delete old unit tests | TODO | Only after E2E tests cover same behavior |
| 10. Final verification | TODO | All gates pass |

## Current Task: Implement E2E tests one by one

Full outline written to `docs/E2E_TESTS.md` — 16 test classes, 60+ tests, 19 TS fixtures.

Implementation order (gameplay lighthouses first, then feature probes):

**All E2E test classes implemented (89 EditMode + 278 PlayMode = 367 total, all green):**

Gameplay lighthouses (4): ComponentLifecycle, WanderingSlimes, CharacterMovement, SlimeSpatial
Feature probes (11): SystemExecution, MathBridge, QueryPipeline, ComponentAccess, EntityOperations, ColorBridge, SystemInfo, LogBridge, TickGroups, MultiScript, ModuleImport
Integration (2): InputBridge, DomainReload

**Remaining: HotReload E2E test (requires file mutation + TscCompiler recompile).**

**Architectural rebuild is blocked until user reviews and approves. Do not start refactoring without explicit approval.**
4. QueryPipelineE2ETests
5. ComponentAccessE2ETests
6. EntityOperationsE2ETests
7. ColorBridgeE2ETests
8. SystemInfoE2ETests
9. LogBridgeE2ETests
10. TickGroupE2ETests
11. MultiScriptE2ETests
12. ModuleImportE2ETests
13. InputBridgeE2ETests
14. SpatialQueryE2ETests
15. HotReloadE2ETests
16. DomainReloadE2ETests

Each test: write fixture → write test class → run → verify → next.

---

## Confirmed Decisions

- **Pure data-structure tests stay as-is** — KDTree, Shape*, BurstId*, QJS tests have no JS lifecycle involvement. Converting them to E2E adds overhead for zero benefit.
- **Architecture doc first, then code** — No code changes until ARCHITECTURE.md is written and reviewed. This prevents the exact "implement before understanding" pattern that caused past failures.

## Execution Order

1. **Write ARCHITECTURE.md** — vision document with mermaid diagrams (Phase 1)
2. **Review with user** — align on the target architecture before touching ANY code
3. **Write E2E tests first** — for every existing feature, write the E2E test that will prove it works (Phase 2)
4. **Run E2E tests GREEN** — they should pass against current code (this validates the tests themselves)
5. **Refactor incrementally** — split one god object at a time, run full suite after each split (Phase 3)
6. **Delete old unit tests** — only after E2E tests cover the same behavior
7. **Final verification** — all gates pass (Phase 4)

## Immediate Next Step

Write `Packages/com.api-haus.unity.js/docs/ARCHITECTURE.md` — the full technical vision document with mermaid diagrams. This is the deliverable for Phase 1.

---

## Critical Files

### To Read (architecture understanding)
- `Packages/com.api-haus.unity.js/Runtime/JsECS/Core/JsECSBridge.cs` — god object to split
- `Packages/com.api-haus.unity.js/Runtime/JsECS/Systems/JsSystemRunner.cs` — god object to split
- `Packages/com.api-haus.unity.js/Runtime/JsRuntime/Core/JsRuntimeManager.cs` — facade to simplify
- `Packages/com.api-haus.unity.js/Runtime/JsECS/Systems/JsComponentInitSystem.cs` — init ordering
- `Packages/com.api-haus.unity.js/Runtime/JsECS/Core/JsQueryBridge.cs` — query lifecycle
- `Packages/com.api-haus.unity.js/Runtime/JsECS/Core/JsComponentRegistry.cs` — bridge registration

### To Create
- `Packages/com.api-haus.unity.js/docs/ARCHITECTURE.md` — the vision document

### To Modify (Phase 3)
- All files in `Runtime/JsECS/Core/` — split god objects
- All files in `Runtime/JsECS/Systems/` — deterministic init
- All files in `Runtime/JsRuntime/Core/` — reduce surface area
- All test files — reorganize to E2E-only structure
