# Game Mode Architecture

> **Draft** - Architecture document for Game Mode system. Subject to review.

## Overview

A **Game Mode** is a self-contained gameplay session backed by a JS script. Each game mode creates its own ECS World, runs a bootstrap script, and destroys all associated entities when transitioning to another mode.

```
┌─────────────────────────────────────────────────────────────┐
│                     C# / Main Menu                          │
│   GameModeManager.LoadMode("roguelike")                     │
├─────────────────────────────────────────────────────────────┤
│                     Game Mode World                         │
│   Isolated ECS World with all entities for this session     │
├─────────────────────────────────────────────────────────────┤
│                   Game Mode Script (JS)                     │
│   onInit → onLoad → onUpdate → onBeforeUnload               │
└─────────────────────────────────────────────────────────────┘
```

**Key principle:** Game modes own worlds. Worlds own entities. Mode switch = world destruction.

---

## World Isolation Strategy

### Decision: Separate ECS World per Game Mode

Each game mode creates and owns its own `World` instance. Mode transitions destroy the previous world entirely.

```
Mode A                          Mode B
┌─────────────────────┐         ┌─────────────────────┐
│ World "roguelike"   │  ──X──► │ World "base_build"  │
│ - All entities      │ Dispose │ - All entities      │
│ - All systems       │         │ - All systems       │
│ - Physics sim       │         │ - Physics sim       │
└─────────────────────┘         └─────────────────────┘
```

### Rationale

| Benefit | Description |
|---------|-------------|
| **Clean isolation** | No entity ID conflicts between modes |
| **Atomic cleanup** | `World.Dispose()` destroys everything in one call |
| **Idiomatic ECS** | Each world has own systems, queries, command buffers |
| **Physics isolation** | Each mode gets own physics simulation (Unity Physics is world-scoped) |
| **Safety** | Cannot accidentally reference entities from previous mode |

### Addressed Concerns

| Concern | Mitigation |
|---------|------------|
| Networking | Unity Netcode creates world per session; mode transitions align with session boundaries |
| Shared assets | Assets (prefabs, materials) are orthogonal to worlds; no issue |
| Creation overhead | Minimal (~1ms), acceptable during loading screen |
| System registration | All systems registered via `JsGameBootstrap.GetJsSystems()`; self-activate via `RequireForUpdate` |

### Rejected Alternative

**Single world with entity cleanup** was considered but rejected:
- Would require tracking all "mode entities" manually
- Bulk deletion is fragile and error-prone
- No physics isolation between modes
- Risk of orphaned references

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| World isolation | Separate world per mode | Clean lifecycle, physics isolation |
| Persistent entities | None | Entities are "alive" units; persistence is data tables, not entities |
| Mode parameters | None (v1) | Future key-value store will handle cross-mode data |
| Return data | None | Clean separation between modes |
| Main menu | Not a game mode | Will use GUI scripting (JS+UXML) |
| Mode stacking | Not supported | Flat replacement only; UI is within mode |
| Transitions | Async with progress | Long operation, supports callbacks |
| Scene integration | Orthogonal | Scenes scriptable from JS, not tied to mode |
| JS VM | Shared VM, world-scoped state | Simpler implementation, single VM instance |
| Script location | `StreamingAssets/js/GameModes/` | Uses existing script loading infrastructure |
| Default world | Null when no mode | No implicit world; explicit mode loading required |
| System activation | Self-activating via `RequireForUpdate` | Systems run when their entity types are present |

---

## Game Mode Lifecycle

```
LoadMode("roguelike")
       │
       ▼
┌──────────────────┐
│ onBeforeUnload   │  ← Previous mode (if any)
│ (previous mode)  │    Cleanup callbacks, save state
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Destroy Previous │  World.Dispose()
│ World            │  All entities destroyed
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Create New World │  new World("roguelike")
│                  │  Register systems
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ onInit           │  Schedule asset loading, spawn initial entities
│ (new mode)       │  Progress: 0% → N%
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Loading          │  Async loading of scheduled assets
│                  │  Progress: N% → 100%
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ onLoad           │  All scheduled loading complete
│                  │  Mode is now playable
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ onUpdate         │  Called every frame
│ (game loop)      │  Until mode switch
└──────────────────┘
```

---

## Script Callbacks

Game mode scripts are JS files in `StreamingAssets/js/GameModes/`:

