namespace UnityJS.Entities.Core
{
  using System.Threading;
  using Components;
  using QJS;
  using Runtime;
  using Unity.Burst;
  using static Runtime.QJSHelpers;
  using Unity.Collections;
  using Unity.Collections.LowLevel.Unsafe;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;

  public struct PendingEntityCreation
  {
    public int entityId;
    public float3 position;
  }

  public struct PendingScriptAddition
  {
    public int entityId;
    public FixedString64Bytes scriptName;
  }

  /// <summary>
  /// Unmanaged struct for pending event dispatch information.
  /// </summary>
  public struct PendingEventDispatch
  {
    public Entity entity;
    public int scriptIndex;
    public FixedString64Bytes scriptName;
    public int entityIndex;
    public int stateRef;
    public int eventStartIndex;
    public int eventCount;
  }

  /// <summary>
  /// Unmanaged event context for SharedStatic storage.
  /// </summary>
  public struct JsEventContextData
  {
    public UnsafeList<PendingEventDispatch> pendingEvents;
    public UnsafeList<JsEvent> eventBuffer;
    public UnsafeList<Entity> entitiesToClear;
    public bool isValid;
  }

  /// <summary>
  /// Unmanaged context for Burst-compiled bridge functions.
  /// Updated each frame before script execution.
  /// </summary>
  public unsafe struct BurstBridgeContext
  {
    /// <summary>
    /// Direct ECB for deferred entity operations. Set each frame from EndSimulationEntityCommandBufferSystem.
    /// </summary>
    public EntityCommandBuffer ecb;

    [NativeDisableUnsafePtrRestriction]
    public UnsafeHashMap<int, Entity> entityIdMap;

    [NativeDisableUnsafePtrRestriction]
    public ComponentLookup<LocalTransform> transformLookup;

    [NativeDisableUnsafePtrRestriction]
    public BufferLookup<JsScript> scriptBufferLookup;

    /// <summary>
    /// Delta time for the current frame, used by movement functions.
    /// </summary>
    public float deltaTime;

    public bool isValid;
  }

  /// <summary>
  /// Coordinates JS-to-ECS bridge functions.
  /// Static state required for Burst-compatible [MonoPInvokeCallback] methods.
  /// Domain-specific functions are organized in partial classes under Bridge/.
  /// </summary>
  public static partial class JsECSBridge
  {
    static JsBridgeState B => Runtime.JsRuntimeManager.Instance?.BridgeState as JsBridgeState;

    struct BurstContextMarker { }

    struct NextEntityIdMarker { }

    struct EventContextMarker { }

    struct PendingEntitiesMarker { }

    static World s_world;
    static EntityManager s_entityManager;
    static EntityQuery s_playerQuery;
    static bool s_playerQueryInitialized;
    static bool s_initialized;

    [UnityEngine.RuntimeInitializeOnLoadMethod(
      UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration
    )]
    static void ResetSession()
    {
      s_world = null;
      s_entityManager = default;
      s_playerQueryInitialized = false;
      s_initialized = false;
    }

    /// <summary>
    /// Tracks entities created in the current frame before ECB playback.
    /// </summary>
    static readonly SharedStatic<UnsafeHashMap<int, Entity>> s_pendingEntities = SharedStatic<
      UnsafeHashMap<int, Entity>
    >.GetOrCreate<PendingEntitiesMarker, UnsafeHashMap<int, Entity>>();

    static readonly SharedStatic<BurstBridgeContext> s_burstContext =
      SharedStatic<BurstBridgeContext>.GetOrCreate<BurstContextMarker, BurstBridgeContext>();

    static readonly SharedStatic<int> s_nextEntityId =
      SharedStatic<int>.GetOrCreate<NextEntityIdMarker>();

    static readonly SharedStatic<JsEventContextData> s_eventContext =
      SharedStatic<JsEventContextData>.GetOrCreate<EventContextMarker, JsEventContextData>();

    public static ref JsEventContextData EventContext => ref s_eventContext.Data;

