# unity.js

JavaScript scripting for Unity ECS. QuickJS-ng runtime with Roslyn codegen, whole-component accessors, and hot reload.

## `[JsBridge]` Attribute

Unified attribute for exposing ECS types to JS. Targets structs, enums, and assemblies.

### On structs

Generates `.get(eid)` / `.set(eid, obj)` JS functions that read/write the entire component as an object. Field names keep their C# casing (camelCase). The JS accessor name defaults to the struct name when omitted.

```csharp
[JsBridge]
public struct SlimeWanderConfig : IComponentData
{
    public float speed;
    public float wanderRadius;
    public WanderPlane wanderPlane;
    public float2 pauseTime;
    public float2 stepDistance;
}
```

JS usage:

```js
const cfg = SlimeWanderConfig.get(eid);  // returns object with all fields, or null
log.info(cfg.speed + " " + cfg.wanderRadius);  // camelCase field names
cfg.speed = 3.5;
SlimeWanderConfig.set(eid, cfg);  // write back the whole component
```

Properties:

| Property        | Default | Effect                                          |
| --------------- | ------- | ----------------------------------------------- |
| `NeedSetters`   | `true`  | Set `false` for read-only components (no `.set`) |
| `NeedAccessors` | `true`  | Set `false` for tag components (no `.get`)       |

```csharp
[JsBridge(NeedSetters = false)]
public struct ECSCharacterState : IComponentData { public bool isGrounded; ... }
```

### On enums

Generates a `SCREAMING_SNAKE` global object:

```csharp
[JsBridge]
public enum WanderPlane { XY, XZ }
```

```js
if (cfg.wanderPlane === WANDER_PLANE.XY) { ... }
```

### On assembly

Bridges external types not owned by your code:

```csharp
[assembly: JsBridge(typeof(LocalTransform))]
```

```js
const lt = LocalTransform.get(eid);
log.info(lt.Position.x + " " + lt.Position.y + " " + lt.Position.z);
LocalTransform.set(eid, { Position: float3(1, 2, 3), Scale: lt.Scale, Rotation: lt.Rotation });
```

## `[JsCompile]` Attribute

Placed on static methods. Two modes depending on whether `Signature` is set.

### Auto-compiled (no `Signature`)

Generates a shim-compatible wrapper, auto-registration, and a type stub from the C# signature.

```csharp
[JsCompile("math", "cross")]
static float3 Cross(float3 a, float3 b) => math.cross(a, b);

[JsCompile("math", "dot")]
static float Dot(float3 a, float3 b) => math.dot(a, b);
```

Supported parameter/return types: `float`, `int`, `bool`, `float3` (+ `out` variants), `void`.

### Stub-only (with `Signature`)

For hand-written `[MonoPInvokeCallback]` methods (e.g. multi-return, string args, complex logic). Only generates the type stub — no wrapper.

```csharp
[JsCompile("entities", "create", Signature = "fun(pos?: vec3): entity")]
[MonoPInvokeCallback(typeof(QJSShimCallback))]
static unsafe void Entities_Create(JSContext ctx, ...) { ... }
```

## JS Systems

Place scripts in `Assets/StreamingAssets/js/systems/`.

### Lifecycle callbacks

| Callback                    | Description                          |
| --------------------------- | ------------------------------------ |
| `onUpdate(state)`           | Called every frame (or tick group)    |
| `onInit(state)`             | Called once when script is attached   |
| `onDestroy(state)`          | Called before entity is destroyed     |
| `onCommand(state, cmd)`     | Called when a command targets entity  |

### Tick groups

Control when `onUpdate` runs with the `@tick:` annotation at the top of the file:

```js
// @tick: fixed
export function onUpdate(state) {
  // runs at fixed timestep
}
```

| Tick Group         | Description                             |
| ------------------ | --------------------------------------- |
| `variable`         | Default. Every frame (SimulationGroup)  |
| `fixed`            | Fixed timestep (FixedStepSimulation)    |
| `before_physics`   | Before physics simulation               |
| `after_physics`    | After physics simulation                |
| `after_transform`  | After transform updates                 |

### Queries and component access

Build queries with the fluent API, then iterate with destructuring. Components are automatically written back at each iteration boundary — no manual `.set()` needed.

```js
const q = ecs.query()
  .withAll(ECSCharacterControl, ECSCharacterStats, ECSCharacterState)
  .withNone(DeadTag)
  .build();

for (const [eid, ctrl, stats, st] of q) {
  ctrl.moveVector = float3(1, 0, 0);  // mutate in-place
  stats.stamina = math.max(0, stats.stamina - 10 * state.dt);
  // no .set() call needed — changes are flushed automatically
}
```

