# unity.js

TypeScript/JavaScript scripting for Unity DOTS via [QuickJS-ng](https://github.com/nicbarker/quickjs-ng). Write ECS systems and components in TypeScript with hot reload, auto-generated type stubs, and Burst-compiled math bridges.

## Installation

Add to your project via git URL in Unity Package Manager:

```
https://github.com/api-haus/com.api-haus.unity.js.git
```

Or via [OpenUPM](https://openupm.com):

```
openupm add com.api-haus.unity.js
```

### Requirements

- Unity 2022.3+
- `com.unity.entities` 1.0.0+
- `com.unity.mathematics` 1.0.0+

## Quick Start

Place TypeScript files in `Assets/StreamingAssets/unity.js/`. Systems go in `systems/`, components in `components/`.

### System script

```typescript
// systems/my_system.ts
// @tick: variable

import * as ecs from 'unity.js/ecs'
import { MyComponent } from 'unity.js/components'

let query: any;

export function onUpdate(state: UpdateState): void {
  query ??= ecs.query().withAll(MyComponent).build();

  for (const [eid, comp] of query) {
    comp.health -= state.deltaTime;
  }
}
```

### Component script

```typescript
// components/my_component.ts

import { Component } from 'unity.js/ecs'

export default class MyComponent extends Component {
  speed = 5;

  start() {
    // called once after entity is created
  }

  update(dt: number) {
    // called every frame
  }
}
```

### Bridging C# components

Mark ECS components with `[JsBridge]` to expose them to JS with auto-generated accessors:

```csharp
[JsBridge]
public struct Health : IComponentData
{
    public float current;
    public float max;
}
```

JS usage — components are auto-flushed when iterating queries:

```typescript
const q = ecs.query().withAll(Health).build();
for (const [eid, hp] of q) {
  hp.current -= 10;  // no .set() needed, flushed automatically
}
```

## Tick Groups

Control when `onUpdate` runs with `// @tick:` annotations:

| Annotation | System Group | Requires |
|---|---|---|
| `variable` (default) | SimulationSystemGroup | - |
| `fixed` | FixedStepSimulationSystemGroup | Physics integration |
| `before_physics` | Before PhysicsSystemGroup | Physics integration |
| `after_physics` | After PhysicsSystemGroup | Physics integration |
| `after_transform` | After TransformSystemGroup | - |

## Integrations

unity.js ships with optional integrations that activate automatically when their dependency is detected in your project. No configuration needed — an editor-only detector scans for known asset GUIDs on domain reload and enables the appropriate integration assemblies.

| Integration | Dependency | What it provides |
|---|---|---|
| **Physics** | `com.unity.physics` | `fixed`, `before_physics`, `after_physics` tick systems; `PhysicsVelocity` and `PhysicsDamping` bridges |
| **InputSystem** | `com.unity.inputsystem` | `input.readValue()`, `input.wasPressed()`, `input.isHeld()`, `input.wasReleased()` bridges |
| **ALINE** | `com.arongranberg.aline` | `draw.line()`, `draw.wireSphere()`, `draw.label2d()` and other debug drawing bridges |
| **CharacterController** | `com.unity.charactercontroller` | `ECSCharacterControl`, `ECSCharacterStats`, `ECSCharacterState` bridges + character physics systems |
| **QuantumConsole** | `com.qfsw.quantum-console` | (Planned) JS REPL via Quantum Console |

Each integration lives in `Integrations/<Name>/` with its own assembly, `defineConstraints`, and test suite. Integration assemblies only compile when their dependency is present.

## Built-in JS API

### `ecs`

| Function | Description |
|---|---|
| `ecs.query()` | Returns a `QueryBuilder` |
| `.withAll(...components)` | Filter to entities with these components |
| `.withNone(...components)` | Exclude entities with these components |
| `.build()` | Returns iterable query |

### `entities`

| Function | Description |
|---|---|
| `entities.create(pos?)` | Create entity, returns ID |
| `entities.destroy(eid)` | Destroy entity |
| `entities.addScript(eid, name)` | Add script to entity |
| `entities.hasScript(eid, name)` | Check if entity has script |

### `math`

Burst-compiled math functions. Most accept `number`, `float2`, `float3`, or `float4`.

Trig: `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `atan2`
Exp: `exp`, `log`, `sqrt`, `pow`
Rounding: `floor`, `ceil`, `round`, `frac`
Utility: `abs`, `sign`, `clamp`, `saturate`, `lerp`, `smoothstep`, `min`, `max`
Vector: `dot`, `cross`, `normalize`, `distance`, `length`, `reflect`

### `log`

`log.debug(msg)`, `log.info(msg)`, `log.warning(msg)`, `log.error(msg)`

### Vector types

```typescript
float3(1, 2, 3)    // component args
float3(5)           // splat
float3()            // zero
v.add(b)  v.sub(b)  v.mul(b)  v.div(b)
v.xy  v.zyx  v.xz = float2(1, 2)  // swizzles (read/write)
```

## `[JsBridge]` Attribute

### On structs

Generates `.get(eid)` / `.set(eid, obj)` accessors:

```csharp
[JsBridge]
public struct MyData : IComponentData { public float speed; }

[JsBridge(NeedSetters = false)]  // read-only, no .set()
public struct MyState : IComponentData { public bool active; }
```

### On enums

Generates a global object matching the C# name:

```csharp
[JsBridge]
public enum WanderPlane { XY, XZ }
// JS: WanderPlane.XY
```

### On assembly

Bridges external types:

```csharp
[assembly: JsBridge(typeof(LocalTransform))]
```

## `[JsCompile]` Attribute

Auto-generates shim wrappers and type stubs from C# method signatures:

```csharp
[JsCompile("math", "cross")]
static float3 Cross(float3 a, float3 b) => math.cross(a, b);
```

## Hot Reload

TypeScript files are compiled by `tsc` and watched for changes. Edits to `.ts` files are picked up automatically during play mode — no restart needed.

Type stubs are auto-generated to `Assets/StreamingAssets/unity.js/types/unity.d.ts` on domain reload. Regenerate manually via **Tools > JS > Generate Type Stubs**.

## Architecture

```
Packages/unity.js/
  Runtime/
    QuickJS/           # QuickJS P/Invoke bindings (UnityJS.QJS)
    JsRuntime/         # Script loading, modules, search paths (UnityJS.Runtime)
    JsECS/             # ECS bridge, components, tick systems (UnityJS.Entities)
  Editor/              # Type stub generator, tsc compiler, hot reload (UnityJS.Editor)
    Integrations/      # Integration detector + E2E test harness
  Integrations/        # Optional integration assemblies
    Physics/           # Unity.Physics tick systems + component bridges
    InputSystem/       # Input action bridges
    ALINE/             # Debug drawing bridges
    CharacterController/  # Character movement components + systems
    QuantumConsole/    # (Planned) JS REPL
  runtimes/            # Native QuickJS binaries (linux, win, osx, android, ios)
```

## License

See [LICENSE](LICENSE).