    public static void Initialize(World world)
    {
      if (s_initialized)
      {
        s_world = world;
        s_entityManager = world.EntityManager;
        return;
      }

      s_world = world;
      s_entityManager = world.EntityManager;
      s_nextEntityId.Data = 1;

      s_pendingEntities.Data = new UnsafeHashMap<int, Entity>(32, Allocator.Persistent);

      s_eventContext.Data = new JsEventContextData
      {
        pendingEvents = new UnsafeList<PendingEventDispatch>(64, Allocator.Persistent),
        eventBuffer = new UnsafeList<JsEvent>(128, Allocator.Persistent),
        entitiesToClear = new UnsafeList<Entity>(64, Allocator.Persistent),
        isValid = true,
      };

      if (s_playerQueryInitialized)
        s_playerQuery.Dispose();

      s_playerQuery = s_entityManager.CreateEntityQuery(ComponentType.ReadOnly<JsPlayerTag>());
      s_playerQueryInitialized = true;
      s_initialized = true;
    }

    public static void Shutdown()
    {
      s_burstContext.Data = default;

      if (s_pendingEntities.Data.IsCreated)
        s_pendingEntities.Data.Dispose();

      ref var eventCtx = ref s_eventContext.Data;
      if (eventCtx.isValid)
      {
        if (eventCtx.pendingEvents.IsCreated)
          eventCtx.pendingEvents.Dispose();
        if (eventCtx.eventBuffer.IsCreated)
          eventCtx.eventBuffer.Dispose();
        if (eventCtx.entitiesToClear.IsCreated)
          eventCtx.entitiesToClear.Dispose();
        eventCtx = default;
      }

      if (s_playerQueryInitialized)
      {
        s_playerQuery.Dispose();
        s_playerQueryInitialized = false;
      }

      s_playerQuery = default;

      s_world = null;
      s_entityManager = default;
      s_initialized = false;
    }

    /// <summary>
    /// Updates the Burst-compatible context with current frame data.
    /// </summary>
    public static void UpdateBurstContext(
      EntityCommandBuffer ecb,
      float deltaTime,
      ComponentLookup<LocalTransform> transformLookup,
      BufferLookup<JsScript> scriptBufferLookup
    )
    {
      if (!s_initialized)
      {
        s_burstContext.Data = default;
        return;
      }

      if (!JsEntityRegistry.IsCreated)
      {
        s_burstContext.Data = default;
        return;
      }

      if (s_pendingEntities.Data.IsCreated)
        s_pendingEntities.Data.Clear();

      s_burstContext.Data = new BurstBridgeContext
      {
        ecb = ecb,
        deltaTime = deltaTime,
        entityIdMap = JsEntityRegistry.EntityIdMap,
        transformLookup = transformLookup,
        scriptBufferLookup = scriptBufferLookup,
        isValid = true,
      };
    }

    internal static void AddPendingEntity(int entityId, Entity entity)
    {
      if (s_pendingEntities.Data.IsCreated)
        s_pendingEntities.Data.TryAdd(entityId, entity);
    }

    internal static Entity GetPendingEntity(int entityId)
    {
      if (
        s_pendingEntities.Data.IsCreated
        && s_pendingEntities.Data.TryGetValue(entityId, out var entity)
      )
        return entity;
      return Entity.Null;
    }

    public static bool IsPendingEntity(int entityId)
    {
      return s_pendingEntities.Data.IsCreated && s_pendingEntities.Data.ContainsKey(entityId);
    }

    public static bool TryGetBurstContextECB(out EntityCommandBuffer ecb)
    {
      ref var ctx = ref s_burstContext.Data;
      if (ctx.isValid)
      {
        ecb = ctx.ecb;
        return true;
      }

      ecb = default;
      return false;
    }

    public static void ClearBurstContext()
    {
      s_burstContext.Data = default;
    }

    public static void RegisterFunctions(JSContext ctx)
    {
      RegisterEntitiesFunctions(ctx);
      RegisterLogFunctions(ctx);
      RegisterMathBootstrap(ctx);

      JsFunctionRegistry.RegisterAll(ctx);
    }

