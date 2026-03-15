namespace UnityJS.Entities.PlayModeTests
{
  using System;
  using System.Collections;
  using System.Runtime.InteropServices;
  using System.Text;
  using Components;
  using Core;
  using NUnit.Framework;
  using QJS;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;

  public class JsEcsBenchmark
  {
    JsRuntimeManager m_Vm;
    World m_World;
    EntityManager m_EntityManager;

    static readonly string[] s_CompNames =
    {
      "BenchComp1",
      "BenchComp2",
      "BenchComp3",
      "BenchComp4",
      "BenchComp5",
    };

    static readonly ComponentType[] s_CompTypes =
    {
      ComponentType.ReadWrite<BenchComp1>(),
      ComponentType.ReadWrite<BenchComp2>(),
      ComponentType.ReadWrite<BenchComp3>(),
      ComponentType.ReadWrite<BenchComp4>(),
      ComponentType.ReadWrite<BenchComp5>(),
    };

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_EntityManager = m_World.EntityManager;

      // Yield two frames so JsSystemRunner.EnsureVmReady runs —
      // it registers all bridges, loads QueryBuilder (which exposes
      // globalThis._nativeQuery), and calls UpdateAllLookups.
      yield return null;
      yield return null;

      m_Vm = JsRuntimeManager.GetOrCreate();

      if (!JsEntityRegistry.IsCreated)
        JsEntityRegistry.Initialize(128_000);
      else
        JsEntityRegistry.Clear();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      JsEntityRegistry.Clear();
      var query = m_EntityManager.CreateEntityQuery(typeof(JsEntityId));
      m_EntityManager.DestroyEntity(query);

      yield return null;
    }

    #region Helpers

    void CreateBenchmarkEntities(int count, int componentCount)
    {
      var types = new NativeList<ComponentType>(componentCount + 1, Allocator.Temp);
      types.Add(ComponentType.ReadWrite<JsEntityId>());
      for (var i = 0; i < componentCount; i++)
        types.Add(s_CompTypes[i]);

      var archetype = m_EntityManager.CreateArchetype(types.AsArray());
      var entities = m_EntityManager.CreateEntity(archetype, count, Allocator.Temp);

      for (var i = 0; i < entities.Length; i++)
      {
        var id = JsEntityRegistry.AllocateId();
        JsEntityRegistry.RegisterImmediate(entities[i], id, m_EntityManager);
      }

      entities.Dispose();
      types.Dispose();
    }

    unsafe void EvalGlobal(string code)
    {
      var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var filenameBytes = Encoding.UTF8.GetBytes("<bench>\0");

      fixed (
        byte* pSource = sourceBytes,
          pFilename = filenameBytes
      )
      {
        var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename, QJS.JS_EVAL_TYPE_GLOBAL);
        if (QJS.IsException(val))
        {
          var ex = QJS.JS_GetException(m_Vm.Context);
          var pMsg = QJS.JS_ToCString(m_Vm.Context, ex);
          var msg = Marshal.PtrToStringUTF8((nint)pMsg) ?? "unknown error";
          QJS.JS_FreeCString(m_Vm.Context, pMsg);
          QJS.JS_FreeValue(m_Vm.Context, ex);
          Debug.LogError($"[Benchmark] EvalGlobal exception: {msg}");
        }

        QJS.JS_FreeValue(m_Vm.Context, val);
      }
    }

    static string BuildReadWriteJs(int compCount)
    {
      var sb = new StringBuilder();
      sb.Append("(function() {");
      sb.Append("var q = ecs.query()");
      for (var i = 0; i < compCount; i++)
        sb.Append($".withAll({s_CompNames[i]})");
      sb.Append(".build();");
      sb.Append("for (const [eid");
      for (var i = 0; i < compCount; i++)
        sb.Append($", c{i}");
      sb.Append("] of q) {");
      sb.Append("  c0.a += 1;");
      sb.Append('}');
      sb.Append("})()");
      return sb.ToString();
    }

    static (int warmup, int iterations) GetIterationCounts(int entityCount)
    {
      return entityCount switch
      {
        <= 100 => (10, 100),
        <= 10_000 => (3, 20),
        _ => (2, 10),
      };
    }

    static string MakeLabel(string op, int entityCount, int componentCount)
    {
      return $"{op} entities={entityCount} comps={componentCount}";
    }

    #endregion

    #region Benchmark Tests

    [UnityTest]
    public IEnumerator Bridged_ReadWrite(
      [Values(10, 100, 10_000, 100_000)] int entityCount,
      [Values(1, 2, 3, 4, 5)] int componentCount
    )
    {
      CreateBenchmarkEntities(entityCount, componentCount);
      yield return null;

      var js = BuildReadWriteJs(componentCount);
      var (warmup, iterations) = GetIterationCounts(entityCount);

      BenchmarkHarness.Measure(
        MakeLabel("ReadWrite", entityCount, componentCount),
        warmup,
        iterations,
        () => EvalGlobal(js)
      );

      yield return null;
    }

    [UnityTest]
    public IEnumerator Bridged_ReadWriteRoundTrip()
    {
      const int count = 10;
      CreateBenchmarkEntities(count, 2);

      // Set known initial values
      var q = m_EntityManager.CreateEntityQuery(
        ComponentType.ReadWrite<BenchComp1>(),
        ComponentType.ReadWrite<BenchComp2>()
      );
      var entities = q.ToEntityArray(Allocator.Temp);
      for (var i = 0; i < entities.Length; i++)
      {
        m_EntityManager.SetComponentData(entities[i], new BenchComp1 { a = 1f });
        m_EntityManager.SetComponentData(entities[i], new BenchComp2 { a = 2f, b = 3f });
      }

      entities.Dispose();
      q.Dispose();

      // PrecomputeQueryResults needs a frame to pick up entities
      yield return null;
      yield return null;

      // JS: read + write via query builder for-of (per-entity set path)
      EvalGlobal(
        @"(function() {
    var q = ecs.query().withAll(BenchComp1).withAll(BenchComp2).build();
    for (const [eid, c1, c2] of q) {
      c1.a += 10;
      c2.b += 100;
    }
  })()"
      );

      // Assert writes persisted to C#
      var q2 = m_EntityManager.CreateEntityQuery(
        ComponentType.ReadWrite<BenchComp1>(),
        ComponentType.ReadWrite<BenchComp2>()
      );
      var ents = q2.ToEntityArray(Allocator.Temp);
      Assert.AreEqual(count, ents.Length);
      for (var i = 0; i < ents.Length; i++)
      {
        var c1 = m_EntityManager.GetComponentData<BenchComp1>(ents[i]);
        var c2 = m_EntityManager.GetComponentData<BenchComp2>(ents[i]);
        Assert.AreEqual(11f, c1.a, 0.001f, $"BenchComp1.a entity {i}");
        Assert.AreEqual(2f, c2.a, 0.001f, $"BenchComp2.a entity {i}");
        Assert.AreEqual(103f, c2.b, 0.001f, $"BenchComp2.b entity {i}");
      }

      ents.Dispose();
      q2.Dispose();
      yield return null;
    }

    #endregion
  }
}
