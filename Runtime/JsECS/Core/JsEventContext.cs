namespace UnityJS.Entities.Core
{
  using Components;
  using Unity.Burst;
  using Unity.Collections;
  using Unity.Collections.LowLevel.Unsafe;
  using Unity.Entities;

  /// <summary>
  /// Manages the event dispatch buffer — pending events, event data, and entity cleanup lists.
  /// Extracted from JsECSBridge to isolate event context concerns.
  /// </summary>
  public static class JsEventContext
  {
    struct EventContextMarker { }

    static readonly SharedStatic<JsEventContextData> s_eventContext =
      SharedStatic<JsEventContextData>.GetOrCreate<EventContextMarker, JsEventContextData>();

    public static ref JsEventContextData Data => ref s_eventContext.Data;

    internal static void Initialize()
    {
      s_eventContext.Data = new JsEventContextData
      {
        pendingEvents = new UnsafeList<PendingEventDispatch>(64, Allocator.Persistent),
        eventBuffer = new UnsafeList<JsEvent>(128, Allocator.Persistent),
        entitiesToClear = new UnsafeList<Entity>(64, Allocator.Persistent),
        isValid = true,
      };
    }

    internal static void Shutdown()
    {
      ref var ctx = ref s_eventContext.Data;
      if (!ctx.isValid)
        return;

      if (ctx.pendingEvents.IsCreated)
        ctx.pendingEvents.Dispose();
      if (ctx.eventBuffer.IsCreated)
        ctx.eventBuffer.Dispose();
      if (ctx.entitiesToClear.IsCreated)
        ctx.entitiesToClear.Dispose();
      ctx = default;
    }

    public static void Clear()
    {
      ref var ctx = ref s_eventContext.Data;
      if (!ctx.isValid)
        return;

      ctx.pendingEvents.Clear();
      ctx.eventBuffer.Clear();
      ctx.entitiesToClear.Clear();
    }

    public static void AddDispatch(
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