    static unsafe void RegisterMathBootstrap(JSContext ctx)
    {
      const string bootstrap =
        @"(function() {
  var m = globalThis.math || {};
  m.PI = 3.14159265358979;
  m.E = 2.71828182845905;
  m.EPSILON = 1.1920928955078125e-7;
  m.INFINITY = Infinity;
  m.random = Math.random;
  globalThis.math = m;

  var F2P = {
    add: function(b) { return typeof b === 'number' ? float2(this.x+b, this.y+b) : float2(this.x+b.x, this.y+b.y); },
    sub: function(b) { return typeof b === 'number' ? float2(this.x-b, this.y-b) : float2(this.x-b.x, this.y-b.y); },
    mul: function(b) { return typeof b === 'number' ? float2(this.x*b, this.y*b) : float2(this.x*b.x, this.y*b.y); },
    div: function(b) { return typeof b === 'number' ? float2(this.x/b, this.y/b) : float2(this.x/b.x, this.y/b.y); },
    equals: function(b) { if (typeof b === 'number') b = {x:b, y:b}; return Math.abs(this.x-b.x) < 1e-6 && Math.abs(this.y-b.y) < 1e-6; }
  };
  globalThis.float2 = function(x, y) {
    if (x === undefined) x = 0;
    var o = Object.create(F2P);
    if (typeof x === 'object') { o.x = x.x; o.y = x.y; }
    else { o.x = x; o.y = y !== undefined ? y : x; }
    return o;
  };

  var F3P = {
    add: function(b) { return typeof b === 'number' ? float3(this.x+b, this.y+b, this.z+b) : float3(this.x+b.x, this.y+b.y, this.z+b.z); },
    sub: function(b) { return typeof b === 'number' ? float3(this.x-b, this.y-b, this.z-b) : float3(this.x-b.x, this.y-b.y, this.z-b.z); },
    mul: function(b) { return typeof b === 'number' ? float3(this.x*b, this.y*b, this.z*b) : float3(this.x*b.x, this.y*b.y, this.z*b.z); },
    div: function(b) { return typeof b === 'number' ? float3(this.x/b, this.y/b, this.z/b) : float3(this.x/b.x, this.y/b.y, this.z/b.z); },
    equals: function(b) { if (typeof b === 'number') b = {x:b, y:b, z:b}; return Math.abs(this.x-b.x) < 1e-6 && Math.abs(this.y-b.y) < 1e-6 && Math.abs(this.z-b.z) < 1e-6; }
  };
  globalThis.float3 = function(x, y, z) {
    if (x === undefined) x = 0;
    var o = Object.create(F3P);
    if (typeof x === 'object') { o.x = x.x; o.y = x.y; o.z = x.z; }
    else { o.x = x; o.y = y !== undefined ? y : x; o.z = z !== undefined ? z : x; }
    return o;
  };

  var F4P = {
    add: function(b) { return typeof b === 'number' ? float4(this.x+b, this.y+b, this.z+b, this.w+b) : float4(this.x+b.x, this.y+b.y, this.z+b.z, this.w+b.w); },
    sub: function(b) { return typeof b === 'number' ? float4(this.x-b, this.y-b, this.z-b, this.w-b) : float4(this.x-b.x, this.y-b.y, this.z-b.z, this.w-b.w); },
    mul: function(b) { return typeof b === 'number' ? float4(this.x*b, this.y*b, this.z*b, this.w*b) : float4(this.x*b.x, this.y*b.y, this.z*b.z, this.w*b.w); },
    div: function(b) { return typeof b === 'number' ? float4(this.x/b, this.y/b, this.z/b, this.w/b) : float4(this.x/b.x, this.y/b.y, this.z/b.z, this.w/b.w); },
    equals: function(b) { if (typeof b === 'number') b = {x:b, y:b, z:b, w:b}; return Math.abs(this.x-b.x) < 1e-6 && Math.abs(this.y-b.y) < 1e-6 && Math.abs(this.z-b.z) < 1e-6 && Math.abs(this.w-b.w) < 1e-6; }
  };
  globalThis.float4 = function(x, y, z, w) {
    if (x === undefined) x = 0;
    var o = Object.create(F4P);
    if (typeof x === 'object') { o.x = x.x; o.y = x.y; o.z = x.z; o.w = x.w; }
    else { o.x = x; o.y = y !== undefined ? y : x; o.z = z !== undefined ? z : x; o.w = w !== undefined ? w : x; }
    return o;
  };

  (function() {
    function _uniq() {
      for (var i = 0; i < arguments.length; i++)
        for (var j = i + 1; j < arguments.length; j++)
          if (arguments[i] === arguments[j]) return false;
      return true;
    }
    function _swz(proto, comps) {
      var n = comps.length;
      for (let i = 0; i < n; i++)
        for (let j = 0; j < n; j++) {
          let a = comps[i], b = comps[j];
          var desc2 = {get:function(){return float2(this[a],this[b])}};
          if (_uniq(i,j)) desc2.set = function(v){this[a]=v.x;this[b]=v.y};
          Object.defineProperty(proto, a+b, desc2);
          for (let k = 0; k < n; k++) {
            let c = comps[k];
            var desc3 = {get:function(){return float3(this[a],this[b],this[c])}};
            if (_uniq(i,j,k)) desc3.set = function(v){this[a]=v.x;this[b]=v.y;this[c]=v.z};
            Object.defineProperty(proto, a+b+c, desc3);
            for (let l = 0; l < n; l++) {
              let d = comps[l];
              var desc4 = {get:function(){return float4(this[a],this[b],this[c],this[d])}};
              if (_uniq(i,j,k,l)) desc4.set = function(v){this[a]=v.x;this[b]=v.y;this[c]=v.z;this[d]=v.w};
              Object.defineProperty(proto, a+b+c+d, desc4);
            }
          }
        }
    }
    _swz(F2P, ['x','y']);
    _swz(F3P, ['x','y','z']);
    _swz(F4P, ['x','y','z','w']);
  })();

  float2.zero = float2(0, 0);
  float3.zero = float3(0, 0, 0);
  float4.zero = float4(0, 0, 0, 0);
  float2.one = float2(1, 1);
  float3.one = float3(1, 1, 1);
  float4.one = float4(1, 1, 1, 1);

  function _isNum(v) { return typeof v === 'number'; }
  globalThis.add = function(a, b) {
    if (_isNum(a)) a = { x: a, y: a, z: a, w: a };
    if (_isNum(b)) b = { x: b, y: b, z: b, w: b };
    if (a.w !== undefined) return float4(a.x+b.x, a.y+b.y, a.z+b.z, a.w+b.w);
    if (a.z !== undefined) return float3(a.x+b.x, a.y+b.y, a.z+b.z);
    return float2(a.x+b.x, a.y+b.y);
  };
  globalThis.sub = function(a, b) {
    if (_isNum(a)) a = { x: a, y: a, z: a, w: a };
    if (_isNum(b)) b = { x: b, y: b, z: b, w: b };
    if (a.w !== undefined) return float4(a.x-b.x, a.y-b.y, a.z-b.z, a.w-b.w);
    if (a.z !== undefined) return float3(a.x-b.x, a.y-b.y, a.z-b.z);
    return float2(a.x-b.x, a.y-b.y);
  };
  globalThis.mul = function(a, b) {
    if (_isNum(a)) a = { x: a, y: a, z: a, w: a };
    if (_isNum(b)) b = { x: b, y: b, z: b, w: b };
    if (a.w !== undefined) return float4(a.x*b.x, a.y*b.y, a.z*b.z, a.w*b.w);
    if (a.z !== undefined) return float3(a.x*b.x, a.y*b.y, a.z*b.z);
    return float2(a.x*b.x, a.y*b.y);
  };
  globalThis.div = function(a, b) {
    if (_isNum(a)) a = { x: a, y: a, z: a, w: a };
    if (_isNum(b)) b = { x: b, y: b, z: b, w: b };
    if (a.w !== undefined) return float4(a.x/b.x, a.y/b.y, a.z/b.z, a.w/b.w);
    if (a.z !== undefined) return float3(a.x/b.x, a.y/b.y, a.z/b.z);
    return float2(a.x/b.x, a.y/b.y);
  };
  globalThis.eq = function(a, b) {
    if (_isNum(a)) a = { x: a, y: a, z: a, w: a };
    if (_isNum(b)) b = { x: b, y: b, z: b, w: b };
    if (a.w !== undefined) return Math.abs(a.x-b.x) < 1e-6 && Math.abs(a.y-b.y) < 1e-6 && Math.abs(a.z-b.z) < 1e-6 && Math.abs(a.w-b.w) < 1e-6;
    if (a.z !== undefined) return Math.abs(a.x-b.x) < 1e-6 && Math.abs(a.y-b.y) < 1e-6 && Math.abs(a.z-b.z) < 1e-6;
    return Math.abs(a.x-b.x) < 1e-6 && Math.abs(a.y-b.y) < 1e-6;
  };
  globalThis.__F2P = F2P;
  globalThis.__F3P = F3P;
  globalThis.__F4P = F4P;
})();";

