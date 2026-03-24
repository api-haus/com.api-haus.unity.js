using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityJS.Editor;
using UnityJS.Entities.Components;
using UnityJS.Entities.Core;
using UnityJS.Runtime;

namespace UnityJS.Integrations.Editor
{
  /// <summary>
  /// Reusable test harness for integration E2E tests.
  /// Provides fixture compilation, search path management, entity creation,
  /// test input injection, and assertion helpers.
  /// </summary>
  public static class IntegrationTestHarness
  {
    // ── Path Resolution ──

    /// <summary>
    /// Resolves an absolute path from a path relative to the unity.js package root.
    /// Works for local, git, and openupm installs.
    /// </summary>
    public static string GetFixturesPath(string integrationRelativePath)
    {
      var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
        typeof(IntegrationTestHarness).Assembly
      );
      Assert.IsNotNull(pkgInfo, "Could not resolve unity.js package info");
      return Path.Combine(pkgInfo.resolvedPath, integrationRelativePath);
    }

    // ── TypeScript Compilation ──

    /// <summary>
    /// Compiles fixture .ts files via the project-root tsconfig.
    /// All fixtures are included in the single tsconfig and compile to Library/TscBuild/.
    /// Returns the compiled output path matching the fixture source tree.
    ///
    /// For fixtures with their own tsconfig.json (e.g. hot-reload stress tests),
    /// a separate TscCompiler is used with a temp output directory.
    /// </summary>
    public static string CompileFixtures(string fixturesPath)
    {
      Assert.IsTrue(
        Directory.Exists(fixturesPath),
        $"Fixtures directory does not exist: {fixturesPath}"
      );

      // Fixtures with their own tsconfig compile independently (e.g. hot-reload tests)
      var fixtureConfig = Path.Combine(fixturesPath, "tsconfig.json");
      if (File.Exists(fixtureConfig))
      {
        var outDir = Path.Combine(
          Path.GetTempPath(),
          "unity-js-fixtures-" + fixturesPath.GetHashCode().ToString("x8")
        );
        var compiler = new TscCompiler(fixturesPath, outDir, fixtureConfig);
        var success = compiler.Recompile();
        Assert.IsTrue(
          success,
          $"Fixture compilation failed:\n{string.Join("\n", compiler.LastErrors)}"
        );
        return outDir;
      }

      // No per-fixture tsconfig — compiled by the project-root tsconfig
      var mainCompiler = TscCompiler.Instance;
      Assert.IsNotNull(mainCompiler, "TscCompiler.Instance not initialized");
      mainCompiler.RecompileIfStale();

      var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
      var normalizedFixtures = Path.GetFullPath(fixturesPath).Replace("\\", "/");
      var normalizedRoot = projectRoot.Replace("\\", "/");

      string relative;
      if (normalizedFixtures.StartsWith(normalizedRoot + "/"))
        relative = normalizedFixtures.Substring(normalizedRoot.Length + 1);
      else
        relative = normalizedFixtures;

      return Path.Combine(mainCompiler.OutDir, relative);
    }

    // ── Search Path Scope ──

    /// <summary>
    /// Registers a path as the highest-priority JS search path.
    /// Returns a disposable that removes the path when disposed.
    /// Usage: using var scope = IntegrationTestHarness.UseSearchPath(path);
    /// </summary>
    public static SearchPathScope UseSearchPath(string absolutePath) => new(absolutePath);

    /// <summary>
    /// Registers a fixture path as the ONLY search path, removing all others
    /// (StreamingAssets, TscBuild). Prevents game scripts from interfering with fixtures.
    /// Restores original paths on dispose.
    /// </summary>
    public static IsolatedSearchPathScope UseIsolatedSearchPath(string absolutePath) => new(absolutePath);

    public sealed class SearchPathScope : IDisposable
    {
      readonly string m_Path;

      public SearchPathScope(string path)
      {
        m_Path = path;
        JsScriptSearchPaths.AddSearchPath(path, 0);
      }

      public void Dispose() => JsScriptSearchPaths.RemoveSearchPath(m_Path);
    }

    public sealed class IsolatedSearchPathScope : IDisposable
    {
      readonly List<(string path, string sourceId, int priority)> m_Saved;
      readonly string m_Path;

      public IsolatedSearchPathScope(string path)
      {
        m_Path = path;
        m_Saved = JsScriptSearchPaths.RemoveAllSources();
        JsScriptSearchPaths.AddSearchPath(path, 0);

        // Force JsSystemRunner to re-discover systems from the isolated path
        var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
        if (world != null)
        {
          var handle = world.Unmanaged.GetExistingUnmanagedSystem<Entities.Systems.JsSystemRunner>();
          if (handle != Unity.Entities.SystemHandle.Null)
          {
            ref var sysState = ref world.Unmanaged.ResolveSystemStateRef(handle);
            Entities.Systems.JsSystemDiscovery.ForceRediscovery(ref sysState);
          }
        }
      }