Component accessors (the bridged objects like `ECSCharacterControl`) are passed to `.withAll()` directly — not as strings. The iterator yields `[eid, comp1, comp2, ...]` tuples matching the order of `.withAll()` arguments.

You can still use `.get()` / `.set()` directly for one-off reads/writes outside of queries:

```js
const cfg = SlimeWanderConfig.get(eid);
cfg.speed = 3.5;
SlimeWanderConfig.set(eid, cfg);
```

## Built-in API

### `ecs`

| Function                                          | Description                                    |
| ------------------------------------------------- | ---------------------------------------------- |
| `ecs.query()`                                     | Returns a `QueryBuilder`                       |
| `QueryBuilder.withAll(...accessors)`              | Filter to entities with these components        |
| `QueryBuilder.withNone(...accessors)`             | Exclude entities with these components          |
| `QueryBuilder.build()`                            | Returns iterable `BuiltQuery`                   |

### `entities`

| Function                                   | Description                     |
| ------------------------------------------ | ------------------------------- |
| `entities.create(pos?)`                    | Create entity, returns ID       |
| `entities.destroy(eid)`                    | Destroy entity, returns success |
| `entities.addScript(eid, scriptName)`      | Add script to entity            |
| `entities.hasScript(eid, scriptName)`      | Check if entity has script      |
| `entities.removeComponent(eid, name)`      | Remove component from entity    |

### `input`

| Function                       | Description                              |
| ------------------------------ | ---------------------------------------- |
| `input.readValue(action)`      | Read action value (`number`/`float3`/`boolean`) |
| `input.wasPressed(action)`     | True if pressed this frame               |
| `input.isHeld(action)`         | True if currently held                   |
| `input.wasReleased(action)`    | True if released this frame              |

### `draw`

| Function                                  | Description                    |
| ----------------------------------------- | ------------------------------ |
| `draw.line(from, to)`                     | Debug line                     |
| `draw.ray(origin, direction)`             | Debug ray                      |
| `draw.arrow(from, to)`                    | Debug arrow                    |
| `draw.wireSphere(center, radius)`         | Wireframe sphere               |
| `draw.wireBox(center, size)`              | Wireframe box                  |
| `draw.wireCapsule(start, end, r)`         | Wireframe capsule              |
| `draw.solidBox(center, size)`             | Solid box                      |
| `draw.solidCircle(center, normal, r)`     | Solid circle                   |
| `draw.circleXz(center, radius)`           | Circle on XZ plane             |
| `draw.label2d(position, text)`            | 2D text label                  |
| `draw.setColor(r, g, b, a?)`             | Set draw color                 |
| `draw.withDuration(duration)`             | Set duration for next draws    |

### `math`

Most functions accept `number`, `float2`, `float3`, or `float4` unless noted.

**Constants**

| Name             | Description               |
| ---------------- | ------------------------- |
| `math.PI`        | 3.14159265...             |
| `math.E`         | 2.71828182...             |
| `math.EPSILON`   | Machine epsilon (~1.2e-7) |
| `math.INFINITY`  | Positive infinity         |
| `math.random()`  | Random number [0, 1)      |

**Trigonometric**

| Function          | Description            |
| ----------------- | ---------------------- |
| `math.sin(x)`     | Sine                   |
| `math.cos(x)`     | Cosine                 |
| `math.tan(x)`     | Tangent                |
| `math.asin(x)`    | Arcsine                |
| `math.acos(x)`    | Arccosine              |
| `math.atan(x)`    | Arctangent             |
| `math.atan2(y,x)` | Two-arg arctangent (scalar only) |
| `math.sinh(x)`    | Hyperbolic sine        |
| `math.cosh(x)`    | Hyperbolic cosine      |
| `math.tanh(x)`    | Hyperbolic tangent     |

**Exponential / Logarithmic**

| Function          | Description            |
| ----------------- | ---------------------- |
| `math.exp(x)`     | e^x                    |
| `math.exp2(x)`    | 2^x                    |
| `math.log(x)`     | Natural log            |
| `math.log2(x)`    | Base-2 log             |
| `math.log10(x)`   | Base-10 log            |
| `math.sqrt(x)`    | Square root            |
| `math.rsqrt(x)`   | Reciprocal square root |

**Rounding**

| Function          | Description                   |
| ----------------- | ----------------------------- |
| `math.floor(x)`   | Round down                    |
| `math.ceil(x)`    | Round up                      |
| `math.round(x)`   | Round to nearest              |
| `math.trunc(x)`   | Truncate toward zero          |
| `math.frac(x)`    | Fractional part (x - floor)   |