      var result = QJS.EvalGlobal(ctx, bootstrap, "<math_bootstrap>");
      if (QJS.IsException(result))
        Unity.Logging.Log.Error(
          "[JsECS] Failed to initialize math bootstrap: {0}",
          QJS.GetExceptionMessage(ctx)
        );

      QJS.JS_FreeValue(ctx, result);

      // Cache vector prototypes for JsStateExtensions
      var f2pBytes = QJS.U8("__F2P");
      var f3pBytes = QJS.U8("__F3P");
      var f4pBytes = QJS.U8("__F4P");
      var global = QJS.JS_GetGlobalObject(ctx);
      fixed (
        byte* pF2 = f2pBytes,
          pF3 = f3pBytes,
          pF4 = f4pBytes
      )
      {
        var f2p = QJS.JS_GetPropertyStr(ctx, global, pF2);
        var f3p = QJS.JS_GetPropertyStr(ctx, global, pF3);
        var f4p = QJS.JS_GetPropertyStr(ctx, global, pF4);
        JsStateExtensions.SetVectorPrototypes(f2p, f3p, f4p);
      }

      QJS.JS_FreeValue(ctx, global);
    }

    /// <summary>
    /// Burst-compatible entity lookup from entity ID.
    /// </summary>
    public static Entity GetEntityFromIdBurst(int entityId)
    {
      ref var ctx = ref s_burstContext.Data;
      if (!ctx.isValid || entityId <= 0)
        return Entity.Null;

      if (ctx.entityIdMap.TryGetValue(entityId, out var entity) && entity != Entity.Null)
        return entity;

      return GetPendingEntity(entityId);
    }

    internal static bool TryGetTransformBurst(Entity entity, out LocalTransform transform)
    {
      ref var ctx = ref s_burstContext.Data;
      transform = default;

      if (!ctx.isValid || entity == Entity.Null)
        return false;

      if (entity.Index < 0)
        return false;

      if (!ctx.transformLookup.HasComponent(entity))
        return false;

      transform = ctx.transformLookup[entity];
      return true;
    }

    internal static bool TrySetTransformBurst(Entity entity, LocalTransform transform)
    {
      ref var ctx = ref s_burstContext.Data;

      if (!ctx.isValid || entity == Entity.Null)
        return false;

      if (entity.Index < 0)
        return false;

      if (!ctx.transformLookup.HasComponent(entity))
        return false;

      ctx.transformLookup[entity] = transform;
      return true;
    }

    internal static int AllocateEntityId()
    {
      return Interlocked.Increment(ref s_nextEntityId.Data);
    }

    internal static bool HasScriptBurst(Entity entity, FixedString64Bytes scriptName)
    {
      ref var ctx = ref s_burstContext.Data;
      if (!ctx.isValid || entity == Entity.Null)
        return false;

      if (entity.Index < 0)
        return false;

      if (!ctx.scriptBufferLookup.HasBuffer(entity))
        return false;

      var scripts = ctx.scriptBufferLookup[entity];
      for (var i = 0; i < scripts.Length; i++)
        if (scripts[i].scriptName == scriptName)
          return true;

      return false;
    }

    public static void SyncNextEntityId(int nextId)
    {
      var current = s_nextEntityId.Data;
      while (current < nextId)
      {
        var prev = Interlocked.CompareExchange(ref s_nextEntityId.Data, nextId, current);
        if (prev == current)
          break;
        current = prev;
      }
    }

    public static void ClearEventContext()
    {
      ref var ctx = ref s_eventContext.Data;
      if (!ctx.isValid)
        return;

      ctx.pendingEvents.Clear();
      ctx.eventBuffer.Clear();
      ctx.entitiesToClear.Clear();
    }

    public static void AddEventDispatch(
      Entity entity,
      int scriptIndex,
      FixedString64Bytes scriptName,
      int entityIndex,
      int stateRef,
      int eventStartIndex,
      int eventCount
    )
    {
      ref var ctx = ref s_eventContext.Data;
      if (!ctx.isValid)
        return;

      ctx.pendingEvents.Add(
        new PendingEventDispatch
        {
          entity = entity,
          scriptIndex = scriptIndex,
          scriptName = scriptName,
          entityIndex = entityIndex,
          stateRef = stateRef,
          eventStartIndex = eventStartIndex,
          eventCount = eventCount,
        }
      );
    }

    public static int AddEvent(JsEvent evt)
    {
      ref var ctx = ref s_eventContext.Data;
      if (!ctx.isValid)
        return -1;

      var index = ctx.eventBuffer.Length;
      ctx.eventBuffer.Add(evt);
      return index;
    }

    public static void AddEntityToClear(Entity entity)
    {
      ref var ctx = ref s_eventContext.Data;
      if (!ctx.isValid)
        return;

      ctx.entitiesToClear.Add(entity);
    }

    public static JsEvent GetEvent(int index)
    {
      ref var ctx = ref s_eventContext.Data;
      if (!ctx.isValid || index < 0 || index >= ctx.eventBuffer.Length)
        return default;

      return ctx.eventBuffer[index];
    }

  }
}
