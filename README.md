# LuaGame

Lua scripting for Unity ECS. Burst-compiled bridges with Roslyn codegen, whole-component accessors, and hot reload.

## `[LuaBridge]` Attribute

Unified attribute for exposing ECS types to Lua. Targets structs, enums, and assemblies.

### On structs

Generates `.get(eid)` / `.set(eid, table)` Lua functions that read/write the entire component as a table. Field names auto-convert `camelCase` to `snake_case`.

```csharp
[LuaBridge("slime_wander_config")]
public struct SlimeWanderConfig : IComponentData
{
    public float speed;
    public float wanderRadius;
    public WanderPlane wanderPlane;
    public float2 pauseTime;
    public float2 stepDistance;
}
```

Lua usage:

```lua
local cfg = slime_wander_config.get(eid)  -- returns table with all fields, or nil
print(cfg.speed, cfg.wander_radius)       -- snake_case field names
cfg.speed = 3.5
slime_wander_config.set(eid, cfg)         -- write back the whole component
```

Properties:

| Property        | Default | Effect                                          |
| --------------- | ------- | ----------------------------------------------- |
| `NeedSetters`   | `true`  | Set `false` for read-only components (no `.set`) |
| `NeedAccessors` | `true`  | Set `false` for tag components (no `.get`)       |

```csharp
[LuaBridge("char_state", NeedSetters = false)]
public struct ECSCharacterState : IComponentData { public bool isGrounded; ... }
```

### On enums

Generates a `SCREAMING_SNAKE` global table:

```csharp
[LuaBridge]
public enum WanderPlane { XY, XZ }
```

```lua
if cfg.wander_plane == WANDER_PLANE.XY then ... end
```

### On assembly

Bridges external types not owned by your code:

```csharp
[assembly: LuaBridge(typeof(LocalTransform), "local_transform")]
```

```lua
local lt = local_transform.get(eid)
print(lt.position.x, lt.position.y, lt.position.z)
local_transform.set(eid, { position = { x = 1, y = 2, z = 3 }, scale = lt.scale, rotation = lt.rotation })
```

## `[LuaCompile]` Attribute

Placed on static methods. Two modes depending on whether `Signature` is set.

### Auto-compiled (no `Signature`)

Generates a Burst-compatible P/Invoke wrapper, auto-registration, and a type stub from the C# signature.

```csharp
[LuaCompile("math", "cross")]
static float3 Cross(float3 a, float3 b) => math.cross(a, b);

[LuaCompile("math", "dot")]
static float Dot(float3 a, float3 b) => math.dot(a, b);
```

Supported parameter/return types: `float`, `int`, `bool`, `float3` (+ `out` variants), `void`.

### Stub-only (with `Signature`)

For hand-written `[MonoPInvokeCallback]` methods (e.g. multi-return, string args, complex logic). Only generates the type stub — no wrapper.

```csharp
[LuaCompile("entities", "create", Signature = "fun(pos?: vec3): entity")]
[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
[BurstCompile]
static int Entities_Create(lua_State l) { ... }
```

## Lua Systems

Place scripts in `Assets/StreamingAssets/lua/systems/`.

### Lifecycle callbacks

| Callback                          | Description                          |
| --------------------------------- | ------------------------------------ |
| `OnUpdate(dt)`                    | Called every frame (or tick group)    |
| `OnInit(entity, state)`          | Called once when script is attached   |
| `OnDestroy(entity, state)`       | Called before entity is destroyed     |
| `OnCommand(entity, state, cmd)`  | Called when a command targets entity  |

### Tick groups

Control when `OnUpdate` runs with the `@tick:` annotation at the top of the file:

```lua
-- @tick: fixed
function OnUpdate(dt)
  -- runs at fixed timestep
end
```

| Tick Group         | Description                             |
| ------------------ | --------------------------------------- |
| `variable`         | Default. Every frame (SimulationGroup)  |
| `fixed`            | Fixed timestep (FixedStepSimulation)    |
| `before_physics`   | Before physics simulation               |
| `after_physics`    | After physics simulation                |
| `after_transform`  | After transform updates                 |

### Queries and component access

```lua
local chars = ecs.query("char_control", "char_stats", "char_state")

for _, eid in ipairs(chars) do
  local ctrl = char_control.get(eid)   -- whole component as table
  local stats = char_stats.get(eid)
  ctrl.move_vector = { x = 1, y = 0, z = 0 }
  ctrl.sprint = stats.stamina > 0
  char_control.set(eid, ctrl)          -- write back
end
```

### Example: character input

```lua
-- @tick: variable

local STAMINA_DRAIN = 20
local STAMINA_REGEN = 10

function OnUpdate(dt)
  local chars = ecs.query("char_control", "char_stats", "char_state")

  for _, eid in ipairs(chars) do
    local move = input.read_value("Move")
    local jump_pressed = input.was_pressed("Jump")
    local sprint_held = input.is_held("Sprint")

    local mx, mz = 0, 0
    if move then
      mx, mz = move.x, move.y
    end

    local ctrl = char_control.get(eid)
    ctrl.move_vector = { x = mx, y = 0, z = mz }

    local stats = char_stats.get(eid)
    local sprinting = sprint_held and stats.stamina > 0 and (mx ~= 0 or mz ~= 0)

    if sprinting then
      stats.stamina = math.max(0, stats.stamina - STAMINA_DRAIN * dt)
    else
      stats.stamina = math.min(stats.max_stamina, stats.stamina + STAMINA_REGEN * dt)
    end
    ctrl.sprint = sprinting

    local state = char_state.get(eid)
    if state.is_grounded and not state.was_grounded_last_frame then
      stats.jump_count = 0
    end
    if jump_pressed and stats.jump_count < stats.max_jumps then
      ctrl.jump = true
      stats.jump_count = stats.jump_count + 1
    end

    char_control.set(eid, ctrl)
    char_stats.set(eid, stats)
  end
end
```