```js
// GameModes/roguelike.js

export function onInit(mode, state) {
    // Called before loading starts
    // Schedule asset loading, create initial entities
    // 'mode' is the game mode entity
    // 'state' is persistent object for this mode

    state.level = 1;
    state.score = 0;

    // Spawn world entities
    for (let i = 0; i < 10; i++) {
        const enemy = entities.create({ x: Math.random() * 20, y: 0, z: Math.random() * 20 });
        entities.add_script(enemy, "enemy_basic");
    }

    log.info("Roguelike mode initializing...");
}

export function onLoad(mode, state) {
    // Called when all onInit-scheduled loading is complete
    // Mode is now fully playable

    log.info("Roguelike mode loaded! Starting level " + state.level);
}

export function onUpdate(mode, state, dt) {
    // Called every frame
    // Main game loop for this mode

    state.elapsed = (state.elapsed || 0) + dt;
}

export function onBeforeUnload(mode, state) {
    // Called before world destruction
    // Save state, cleanup external resources

    log.info("Roguelike mode unloading. Final score: " + state.score);
}
```

### Callback Summary

| Callback | When | Purpose |
|----------|------|---------|
| `onInit(mode, state)` | World created, before loading | Schedule loading, spawn entities |
| `onLoad(mode, state)` | All loading complete | Mode is playable, finalize setup |
| `onUpdate(mode, state, dt)` | Every frame | Main game loop |
| `onBeforeUnload(mode, state)` | Before world destruction | Cleanup, save state |

---

## C# API

### GameModeManager

Primary C# interface for mode management:

```csharp
namespace UnityJS.Entities
{
    /// <summary>
    /// Manages game mode lifecycle: loading, transitions, world ownership.
    /// </summary>
    public class GameModeManager : IDisposable
    {
        /// <summary>Current game mode name, or null if no mode active.</summary>
        public string CurrentMode { get; }

        /// <summary>Current mode's ECS World, or null if no mode active.</summary>
        public World CurrentWorld { get; }

        /// <summary>True if a mode transition is in progress.</summary>
        public bool IsLoading { get; }

        /// <summary>Loading progress 0.0 to 1.0 during transitions.</summary>
        public float LoadingProgress { get; }

        /// <summary>
        /// Load a game mode by script name.
        /// </summary>
        /// <param name="modeName">Script name without path/extension (e.g., "roguelike")</param>
        /// <returns>Async operation that completes when mode is loaded</returns>
        public async Task LoadModeAsync(string modeName, CancellationToken ct = default);

        /// <summary>
        /// Load a game mode with progress callback.
        /// </summary>
        public async Task LoadModeAsync(string modeName, IProgress<float> progress, CancellationToken ct = default);

        /// <summary>
        /// Unload current mode without loading another.
        /// </summary>
        public async Task UnloadCurrentModeAsync(CancellationToken ct = default);

        /// <summary>
        /// Event fired when mode loading starts.
        /// </summary>
        public event Action<string> OnModeLoadStarted;

        /// <summary>
        /// Event fired when mode is fully loaded.
        /// </summary>
        public event Action<string> OnModeLoaded;

        /// <summary>
        /// Event fired before mode unloads.
        /// </summary>
        public event Action<string> OnModeUnloading;
    }
}
```

### Usage Example

```csharp
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private GameModeManager gameModeManager;
    [SerializeField] private Slider loadingBar;

    public async void OnPlayButtonClicked()
    {
        loadingBar.gameObject.SetActive(true);

        var progress = new Progress<float>(p => loadingBar.value = p);
        await gameModeManager.LoadModeAsync("roguelike", progress);

        loadingBar.gameObject.SetActive(false);
    }
}
```

---

## JS API

### Mode Switching from JS

```js
// Switch to another game mode from within JS
gamemode.load("base_building");

// Get current mode name
const current = gamemode.current();  // "roguelike"

// Check if loading
const isLoading = gamemode.is_loading();  // false
```

### Bridge Functions

| Function | Parameters | Returns | Description |
|----------|------------|---------|-------------|
| `gamemode.load(name)` | `string` | `void` | Request mode switch (async) |
| `gamemode.current()` | - | `string?` | Current mode name or null |
| `gamemode.is_loading()` | - | `boolean` | True during transitions |

---

## Implementation Components

### New Files

```
Runtime/
  JsECS/
    GameMode/
      GameModeManager.cs        # Main manager, world lifecycle
      GameModeState.cs          # Mode state container
      GameModeWorld.cs          # World setup, system registration
      JsGameModeBridge.cs       # JS API for mode switching
    Systems/
      GameModeUpdateSystem.cs   # Drives onUpdate for mode script
```

### Integration Points

| Component | Integration |
|-----------|-------------|
| `JsRuntimeManager` | Shared VM; mode state isolated via JS objects |
| `JsEntityRegistry` | Clear on world destruction, reinitialize for new world |
| `JsECSBridge` | Rebind `EntityManager` reference to new world |
| JS Systems | Self-activate via `RequireForUpdate<T>` when relevant entities exist |

### World Setup Sequence

