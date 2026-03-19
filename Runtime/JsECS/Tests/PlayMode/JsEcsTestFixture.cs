namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using System.Collections.Generic;
  using Components;
  using Core;
  using NUnit.Framework;
  using QJS;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityEngine.TestTools;

  /// <summary>
  /// Test fixture for bridge tests that need a real ECS World + JsEntityRegistry.
  /// Shares the singleton JsRuntimeManager with the DefaultWorld system pipeline
  /// (JsScriptingSystem / JsSystemRunner) so bridge tests don't poison the VM state
  /// that E2E tests depend on.
  /// </summary>
  public unsafe class JsEcsTestFixture
  {
    protected JsRuntimeManager m_Manager;
    protected JSContext Ctx => m_Manager.Context;
    protected World m_World;
    protected EntityManager m_Em;
    protected EntityCommandBuffer m_Ecb;
    protected bool m_EcbActive;
    protected readonly List<Entity> m_Entities = new();

    [UnitySetUp]
    public virtual IEnumerator SetUp()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_Em = m_World.EntityManager;

      // Ensure the VM + system pipeline is fully initialized by ticking two frames.
      // Frame 1: JsScriptingSystem.OnStartRunning → GetOrCreate, JsSystemRunner.EnsureVmReady
      //          registers all bridges + loads glue scripts.
      // Frame 2: FlushPendingQueries + PrecomputeQueryResults.
      m_Manager = JsRuntimeManager.GetOrCreate();
      yield return null;
      yield return null;

      // Re-acquire in case the system pipeline recreated the VM
      m_Manager = JsRuntimeManager.Instance ?? m_Manager;

      JsECSBridge.Initialize(m_World);

      if (!JsEntityRegistry.IsCreated)
        JsEntityRegistry.Initialize(1024);
    }

    [UnityTearDown]
    public virtual IEnumerator TearDown()
    {
      // 1. Clean up ECB
      if (m_EcbActive)
      {
        JsECSBridge.ClearBurstContext();
        m_Ecb.Dispose();
        m_EcbActive = false;
      }

      // 2. Destroy tracked entities
      foreach (var e in m_Entities)
        if (m_Em.Exists(e))
          m_Em.DestroyEntity(e);
      m_Entities.Clear();

      // 3. Bridge-specific cleanup hook
      CleanupBridgeState();

      // 4. Clear burst context
      JsECSBridge.ClearBurstContext();

      // We don't own the VM — JsScriptingSystem does. Just null out our reference.
      m_Manager = null;

      yield return null;
    }

    protected virtual void CleanupBridgeState() { }

    #region Entity Helpers

    protected Entity CreateRegisteredEntity(params ComponentType[] types)
    {
      var entity = m_Em.CreateEntity(types);
      m_Em.AddComponentData(entity, LocalTransform.FromPosition(float3.zero));
      var id = JsEntityRegistry.AllocateId();
      JsEntityRegistry.RegisterImmediate(entity, id, m_Em);
      m_Entities.Add(entity);
      return entity;
    }

    protected int GetEntityId(Entity entity) => JsEntityRegistry.GetIdFromEntity(entity);

    #endregion

    #region ECB Helpers

    protected void SetupBurstContext()
    {
      if (m_EcbActive)
        return;
      m_Ecb = new EntityCommandBuffer(Allocator.TempJob);
      m_EcbActive = true;
      JsECSBridge.UpdateBurstContext(m_Ecb, 0f, default, default);
    }

    protected void PlaybackAndCleanupEcb()
    {
      JsECSBridge.ClearBurstContext();
      if (m_EcbActive)
      {
        m_Ecb.Playback(m_Em);
        m_Ecb.Dispose();
        m_EcbActive = false;
      }
    }

    #endregion

    #region Eval Helpers

    protected JSValue EvalGlobal(string code)
    {
      var sourceBytes = System.Text.Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var fileBytes = System.Text.Encoding.UTF8.GetBytes("<test>\0");
      fixed (
        byte* pSrc = sourceBytes,
          pFile = fileBytes
      )
      {
        var result = QJS.JS_Eval(Ctx, pSrc, sourceLen, pFile, QJS.JS_EVAL_TYPE_GLOBAL);
        if (QJS.IsException(result))
        {
          var exc = QJS.JS_GetException(Ctx);
          var eptr = QJS.JS_ToCString(Ctx, exc);
          var emsg =
            System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)eptr) ?? "unknown error";
          QJS.JS_FreeCString(Ctx, eptr);
          QJS.JS_FreeValue(Ctx, exc);
          Assert.Fail($"JS exception: {emsg}");
        }

        return result;
      }
    }

    protected void EvalVoid(string code)
    {
      var result = EvalGlobal(code);
      QJS.JS_FreeValue(Ctx, result);
    }

    protected int EvalInt(string code)
    {
      var result = EvalGlobal(code);
      int val;
      QJS.JS_ToInt32(Ctx, &val, result);
      QJS.JS_FreeValue(Ctx, result);
      return val;
    }

    protected double EvalFloat(string code)
    {
      var result = EvalGlobal(code);
      double val;
      QJS.JS_ToFloat64(Ctx, &val, result);
      QJS.JS_FreeValue(Ctx, result);
      return val;
    }

    protected bool EvalBool(string code)
    {
      var result = EvalGlobal(code);
      var val = QJS.JS_ToBool(Ctx, result);
      QJS.JS_FreeValue(Ctx, result);
      return val != 0;
    }

    #endregion
  }
}
