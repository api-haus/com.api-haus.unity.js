namespace UnityJS.Runtime
{
  using System.Collections.Generic;
  using QJS;

  /// <summary>
  /// Manages JS entity state objects — creation, release, validation, and timing updates.
  /// Each state object is a JSValue holding { entityId, deltaTime, elapsedTime, _script }.
  /// Extracted from JsRuntimeManager.
  /// </summary>
  public class JsEntityStateStore
  {
    static readonly byte[] s_script = QJS.U8("_script");
    static readonly byte[] s_entityId = QJS.U8("entityId");
    static readonly byte[] s_deltaTime = QJS.U8("deltaTime");
    static readonly byte[] s_elapsedTime = QJS.U8("elapsedTime");

    readonly JsRuntimeManager m_Owner;
    int m_NextStateId = 1;
    internal readonly Dictionary<int, JSValue> Refs = new();

    internal JsEntityStateStore(JsRuntimeManager owner)
    {
      m_Owner = owner;
    }

    public unsafe int Create(string scriptName, int entityId)
    {
      var ctx = m_Owner.Context;
      var state = QJS.JS_NewObject(ctx);

      fixed (
        byte* pEntityId = s_entityId,
          pDeltaTime = s_deltaTime,
          pElapsedTime = s_elapsedTime,
          pScript = s_script
      )
      {
        QJS.JS_SetPropertyStr(ctx, state, pEntityId, QJS.NewInt32(ctx, entityId));
        QJS.JS_SetPropertyStr(ctx, state, pDeltaTime, QJS.NewFloat64(ctx, 0.0));
        QJS.JS_SetPropertyStr(ctx, state, pElapsedTime, QJS.NewFloat64(ctx, 0.0));

        var nameBytes = m_Owner.GetOrCacheBytes(scriptName);
        fixed (byte* pName = nameBytes)
        {
          var nameVal = QJS.JS_NewString(ctx, pName);
          QJS.JS_SetPropertyStr(ctx, state, pScript, nameVal);
        }
      }

      var id = m_NextStateId++;
      Refs[id] = QJS.JS_DupValue(ctx, state);
      QJS.JS_FreeValue(ctx, state);
      return id;
    }

    public void Release(int stateRef)
    {
      if (Refs.Remove(stateRef, out var val))
        QJS.JS_FreeValue(m_Owner.Context, val);
    }

    public bool Validate(int stateRef)
    {
      return Refs.ContainsKey(stateRef);
    }

    public unsafe void UpdateTimings(int stateRef, float deltaTime, double elapsedTime)
    {
      if (!Refs.TryGetValue(stateRef, out var stateVal))
        return;

      var ctx = m_Owner.Context;
      fixed (
        byte* pDt = s_deltaTime,
          pElapsed = s_elapsedTime
      )
      {
        QJS.JS_SetPropertyStr(ctx, stateVal, pDt, QJS.NewFloat64(ctx, deltaTime));
        QJS.JS_SetPropertyStr(ctx, stateVal, pElapsed, QJS.NewFloat64(ctx, elapsedTime));
      }
    }

    internal void DisposeAll()
    {
      var ctx = m_Owner.Context;
      foreach (var kv in Refs)
        QJS.JS_FreeValue(ctx, kv.Value);
      Refs.Clear();
    }
  }
}