      public void Dispose()
      {
        JsScriptSearchPaths.RemoveSearchPath(m_Path);
        JsScriptSearchPaths.RestoreSources(m_Saved);

        // Force re-discovery with restored paths
        var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
        if (world != null)
        {
          var handle = world.Unmanaged.GetExistingUnmanagedSystem<Entities.Systems.JsSystemRunner>();
          if (handle != Unity.Entities.SystemHandle.Null)
          {
            ref var sysState = ref world.Unmanaged.ResolveSystemStateRef(handle);
            Entities.Systems.JsSystemDiscovery.ForceRediscovery(ref sysState);
          }
        }
      }
    }

    // ── Entity Creation ──

    /// <summary>
    /// Creates an entity with JsEntityId, LocalTransform, and a JsScript (unfulfilled)
    /// for the given script name, plus any extra component types.
    /// Follows the same pattern as JsPlayModeCycleTests.CreateSlimeEntities.
    /// </summary>
    public static Entity CreateScriptedEntity(
      EntityManager em,
      string scriptName,
      params ComponentType[] extraTypes
    )
    {
      var types = new NativeList<ComponentType>(extraTypes.Length + 2, Allocator.Temp);
      types.Add(ComponentType.ReadWrite<JsEntityId>());
      types.Add(ComponentType.ReadWrite<LocalTransform>());
      foreach (var t in extraTypes)
        types.Add(t);
      var entity = em.CreateEntity(types.AsArray());
      types.Dispose();

      var entityId = JsEntityRegistry.AllocateId();
      JsEntityRegistry.RegisterImmediate(entity, entityId, em);
      em.SetComponentData(entity, new JsEntityId { value = entityId });
      em.SetComponentData(entity, LocalTransform.FromPosition(float3.zero));

      var scripts = em.AddBuffer<JsScript>(entity);
      scripts.Add(
        new JsScript
        {
          scriptName = new FixedString64Bytes(scriptName),
          stateRef = -1,
          entityIndex = 0,
          requestHash = JsScriptPathUtility.HashScriptName(scriptName),
          disabled = false,
        }
      );
      return entity;
    }

    // ── Test Input Injection ──

    /// <summary>
    /// Sets globalThis._testInput so fixture scripts can read mock input values.
    /// </summary>
    public static unsafe void SetTestInput(
      float moveX = 0,
      float moveZ = 0,
      bool jump = false,
      bool sprint = false
    )
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      var code =
        $"globalThis._testInput = {{ moveX:{moveX}, moveZ:{moveZ}, "
        + $"jump:{(jump ? "true" : "false")}, sprint:{(sprint ? "true" : "false")} }};";
      EvalVoidInternal(vm.Context, code);
    }

    /// <summary>
    /// Clears globalThis._testInput.
    /// </summary>
    public static unsafe void ClearTestInput()
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;
      EvalVoidInternal(vm.Context, "delete globalThis._testInput;");
    }

    // ── Assertions ──

    /// <summary>
    /// Asserts that no JS errors occurred: checks module health and captured exceptions.
    /// </summary>
    public static void AssertNoJsErrors(string context)
    {
      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, $"VM should exist ({context})");

      var health = vm.VerifyModuleHealth();
      Assert.IsNull(health, $"Module TDZ ({context}): {health}");

      Assert.IsEmpty(
        vm.CapturedExceptions,
        $"JS exceptions ({context}):\n" + string.Join("\n", vm.CapturedExceptions)
      );
    }

    /// <summary>
    /// Asserts that all entities have a JsScript buffer with at least one fulfilled script.
    /// </summary>
    public static void AssertEntitiesFulfilled(EntityManager em, params Entity[] entities)
    {
      foreach (var e in entities)
      {
        Assert.IsTrue(em.HasBuffer<JsScript>(e), "Entity missing JsScript buffer");
        var scripts = em.GetBuffer<JsScript>(e);
        Assert.GreaterOrEqual(scripts.Length, 1, "No scripts fulfilled");
      }
    }

    // ── Internal ──

    static unsafe void EvalVoidInternal(UnityJS.QJS.JSContext ctx, string code)
    {
      var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var fileBytes = Encoding.UTF8.GetBytes("<harness>\0");
      fixed (
        byte* pSrc = sourceBytes,
          pFile = fileBytes
      )
      {
        var result = UnityJS.QJS.QJS.JS_Eval(
          ctx,
          pSrc,
          sourceLen,
          pFile,
          UnityJS.QJS.QJS.JS_EVAL_TYPE_GLOBAL
        );
        if (UnityJS.QJS.QJS.IsException(result))
        {
          var exc = UnityJS.QJS.QJS.JS_GetException(ctx);
          var eptr = UnityJS.QJS.QJS.JS_ToCString(ctx, exc);
          var emsg = Marshal.PtrToStringUTF8((nint)eptr) ?? "unknown error";
          UnityJS.QJS.QJS.JS_FreeCString(ctx, eptr);
          UnityJS.QJS.QJS.JS_FreeValue(ctx, exc);
          Assert.Fail($"JS exception in harness: {emsg}");
        }
        UnityJS.QJS.QJS.JS_FreeValue(ctx, result);
      }
    }
  }
}