**Sign / Utility**

| Function            | Description              |
| ------------------- | ------------------------ |
| `math.abs(x)`       | Absolute value           |
| `math.sign(x)`      | Sign (-1, 0, or 1)      |
| `math.saturate(x)`  | Clamp to [0, 1]         |
| `math.radians(x)`   | Degrees to radians       |
| `math.degrees(x)`   | Radians to degrees       |

**Binary**

| Function            | Description              |
| ------------------- | ------------------------ |
| `math.min(a, b)`    | Minimum                  |
| `math.max(a, b)`    | Maximum                  |
| `math.pow(a, b)`    | Power (a^b)              |
| `math.step(a, b)`   | Step function            |

**Interpolation**

| Function                        | Description                          |
| ------------------------------- | ------------------------------------ |
| `math.lerp(a, b, t)`           | Linear interpolation                 |
| `math.clamp(x, min, max)`      | Clamp to range                       |
| `math.smoothstep(a, b, x)`     | Smooth Hermite interpolation         |
| `math.unlerp(a, b, x)`         | Inverse lerp (scalar only)           |
| `math.remap(a,b,c,d,x)`       | Remap from [a,b] to [c,d] (scalar only) |

**Vector (float3 only)**

| Function                          | Description              |
| --------------------------------- | ------------------------ |
| `math.dot(a, b)`                 | Dot product              |
| `math.cross(a, b)`              | Cross product            |
| `math.normalize(v)`             | Safe normalize           |
| `math.distance(a, b)`           | Distance                 |
| `math.distancesq(a, b)`         | Squared distance         |
| `math.length(v)`                | Length                    |
| `math.lengthsq(v)`              | Squared length           |
| `math.reflect(i, n)`            | Reflect around normal    |
| `math.refract(i, n, eta)`       | Refraction               |

### `colors`

| Function                        | Description                    |
| ------------------------------- | ------------------------------ |
| `colors.hsvToRgb(h, s, v)`     | HSV to RGB (h in degrees)      |
| `colors.rgbToHsv(rgb)`         | RGB to `{h, s, v}`             |
| `colors.oklabToRgb(lab)`       | OKLab to RGB                   |
| `colors.rgbToOklab(rgb)`       | RGB to OKLab                   |

### `log`

| Function           | Description       |
| ------------------ | ----------------- |
| `log.debug(msg)`   | Log debug message |
| `log.info(msg)`    | Log info message  |
| `log.warning(msg)` | Log warning       |
| `log.error(msg)`   | Log error         |
| `log.trace(msg)`   | Log trace         |

## Vector Types

### Constructors

`float2`, `float3`, and `float4` are global constructor functions:

```js
float3(1, 2, 3)    // component args
float3(5)           // splat → float3(5, 5, 5)
float3(other)       // clone from existing vector
float3()            // zero → float3(0, 0, 0)
```

### Static constants

```js
float3.zero  // float3(0, 0, 0)
float3.one   // float3(1, 1, 1)
// same for float2 and float4
```

### Arithmetic

Instance methods — `b` can be a vector of matching type or a scalar:

```js
const v = float3(1, 2, 3);
v.add(float3(4, 5, 6))  // float3(5, 7, 9)
v.sub(1)                 // float3(0, 1, 2)
v.mul(2)                 // float3(2, 4, 6)
v.div(float3(1, 2, 3))  // float3(1, 1, 1)
```

Global free functions — auto-detect dimension from `a`:

```js
add(a, b)   sub(a, b)   mul(a, b)   div(a, b)
```

### Swizzles

All permutations of component names are available as properties. Swizzles with **unique** indices are read/write; repeated indices are read-only.

```js
const v = float3(1, 2, 3);
v.xy             // float2(1, 2) — read
v.zyx            // float3(3, 2, 1) — read
v.xy = float2(9, 8)  // write — v is now float3(9, 8, 3)
v.xx             // float2(9, 9) — read-only (repeated index)
```

Return type matches swizzle length: 2-component → `float2`, 3 → `float3`, 4 → `float4`.

## Other Types

| Type     | JS representation                        |
| -------- | ---------------------------------------- |
| `entity` | integer                                  |
| `quat`   | `{x: number, y: number, z: number, w: number}` |

## Type Stubs

TypeScript declaration stubs are auto-generated to `Assets/StreamingAssets/js/types/unity.d.ts`. Regenerate via **Tools > JS > Generate Type Stubs** (also runs automatically on domain reload when changes are detected).