## Built-in API

### `ecs`

| Function                           | Description                         |
| ---------------------------------- | ----------------------------------- |
| `ecs.query("comp1", "comp2", ...)` | Returns array of matching entity IDs |

### `entities`

| Function                                  | Description                     |
| ----------------------------------------- | ------------------------------- |
| `entities.create(pos?)`                   | Create entity, returns ID       |
| `entities.destroy(eid)`                   | Destroy entity, returns success |
| `entities.add_script(eid, script_name)`   | Add script to entity            |
| `entities.has_script(eid, script_name)`   | Check if entity has script      |
| `entities.remove_component(eid, name)`    | Remove component from entity    |

### `transform`

| Function                                      | Description                |
| --------------------------------------------- | -------------------------- |
| `transform.get_position(eid)`                 | Returns `vec3`             |
| `transform.set_position(eid, x, y, z)`        | Set world position         |
| `transform.get_rotation(eid)`                 | Returns euler `vec3`       |
| `transform.move_toward(eid, target, speed)`   | Move toward entity or vec3 |

### `spatial`

| Function                            | Description                         |
| ----------------------------------- | ----------------------------------- |
| `spatial.distance(a, b)`            | Distance (entity or vec3)           |
| `spatial.query_near(center, radius)`| Returns array of nearby entity IDs  |
| `spatial.get_entity_count()`        | Total entity count                  |

### `input`

| Function                      | Description                              |
| ----------------------------- | ---------------------------------------- |
| `input.read_value(action)`    | Read action value (`number`/`vec3`/`boolean`) |
| `input.was_pressed(action)`   | True if pressed this frame               |
| `input.is_held(action)`       | True if currently held                   |
| `input.was_released(action)`  | True if released this frame              |

### `draw`

| Function                                  | Description                    |
| ----------------------------------------- | ------------------------------ |
| `draw.line(from, to)`                     | Debug line                     |
| `draw.ray(origin, direction)`             | Debug ray                      |
| `draw.arrow(from, to)`                    | Debug arrow                    |
| `draw.wire_sphere(center, radius)`        | Wireframe sphere               |
| `draw.wire_box(center, size)`             | Wireframe box                  |
| `draw.wire_capsule(start, end_pos, r)`    | Wireframe capsule              |
| `draw.solid_box(center, size)`            | Solid box                      |
| `draw.solid_circle(center, normal, r)`    | Solid circle                   |
| `draw.circle_xz(center, radius)`          | Circle on XZ plane             |
| `draw.label_2d(position, text)`           | 2D text label                  |
| `draw.set_color(r, g, b, a?)`            | Set draw color                 |
| `draw.with_duration(duration)`            | Set duration for next draws    |

### `math`

| Function                               | Description                       |
| -------------------------------------- | --------------------------------- |
| `math.cross(a, b)`                    | Cross product (vec3)              |
| `math.dot(a, b)`                      | Dot product (vec3)                |
| `math.normalize(v)`                   | Safe normalize (vec3)             |
| `math.distance(a, b)`                 | Distance (vec3)                   |
| `math.length(v)`                      | Length (vec3)                     |
| `math.length_sq(v)`                   | Squared length (vec3)             |
| `math.lerp(a, b, t)`                  | Linear interpolation              |
| `math.clamp_length(v, max_len)`       | Clamp vector length               |
| `math.project_on_plane(v, normal)`    | Project onto plane                |
| `math.reorient_on_plane(v, n, dir)`   | Reorient on plane                 |
| `math.rgb_to_hsv(rgb)`                | Returns h, s, v                   |
| `math.hsv_to_rgb(h, s, v)`            | Returns vec3                      |
| `math.rgb_to_oklab(rgb)`              | RGB to OKLab (vec3)               |
| `math.oklab_to_rgb(lab)`              | OKLab to RGB (vec3)               |

### `log`

| Function           | Description       |
| ------------------ | ----------------- |
| `log.debug(msg)`   | Log debug message |
| `log.info(msg)`    | Log info message  |
| `log.warning(msg)` | Log warning       |
| `log.error(msg)`   | Log error         |
| `log.trace(msg)`   | Log trace         |

## Types

| Type     | Lua representation                       |
| -------- | ---------------------------------------- |
| `entity` | integer                                  |
| `vec2`   | `{x: number, y: number}`                |
| `vec3`   | `{x: number, y: number, z: number}`     |
| `vec4`   | `{x: number, y: number, z: number, w: number}` |
| `quat`   | `{x: number, y: number, z: number, w: number}` |

## Type Stubs

LuaCATS type stubs are auto-generated to `Assets/StreamingAssets/lua/types/luagame.lua`. Regenerate via **Tools > Lua > Generate Type Stubs** (also runs automatically on domain reload when changes are detected).
