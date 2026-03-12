# QuickJS ECS Architecture

> **Pre-Alpha** - This document reflects current implementation. APIs may change.

This document defines the boundary between **Framework** (C#/ECS/Burst) and **Scriptable** (JS) layers.

## Design Philosophy

```
┌─────────────────────────────────────────────────────────────┐
│                      JS Scripts                              │
│   "What to do" — decisions, behaviors, state machines        │
├─────────────────────────────────────────────────────────────┤
│                      ECS Bridge                              │
│   API surface — ecs.*, input.* exposed to JS                 │
├─────────────────────────────────────────────────────────────┤
│                   Framework (C#/Burst)                       │
│   "How to do it" — movement, physics, queries, data          │
└─────────────────────────────────────────────────────────────┘
```

**Guiding principle:** JS controls *intent*, C# executes *mechanics*.

---

## Package Structure

```
Runtime/
  QJS/
    QJS.cs                    # P/Invoke bindings to quickjs native library
  JsRuntime/
    Core/
      JsRuntimeManager.cs    # JS runtime lifecycle, script loading
      JsStateExtensions.cs   # Helper extensions for JSContext
    Burst/
      BurstJsContext.cs       # SharedStatic patterns for Burst
  JsECS/
    Core/
      JsECSBridge.cs          # Coordinator: registration, shared state
      JsEntityRegistry.cs     # O(1) entity ID lookups via SharedStatic
      JsScriptPathUtility.cs  # Script file resolution, hash generation
      Bridge/
        JsEntitiesBridge.cs     # entities.create, destroy, add_script, has_script
        JsTransformBridge.cs    # transform.get_position, set_position, move_toward
        JsSpatialBridge.cs      # spatial.distance, query_near, get_entity_count
        JsEventsBridge.cs       # events.send_attack
        JsLogBridge.cs          # log.info, debug, warning, error
        JsInputBridge.cs        # input.read_value, was_pressed, is_held, was_released
        JsDrawBridge.cs         # draw.line, draw.sphere (debug)
    Components/
      JsScriptComponent.cs    # JsScriptRequest and JsScript buffers
      JsEvent.cs               # Event dispatch to JS
      JsPlayerComponents.cs   # Player tag and related components
    Systems/
      JsScriptingSystem.cs        # Orchestrator: runtime updates, events, direct ECB
      Support/
        JsScriptFulfillmentSystem.cs  # Request processing, script init, disabling
        JsEventDispatcher.cs          # Event collection, dispatch
        JsScriptCleanupSystem.cs      # Script state cleanup, OnDestroy
    Authoring/
      JsScriptAuthoring.cs        # MonoBehaviour for script assignment
      JsScriptBufferAuthoring.cs  # Buffer-based script authoring
    Demo/
      FruitEaterDemo.cs        # Demo bootstrapper
Editor/
  JsHotReloadSystem.cs          # FileSystemWatcher for hot reload
  JsScriptAssetReferenceDrawer.cs # Inspector drawer
Tests/
  JsECS.Tests/
    JsScriptLifecycleTest.cs
    JsEntityCreationTest.cs
    JsSpatialQueryTest.cs
    JsEventDispatchTest.cs
    JsEntityRegistryTest.cs
    JsStateCleanupTest.cs
  JsRuntime.Tests/
    BurstIdAllocatorTests.cs
    BurstIdLookupTests.cs
    BurstOperationQueueTests.cs
docs/
  entity-cleanup-flow.md       # Entity lifecycle and cleanup architecture
  sequence-runtime-flow.md     # Runtime interaction sequence diagram
  flowchart-architecture.md    # Structural relationships flowchart
  sequence-entity-lifecycle.md # Entity creation/destruction flow
```

## Visual Documentation

See the following mermaid diagrams for detailed flow visualization:

- **[entity-cleanup-flow.md](entity-cleanup-flow.md)** — Two-buffer lifecycle architecture
- **[sequence-runtime-flow.md](sequence-runtime-flow.md)** — Per-frame runtime interactions between components
- **[flowchart-architecture.md](flowchart-architecture.md)** — Structural relationships and decision points
- **[sequence-entity-lifecycle.md](sequence-entity-lifecycle.md)** — Entity creation/destruction lifecycle

---

## Script Lifecycle Architecture

The JS script system uses a **two-buffer architecture**:

### Components

| Component          | Type                        | Purpose                                    |
| ------------------ | --------------------------- | ------------------------------------------ |
| `JsScriptRequest`  | `IBufferElementData`        | Pending script requests with unique hashes |
| `JsScript`         | `ICleanupBufferElementData` | Initialized scripts with runtime state     |

### Key Fields

```csharp
public struct JsScriptRequest : IBufferElementData
{
    public FixedString64Bytes ScriptName;
    public Hash128 RequestHash;  // xxHash3 of script name for deduplication
    public bool Fulfilled;       // True once script is created
}

public struct JsScript : ICleanupBufferElementData
{
    public FixedString64Bytes ScriptName;
    public int StateRef;         // JS registry reference (-1 if disabled)
    public int EntityIndex;      // JS entity ID
    public Hash128 RequestHash;  // Links back to request
    public bool Disabled;        // True = skip execution, cleanup at entity end
}
```

### Lifecycle Systems

All lifecycle systems run in `InitializationSystemGroup`:

| System                      | Purpose                                                                       |
| --------------------------- | ----------------------------------------------------------------------------- |
| `JsScriptFulfillmentSystem` | Processes requests, creates runtime state, calls onInit, supports script disabling |
| `JsScriptCleanupSystem`     | Handles entity destruction, calls onDestroy, releases runtime state           |

---

## Framework Scope (C#/ECS)

The framework provides primitives that JS cannot efficiently implement.

### Core Systems (Implemented)

| System                                 | Purpose                                    | Thread | Burst |
| -------------------------------------- | ------------------------------------------ | ------ | ----- |
| `JsScriptingSystem`                    | Runtime updates, event dispatch, ECB       | Main   | No    |
| `JsScriptFulfillmentSystem`            | Script initialization, disabling           | Main   | No    |
| `JsScriptCleanupSystem`               | Destruction, onDestroy, cleanup            | Main   | No    |
| `EndSimulationEntityCommandBufferSystem` | Unity built-in structural change playback | Main   | N/A   |

### Bridge API (Implemented)

The bridge uses a **domain-oriented API** with direct ECB access. Modules are under `Runtime/JsECS/Core/Bridge/`:

| Module              | JS Namespace | Functions                                                             | Notes                                 |
| ------------------- | ------------ | --------------------------------------------------------------------- | ------------------------------------- |
| `JsEntitiesBridge`  | `entities`   | `create`, `destroy`, `add_script`, `has_script`                       | Entity lifecycle via direct ECB       |
| `JsTransformBridge` | `transform`  | `get_position`, `set_position`, `get_rotation`, `move_toward`         | Transform read/write + movement       |
| `JsSpatialBridge`   | `spatial`    | `distance`, `query_near`, `get_entity_count`                          | Distance checks, area queries         |
| `JsEventsBridge`    | `events`     | `send_attack`                                                         | Cross-entity event dispatch           |
| `JsLogBridge`       | `log`        | `info`, `debug`, `warning`, `error`                                   | Via Unity.Logging                     |
| `JsInputBridge`     | `input`      | `read_value`, `was_pressed`, `is_held`, `was_released`                | Unity Input System                    |
| `JsDrawBridge`      | `draw`       | `line`, `sphere`                                                      | Debug visualization                   |

### Support Systems

The scripting system delegates to specialized systems under `Runtime/JsECS/Systems/Support/`:

| System                      | Responsibility                                     |
| --------------------------- | -------------------------------------------------- |
| `JsScriptFulfillmentSystem` | Request processing, script init, disabling         |
| `JsEventDispatcher`         | Event collection, clearing, and dispatch           |
| `JsScriptCleanupSystem`     | onDestroy callbacks, state release, entity cleanup |

Entity ID management is handled by `JsEntityRegistry` via SharedStatic for Burst compatibility.

---

## `[JsBridge]` Codegen Pattern

Components annotated with `[JsBridge("name")]` automatically get Roslyn source-generated JS getter/setter bridges. This is the preferred way to expose ECS data to JS — define a component, annotate it, and the codegen handles registration.

Character controllers, stats, and gameplay systems should be implemented at the **project level** using this pattern rather than in the package.

---

## Scriptable Scope (JS)

What JS scripts control — the "brain" of game entities.

### Entity Behaviors

Scripts export lifecycle functions:

```js
// JS decides: "I should move toward the nearest fruit"
// Framework executes: pathfinding, collision, transform updates

export function onUpdate(state) {
    if (!state.target) {
        state.target = findNearestFruit(state.entity);
    }
    if (state.target) {
        transform.move_toward(state.entity, state.target, state.speed);
    }
}
```

### State Machines

JS owns entity state and transitions:

```js
state.mode = "idle";      // JS decides current mode
state.target = null;      // JS tracks targets
state.cooldown = 0;       // JS manages timers

// State transitions are script logic
if (state.mode === "gathering" && inventoryFull()) {
    state.mode = "returning";
}
```

### Event Handling

Scripts respond to framework events via callbacks:

```js
export function onAttacked(state, event) {
    state.threat = event.source;
    state.mode = "fleeing";
}

export function onCommand(state, command) {
    if (command === "stop") {
        state.mode = "idle";
    }
}
```

---

## Boundary Rules

### JS SHOULD control:
- High-level decisions (what to do next)
- State machine transitions
- Target selection and prioritization
- Behavior parameters (speeds, ranges, cooldowns)
- Game data definitions

### JS SHOULD NOT control:
- Physics calculations
- Spatial queries (use `spatial.query_near`)
- Transform math (use `transform.move_toward`, `spatial.distance`)
- Rendering or animation triggers
- Networking or persistence

### Framework MUST provide:
- Efficient spatial queries
- Burst-compiled movement
- Thread-safe command execution
- Data validation
- Event dispatch

---

## Extension Pattern

When adding new framework features, prefer the `[JsBridge]` codegen pattern:

1. **Define the component** (C# `IComponentData`) with `[JsBridge("name")]` attribute
2. **Codegen auto-generates** getter/setter bridge functions
3. **Update type definitions** (`types/unity.d.ts`)

For custom bridge functions that don't fit the codegen pattern:

1. **Add bridge function** (in appropriate `Bridge/*Bridge.cs` file)
2. **Register in `JsECSBridge.RegisterFunctions`**
3. **Update type definitions** (`types/unity.d.ts`)
4. **Document in this file**

---

## Architectural Decisions

### Static Bridge for Callback Compatibility

The `JsECSBridge` uses static fields and `[MonoPInvokeCallback]` methods because:
- QuickJS C function pointers cannot reference instance methods
- Static state is required for `MonoPInvokeCallback` attribute
- This enables integration with Burst-compiled systems

### Hybrid Pattern (Static + DI)

- **Static**: Bridge functions (callback-compatible, required by QuickJS)
- **Instance/Singleton**: `JsRuntimeManager` (for testability)
- **ECS-managed**: Systems follow standard ECS lifecycle

### Deferred Entity Operations

All structural changes use `EntityCommandBuffer` pattern:
- Entity creation returns ID immediately
- Actual entity created at frame end
- `m_DeferredEntities` map tracks in-flight entities

### Two-Buffer Lifecycle Architecture

Script initialization uses separate buffers for requests and fulfilled scripts:
- `JsScriptRequest` — pending requests, can be added any time
- `JsScript` — initialized scripts, cleanup buffer ensures proper teardown
- `RequestHash` (xxHash3 of script name) enables deduplication
- `Disabled` flag allows runtime script removal without entity destruction

---

## Current Implementation Status

| Feature           | Framework | Bridge | Types | Notes                      |
| ----------------- | --------- | ------ | ----- | -------------------------- |
| Transform         | ✅         | ✅      | ✅     | Full read/write support    |
| Movement          | ✅         | ✅      | ✅     | Burst-compiled commands    |
| Spatial Queries   | ✅         | ✅      | ✅     | Distance, area queries     |
| Commands          | ✅         | ✅      | ✅     | Deferred execution         |
| Events            | ✅         | ✅      | ✅     | Full dispatch system       |
| Logging           | ✅         | ✅      | ✅     | Unity.Logging integration  |
| Input             | ✅         | ✅      | ⚠️     | Unity Input System         |
| Debug Draw        | ✅         | ✅      | ⚠️     | Line, sphere primitives    |
| onDestroy         | ✅         | N/A    | N/A   | Lifecycle callback         |
| Script Disabling  | ✅         | N/A    | N/A   | Runtime script removal     |

Legend: ✅ Implemented | ⚠️ Partial/No Types | ❌ Not started

---

## See Also

- [Entity Cleanup Flow](entity-cleanup-flow.md) - Two-buffer lifecycle architecture
- [Multithreading Architecture](multithreading.md) - Future parallel script execution design (not implemented)
- [Runtime Flow](sequence-runtime-flow.md) - Frame execution sequence
- [Entity Lifecycle](sequence-entity-lifecycle.md) - Entity creation/destruction flow
