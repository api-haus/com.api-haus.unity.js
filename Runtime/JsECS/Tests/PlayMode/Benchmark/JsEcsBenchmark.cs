namespace UnityJS.Entities.PlayModeTests
{
  using System;
  using System.Collections;
  using System.Runtime.InteropServices;
  using System.Text;
  using Components;
  using Core;
  using NUnit.Framework;
  using Unity.Collections;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.QJS;
  using UnityJS.Runtime;

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
        var val = QJS.JS_Eval(
          m_Vm.Context,
          pSource,
          sourceLen,
          pFilename,
          QJS.JS_EVAL_TYPE_GLOBAL
        );
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

    static string BuildQueryOnlyJs(int compCount)
    {
      var names = new StringBuilder();
      for (var i = 0; i < compCount; i++)
      {
        if (i > 0)
          names.Append(", ");
        names.Append('"');
        names.Append(s_CompNames[i]);
        names.Append('"');
      }

      return $"(function() {{ var r = globalThis._nativeQuery({names}); return r.length; }})()";
    }

    static string BuildIterateReadJs(int compCount)
    {
      var sb = new StringBuilder();
      sb.Append("(function() {");
      sb.Append("var names = [");
      for (var i = 0; i < compCount; i++)
      {
        if (i > 0)
          sb.Append(',');
        sb.Append('"');
        sb.Append(s_CompNames[i]);
        sb.Append('"');
      }
      sb.Append("];");
      sb.Append("var eids = globalThis._nativeQuery(...names);");
      sb.Append("var sum = 0;");
      sb.Append("for (var i = 0; i < eids.length; i++) {");
      sb.Append("  var eid = eids[i];");
      for (var i = 0; i < compCount; i++)
        sb.Append($"  var c{i} = {s_CompNames[i]}.get(eid);");
      sb.Append("  sum += c0.a;");
      sb.Append('}');
      sb.Append("return sum;");
      sb.Append("})()");
      return sb.ToString();
    }

    static string BuildIterateReadWriteJs(int compCount)
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
    public IEnumerator Bridged_QueryOnly(
      [Values(10, 100, 10_000, 100_000)] int entityCount,
      [Values(1, 2, 3, 4, 5)] int componentCount
    )
    {
      CreateBenchmarkEntities(entityCount, componentCount);
      yield return null;

      var js = BuildQueryOnlyJs(componentCount);
      var (warmup, iterations) = GetIterationCounts(entityCount);

      BenchmarkHarness.Measure(
        MakeLabel("QueryOnly", entityCount, componentCount),
        warmup,
        iterations,
        () => EvalGlobal(js)
      );

      yield return null;
    }

    [UnityTest]
    public IEnumerator Bridged_IterateRead(
      [Values(10, 100, 10_000, 100_000)] int entityCount,
      [Values(1, 2, 3, 4, 5)] int componentCount
    )
    {
      CreateBenchmarkEntities(entityCount, componentCount);
      yield return null;

      var js = BuildIterateReadJs(componentCount);
      var (warmup, iterations) = GetIterationCounts(entityCount);

      BenchmarkHarness.Measure(
        MakeLabel("IterateRead", entityCount, componentCount),
        warmup,
        iterations,
        () => EvalGlobal(js)
      );

      yield return null;
    }

    [UnityTest]
    public IEnumerator Bridged_IterateReadWrite(
      [Values(10, 100, 10_000, 100_000)] int entityCount,
      [Values(1, 2, 3, 4, 5)] int componentCount
    )
    {
      CreateBenchmarkEntities(entityCount, componentCount);
      yield return null;

      var js = BuildIterateReadWriteJs(componentCount);
      var (warmup, iterations) = GetIterationCounts(entityCount);

      BenchmarkHarness.Measure(
        MakeLabel("IterateReadWrite", entityCount, componentCount),
        warmup,
        iterations,
        () => EvalGlobal(js)
      );

      yield return null;
    }

    #endregion
  }
}