```csharp
private async Task<World> CreateModeWorld(string modeName)
{
    // 1. Create world
    var world = new World(modeName);

    // 2. Get or create system groups (systems self-register)
    world.GetOrCreateSystemManaged<InitializationSystemGroup>();
    world.GetOrCreateSystemManaged<SimulationSystemGroup>();

    // 3. Add all JS systems - they self-activate via RequireForUpdate
    DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(
        world,
        JsGameBootstrap.GetJsSystems()
    );

    // 4. Rebind bridge to new world
    JsECSBridge.SetWorld(world);
    JsEntityRegistry.Clear();

    // 5. Create mode entity with script
    var modeEntity = world.EntityManager.CreateEntity();
    var buffer = world.EntityManager.AddBuffer<JsScriptRequest>(modeEntity);
    buffer.Add(new JsScriptRequest
    {
        ScriptName = $"GameModes/{modeName}",
        RequestHash = JsScriptPathUtility.ComputeHash($"GameModes/{modeName}")
    });

    return world;
}
```

### System Self-Activation Pattern

Systems use `RequireForUpdate` to only run when their entity types exist:

```csharp
[BurstCompile]
public partial struct JsHealthSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // System only updates when JsHealth entities exist
        state.RequireForUpdate<JsHealth>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Process health...
    }
}
```

This means:
- All JS systems are registered in every game mode world
- Systems with no matching entities have zero overhead (skip update)
- Game modes implicitly enable systems by creating relevant entities

### Shared VM with World-Scoped State

Single JS VM instance, but each world's entities have isolated state:

```
┌─────────────────────────────────────────────────────────┐
│                    JS VM (Single)                        │
├─────────────────────────────────────────────────────────┤
│  Global Objects:                                         │
│    ecs.*, gamemode.*, log.*, etc.                       │
├─────────────────────────────────────────────────────────┤
│  Per-Entity State (via registry refs):                  │
│    Entity 1 → { state object }                          │
│    Entity 2 → { state object }                          │
│    ...                                                  │
└─────────────────────────────────────────────────────────┘
```

On world destruction:
1. All entities in that world are destroyed
2. `JsScriptCleanupSystem` calls `onDestroy` for each script
3. Registry refs released → JS GC collects state objects
4. `JsEntityRegistry.Clear()` resets ID mappings

The VM itself persists, but all world-specific state is cleaned up through normal entity destruction flow.

---

## Backward Compatibility

The game mode system is **opt-in**. Existing code using direct world access continues to work:

```csharp
// Old way: Direct world usage (still works)
var world = World.DefaultGameObjectInjectionWorld;
var entity = world.EntityManager.CreateEntity();

// New way: Via game mode (optional)
await gameModeManager.LoadModeAsync("fruit_eater");
// Now gameModeManager.CurrentWorld is the active world
```

Tests can run without game modes by creating worlds directly.

---

## Acceptance Criteria

### Core Functionality
- [ ] `GameModeManager.LoadModeAsync()` creates isolated world
- [ ] Previous world destroyed on mode switch
- [ ] All four callbacks fire in correct order: onInit → onLoad → onUpdate → onBeforeUnload
- [ ] `gamemode.load()` works from JS
- [ ] Loading progress reported correctly

### Integration
- [ ] `JsEntityRegistry` cleared between modes
- [ ] `JsECSBridge` rebinds to new world
- [ ] Systems registered correctly in new world

### Testing
- [ ] PlayMode test: Load fruit_eater as game mode
- [ ] PlayMode test: Switch between two modes
- [ ] PlayMode test: Verify entity isolation between modes
- [ ] PlayMode test: Verify callbacks fire correctly
- [ ] Existing tests pass without game mode system

---

## Future Considerations

### v2: Persistent Key-Value Store
Cross-mode data via typed key-value store with optional persistence:
```js
// In roguelike mode
persist.set("blueprints_unlocked", state.blueprints);

// In base_building mode
const blueprints = persist.get("blueprints_unlocked", {});
```

### v2: Asset Loading Integration
Formalized asset loading with sectioned progress:
```js
export function onInit(mode, state) {
    assets.load("prefabs/enemies", { progress_id: "enemies" });
    assets.load("prefabs/environment", { progress_id: "environment" });
}
```

### v2: Scene Integration
Optional scene loading as part of mode initialization:
```js
export function onInit(mode, state) {
    scene.load("Levels/Forest");
}
```

---

## See Also

- [ARCHITECTURE.md](ARCHITECTURE.md) - Overall unity.js architecture
- [Entity Cleanup Flow](entity-cleanup-flow.md) - Entity lifecycle
- [fruit_demo_bootstrap.js](../../../Assets/StreamingAssets/js/scripts/fruit_demo_bootstrap.js) - Example bootstrap pattern
