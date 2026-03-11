namespace UnityJS.Entities.Core
{
	using System.Threading;
	using Components;
	using UnityJS.QJS;
	using UnityJS.Runtime;
	using Unity.Burst;
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
		struct BurstContextMarker { }

		struct NextEntityIdMarker { }

		struct EventContextMarker { }

		struct PendingEntitiesMarker { }

		static World s_world;
		static EntityManager s_entityManager;
		static EntityQuery s_playerQuery;
		static bool s_playerQueryInitialized;
		static bool s_initialized;

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
			RegisterTransformFunctions(ctx);
			RegisterSpatialFunctions(ctx);
			RegisterEventsFunctions(ctx);
			RegisterLogFunctions(ctx);
			RegisterInputFunctions(ctx);
			RegisterDrawFunctions(ctx);
			RegisterMathFunctions(ctx);

			JsFunctionRegistry.RegisterAll(ctx);
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
			{
				if (scripts[i].scriptName == scriptName)
					return true;
			}

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

		/// <summary>
		/// Helper: set JS_UNDEFINED on the output pointers of a QJSShimCallback.
		/// </summary>
		static unsafe void SetUndefined(long* outU, long* outTag)
		{
			var undef = QJS.JS_UNDEFINED;
			*outU = undef.u;
			*outTag = undef.tag;
		}
	}
}
