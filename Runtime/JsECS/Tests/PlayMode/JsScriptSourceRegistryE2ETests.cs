namespace UnityJS.Entities.PlayModeTests
{
  using System;
  using System.Collections;
  using System.IO;
  using System.Runtime.InteropServices;
  using System.Text;
  using Components;
  using Core;
  using NUnit.Framework;
  using QJS;
  using Runtime;
  using Systems;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityEngine;
  using UnityEngine.TestTools;

  public class JsScriptSourceRegistryE2ETests
  {
    World m_World;
    EntityManager m_EntityManager;
    JsRuntimeManager m_Vm;
    string m_TempDir;

    static readonly string s_testsPath = Path.Combine(
      Application.streamingAssetsPath,
      "unity.js",
      "tests"
    );

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_EntityManager = m_World.EntityManager;
      m_Vm = JsRuntimeManager.GetOrCreate();

      // Clear test globals
      EvalGlobal("for (var k in globalThis) { if (k.startsWith('_e2e')) delete globalThis[k]; }");

      yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      var query = m_EntityManager.CreateEntityQuery(typeof(JsEntityId));
      m_EntityManager.DestroyEntity(query);
      var cleanupQuery = m_EntityManager.CreateEntityQuery(typeof(JsScript));
      m_EntityManager.DestroyEntity(cleanupQuery);

      // Clean up temp dir if created
      if (m_TempDir != null && Directory.Exists(m_TempDir))
      {
        try
        {
          Directory.Delete(m_TempDir, true);
        }
        catch
        {
          /* best effort */
        }

        m_TempDir = null;
      }

      yield return null;
    }

    #region Helpers

    string CreateTempDir()
    {
      m_TempDir = Path.Combine(
        Path.GetTempPath(),
        "unity_js_test_" + Guid.NewGuid().ToString("N")[..8]
      );
      Directory.CreateDirectory(m_TempDir);
      return m_TempDir;
    }

    string CreateTempDirWithSystems(params (string name, string source)[] systems)
    {
      var dir = CreateTempDir();
      var systemsDir = Path.Combine(dir, "systems");
      Directory.CreateDirectory(systemsDir);
      foreach (var (name, source) in systems)
        File.WriteAllText(Path.Combine(systemsDir, name + ".js"), source);
      return dir;
    }

    unsafe void EvalGlobal(string code)
    {
      var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

      fixed (
        byte* pSource = sourceBytes,
          pFilename = filenameBytes
      )
      {
        var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename, QJS.JS_EVAL_TYPE_GLOBAL);
        if (QJS.IsException(val))
          Log.Error("[E2E] EvalGlobal exception");
        QJS.JS_FreeValue(m_Vm.Context, val);
      }
    }

    unsafe int GetGlobalInt(string name)
    {
      var code = $"globalThis.{name} || 0";
      var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

      fixed (
        byte* pSource = sourceBytes,
          pFilename = filenameBytes
      )
      {
        var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename, QJS.JS_EVAL_TYPE_GLOBAL);
        int result;
        QJS.JS_ToInt32(m_Vm.Context, &result, val);
        QJS.JS_FreeValue(m_Vm.Context, val);
        return result;
      }
    }

    unsafe bool GetGlobalBool(string name)
    {
      var code = $"!!globalThis.{name}";
      var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

      fixed (
        byte* pSource = sourceBytes,
          pFilename = filenameBytes
      )
      {
        var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename, QJS.JS_EVAL_TYPE_GLOBAL);
        var result = QJS.JS_ToBool(m_Vm.Context, val) != 0;
        QJS.JS_FreeValue(m_Vm.Context, val);
        return result;
      }
    }

    unsafe string GetGlobalString(string name)
    {
      var code = $"globalThis.{name} || ''";
      var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

      fixed (
        byte* pSource = sourceBytes,
          pFilename = filenameBytes
      )
      {
        var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename, QJS.JS_EVAL_TYPE_GLOBAL);
        var ptr = QJS.JS_ToCString(m_Vm.Context, val);
        var result = Marshal.PtrToStringUTF8((nint)ptr);
        QJS.JS_FreeCString(m_Vm.Context, ptr);
        QJS.JS_FreeValue(m_Vm.Context, val);
        return result;
      }
    }

    #endregion

    #region Registry Basics

    [UnityTest]
    public IEnumerator Register_FileSystemSource_DiscoversSystems()
    {
      var dir = CreateTempDirWithSystems(
        ("test_reg_a", "export function onUpdate(state) { globalThis._e2eRegA = true; }")
      );

      var source = new FileSystemScriptSource("test-fs", dir, 10);
      JsScriptSourceRegistry.Register(source);

      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      var found = false;
      foreach (var (name, _) in systems)
        if (name == "test_reg_a")
        {
          found = true;
          break;
        }

      Assert.IsTrue(found, "Should discover test_reg_a from filesystem source");

      JsScriptSourceRegistry.Unregister("test-fs");
      yield return null;
    }

    [UnityTest]
    public IEnumerator Register_BundleSource_DiscoversSystems()
    {
      var bundle = new BundleScriptSource("test-bundle", 10);
      bundle.Add(
        "systems/bundle_sys",
        "export function onUpdate(state) { globalThis._e2eBundleSys = true; }"
      );
      JsScriptSourceRegistry.Register(bundle);

      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      var found = false;
      foreach (var (name, _) in systems)
        if (name == "bundle_sys")
        {
          found = true;
          break;
        }

      Assert.IsTrue(found, "Should discover bundle_sys from bundle source");

      JsScriptSourceRegistry.Unregister("test-bundle");
      yield return null;
    }

    [UnityTest]
    public IEnumerator Register_MultipleSourcesPriorityOrder()
    {
      var bundle1 = new BundleScriptSource("prio-high", 0);
      bundle1.Add(
        "systems/prio_test",
        "export function onUpdate(state) { globalThis._e2ePrio = 'high'; }"
      );

      var bundle2 = new BundleScriptSource("prio-low", 100);
      bundle2.Add(
        "systems/prio_test",
        "export function onUpdate(state) { globalThis._e2ePrio = 'low'; }"
      );

      JsScriptSourceRegistry.Register(bundle2);
      JsScriptSourceRegistry.Register(bundle1);

      // Lower priority number wins
      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      IJsScriptSource winner = null;
      foreach (var (name, src) in systems)
        if (name == "prio_test")
        {
          winner = src;
          break;
        }

      Assert.IsNotNull(winner);
      Assert.AreEqual("prio-high", winner.SourceId, "Lower priority number should win");

      JsScriptSourceRegistry.Unregister("prio-high");
      JsScriptSourceRegistry.Unregister("prio-low");
      yield return null;
    }

    [UnityTest]
    public IEnumerator Unregister_SourceRemoved_NotDiscoveredAnymore()
    {
      var bundle = new BundleScriptSource("test-unreg", 10);
      bundle.Add("systems/unreg_sys", "export function onUpdate(state) {}");
      JsScriptSourceRegistry.Register(bundle);

      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      var found = false;
      foreach (var (name, _) in systems)
        if (name == "unreg_sys")
        {
          found = true;
          break;
        }

      Assert.IsTrue(found, "Should find before unregister");

      JsScriptSourceRegistry.Unregister("test-unreg");

      systems = JsScriptSourceRegistry.DiscoverAllSystems();
      found = false;
      foreach (var (name, _) in systems)
        if (name == "unreg_sys")
        {
          found = true;
          break;
        }

      Assert.IsFalse(found, "Should not find after unregister");

      yield return null;
    }

    [UnityTest]
    public IEnumerator Reset_ClearsAllSources()
    {
      var bundle = new BundleScriptSource("test-reset", 10);
      bundle.Add("systems/reset_sys", "export function onUpdate(state) {}");
      JsScriptSourceRegistry.Register(bundle);

      // Reset clears everything — reinitialize default paths
      JsScriptSourceRegistry.Unregister("test-reset");

      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      var found = false;
      foreach (var (name, _) in systems)
        if (name == "reset_sys")
        {
          found = true;
          break;
        }

      Assert.IsFalse(found, "Should be empty after removing the source");

      yield return null;
    }

    #endregion

    #region Script Resolution

    [UnityTest]
    public IEnumerator TryReadScript_FileSystemSource_ReadsFromDisk()
    {
      var dir = CreateTempDir();
      File.WriteAllText(
        Path.Combine(dir, "my_script.js"),
        "export function onInit(state) { globalThis._e2eFsRead = true; }"
      );

      var source = new FileSystemScriptSource("test-read", dir, 10);
      JsScriptSourceRegistry.Register(source);

      Assert.IsTrue(
        JsScriptSourceRegistry.TryReadScript("my_script", out var code, out var resolvedId)
      );
      Assert.IsTrue(code.Contains("_e2eFsRead"));
      Assert.IsTrue(resolvedId.EndsWith("my_script.js"));

      JsScriptSourceRegistry.Unregister("test-read");
      yield return null;
    }

    [UnityTest]
    public IEnumerator TryReadScript_BundleSource_ReadsFromMemory()
    {
      var bundle = new BundleScriptSource("test-mem-read", 10);
      bundle.Add(
        "my_mem_script",
        "export function onInit(state) { globalThis._e2eMemRead = true; }"
      );
      JsScriptSourceRegistry.Register(bundle);

      Assert.IsTrue(
        JsScriptSourceRegistry.TryReadScript("my_mem_script", out var code, out var resolvedId)
      );
      Assert.IsTrue(code.Contains("_e2eMemRead"));
      Assert.IsTrue(resolvedId.StartsWith("bundle://"));

      JsScriptSourceRegistry.Unregister("test-mem-read");
      yield return null;
    }

    [UnityTest]
    public IEnumerator TryReadScript_NotInAnySource_ReturnsFalse()
    {
      Assert.IsFalse(JsScriptSourceRegistry.TryReadScript("nonexistent_script_xyz", out _, out _));
      yield return null;
    }

    #endregion

    #region Bundle Source Execution

    [UnityTest]
    public IEnumerator BundleSource_ScriptExecutes()
    {
      var bundle = new BundleScriptSource("test-exec", 10);
      bundle.Add(
        "test_bundle_exec",
        "export function onInit(state) { globalThis._e2eBundleExec = true; }\n"
          + "export function onUpdate(state) { globalThis._e2eBundleExecCount = (globalThis._e2eBundleExecCount || 0) + 1; }"
      );
      JsScriptSourceRegistry.Register(bundle);

      Assert.IsTrue(bundle.TryReadScript("test_bundle_exec", out var source, out var resolvedId));
      Assert.IsTrue(m_Vm.LoadScriptAsModule("test_bundle_exec", source, resolvedId));

      var stateRef = m_Vm.CreateEntityState("test_bundle_exec", -1);
      m_Vm.CallInit("test_bundle_exec", stateRef);
      Assert.IsTrue(GetGlobalBool("_e2eBundleExec"), "onInit should have run");

      m_Vm.CallFunction("test_bundle_exec", "onUpdate", stateRef);
      m_Vm.CallFunction("test_bundle_exec", "onUpdate", stateRef);
      Assert.AreEqual(2, GetGlobalInt("_e2eBundleExecCount"));

      m_Vm.ReleaseEntityState(stateRef);
      JsScriptSourceRegistry.Unregister("test-exec");
      yield return null;
    }

    #endregion

    #region Per-Entity Script Fulfillment

    [UnityTest]
    public IEnumerator Fulfillment_ScriptFromBundleSource()
    {
      var bundle = new BundleScriptSource("test-fulfill", 5);
      bundle.Add(
        "test_fulfill_bundle",
        "export function onInit(state) { globalThis._e2eFulfillBundle = true; }"
      );
      JsScriptSourceRegistry.Register(bundle);

      // Create entity with script request
      var entity = m_EntityManager.CreateEntity();
      m_EntityManager.AddComponentData(
        entity,
        new LocalTransform
        {
          Position = float3.zero,
          Rotation = quaternion.identity,
          Scale = 1f,
        }
      );
      m_EntityManager.AddComponentData(
        entity,
        new JsEntityId { value = JsEntityRegistry.IsCreated ? JsEntityRegistry.AllocateId() : 1 }
      );
      var requests = m_EntityManager.AddBuffer<JsScriptRequest>(entity);
      requests.Add(
        new JsScriptRequest
        {
          scriptName = new FixedString64Bytes("test_fulfill_bundle"),
          requestHash = JsScriptPathUtility.HashScriptName("test_fulfill_bundle"),
          fulfilled = false,
        }
      );

      // Wait for fulfillment system
      yield return null;
      yield return null;
      yield return null;

      Assert.IsTrue(
        GetGlobalBool("_e2eFulfillBundle"),
        "onInit should have been called via fulfillment"
      );

      JsScriptSourceRegistry.Unregister("test-fulfill");
    }

    [UnityTest]
    public IEnumerator Fulfillment_ScriptNotInAnySource_LogsError()
    {
      var entity = m_EntityManager.CreateEntity();
      m_EntityManager.AddComponentData(
        entity,
        new LocalTransform
        {
          Position = float3.zero,
          Rotation = quaternion.identity,
          Scale = 1f,
        }
      );
      m_EntityManager.AddComponentData(
        entity,
        new JsEntityId { value = JsEntityRegistry.IsCreated ? JsEntityRegistry.AllocateId() : 1 }
      );
      var requests = m_EntityManager.AddBuffer<JsScriptRequest>(entity);
      requests.Add(
        new JsScriptRequest
        {
          scriptName = new FixedString64Bytes("nonexistent_abc_xyz"),
          requestHash = JsScriptPathUtility.HashScriptName("nonexistent_abc_xyz"),
          fulfilled = false,
        }
      );

      LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Script not found"));

      yield return null;
      yield return null;

      // Verify request was marked fulfilled (error case)
      var reqs = m_EntityManager.GetBuffer<JsScriptRequest>(entity);
      Assert.IsTrue(reqs[0].fulfilled, "Failed request should be marked fulfilled");
    }

    #endregion

    #region Module Imports

    [UnityTest]
    public IEnumerator Import_RelativePath_FileSystem()
    {
      // test_import_main.js imports ./test_import_helper.js
      JsScriptSearchPaths.AddSearchPath(s_testsPath, 0);

      var mainPath = Path.Combine(s_testsPath, "test_import_main.js");
      var source = File.ReadAllText(mainPath);
      Assert.IsTrue(m_Vm.LoadScriptAsModule("test_import_main", source, mainPath));

      var stateRef = m_Vm.CreateEntityState("test_import_main", -1);
      m_Vm.CallFunction("test_import_main", "onUpdate", stateRef);

      Assert.AreEqual(42, GetGlobalInt("_e2eImport"), "Relative import should resolve and execute");

      m_Vm.ReleaseEntityState(stateRef);
      JsScriptSearchPaths.RemoveSearchPath(s_testsPath);
      yield return null;
    }

    [UnityTest]
    public IEnumerator Import_RelativePath_BundleSource()
    {
      var bundle = new BundleScriptSource("test-bundle-import", 10);
      bundle.Add(
        "systems/bundle_main",
        "import { helper } from './bundle_helper.js';\n"
          + "export function onUpdate(state) { globalThis._e2eBundleImport = helper(); }"
      );
      bundle.Add("systems/bundle_helper", "export function helper() { return 77; }");
      JsScriptSourceRegistry.Register(bundle);

      Assert.IsTrue(
        bundle.TryReadScript("systems/bundle_main", out var source, out var resolvedId)
      );
      Assert.IsTrue(m_Vm.LoadScriptAsModule("bundle_main", source, resolvedId));

      var stateRef = m_Vm.CreateEntityState("bundle_main", -1);
      m_Vm.CallFunction("bundle_main", "onUpdate", stateRef);

      Assert.AreEqual(
        77,
        GetGlobalInt("_e2eBundleImport"),
        "Relative import within bundle should resolve and execute"
      );

      m_Vm.ReleaseEntityState(stateRef);
      JsScriptSourceRegistry.Unregister("test-bundle-import");
      yield return null;
    }

    [UnityTest]
    public IEnumerator Import_BareModule_FileSystem()
    {
      // test_bare_importer.js imports ./test_bare_module.js (relative, not bare)
      JsScriptSearchPaths.AddSearchPath(s_testsPath, 0);

      var importerPath = Path.Combine(s_testsPath, "test_bare_importer.js");
      var source = File.ReadAllText(importerPath);
      Assert.IsTrue(m_Vm.LoadScriptAsModule("test_bare_importer", source, importerPath));

      var stateRef = m_Vm.CreateEntityState("test_bare_importer", -1);
      m_Vm.CallFunction("test_bare_importer", "onUpdate", stateRef);

      Assert.AreEqual(
        99,
        GetGlobalInt("_e2eBareImport"),
        "Module import should resolve and execute"
      );

      m_Vm.ReleaseEntityState(stateRef);
      JsScriptSearchPaths.RemoveSearchPath(s_testsPath);
      yield return null;
    }

    #endregion

    #region Edge Cases

    [UnityTest]
    public IEnumerator DuplicateSourceId_ReplacesExisting()
    {
      var bundle1 = new BundleScriptSource("dup-id", 10);
      bundle1.Add(
        "systems/dup_sys",
        "export function onUpdate(state) { globalThis._e2eDup = 'first'; }"
      );
      JsScriptSourceRegistry.Register(bundle1);

      var bundle2 = new BundleScriptSource("dup-id", 10);
      bundle2.Add(
        "systems/dup_sys_v2",
        "export function onUpdate(state) { globalThis._e2eDup = 'second'; }"
      );
      JsScriptSourceRegistry.Register(bundle2);

      // Only the second should be active
      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      var foundV1 = false;
      var foundV2 = false;
      foreach (var (name, _) in systems)
      {
        if (name == "dup_sys")
          foundV1 = true;
        if (name == "dup_sys_v2")
          foundV2 = true;
      }

      Assert.IsFalse(foundV1, "First source's systems should be gone");
      Assert.IsTrue(foundV2, "Second source's systems should be present");

      JsScriptSourceRegistry.Unregister("dup-id");
      yield return null;
    }

    [UnityTest]
    public IEnumerator EmptySource_DiscoverReturnsEmpty()
    {
      var bundle = new BundleScriptSource("test-empty", 10);
      JsScriptSourceRegistry.Register(bundle);

      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      var found = false;
      foreach (var (name, src) in systems)
        if (src.SourceId == "test-empty")
        {
          found = true;
          break;
        }

      Assert.IsFalse(found, "Empty source should not contribute any systems");

      JsScriptSourceRegistry.Unregister("test-empty");
      yield return null;
    }

    [UnityTest]
    public IEnumerator FileSystemSource_SubdirDiscovery()
    {
      var dir = CreateTempDirWithSystems(
        ("sub_sys_a", "export function onUpdate(state) {}"),
        ("sub_sys_b", "export function onUpdate(state) {}")
      );

      var source = new FileSystemScriptSource("test-subdir", dir, 10);
      JsScriptSourceRegistry.Register(source);

      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      var foundA = false;
      var foundB = false;
      foreach (var (name, _) in systems)
      {
        if (name == "sub_sys_a")
          foundA = true;
        if (name == "sub_sys_b")
          foundB = true;
      }

      Assert.IsTrue(foundA, "Should discover sub_sys_a");
      Assert.IsTrue(foundB, "Should discover sub_sys_b");

      JsScriptSourceRegistry.Unregister("test-subdir");
      yield return null;
    }

    [UnityTest]
    public IEnumerator FileSystemSource_HasModule_ChecksCorrectly()
    {
      var dir = CreateTempDir();
      File.WriteAllText(Path.Combine(dir, "exists.js"), "export const x = 1;");

      var source = new FileSystemScriptSource("test-hasmod", dir, 10);

      Assert.IsTrue(source.HasModule("exists.js"), "Should find existing module");
      Assert.IsFalse(source.HasModule("nope.js"), "Should not find missing module");

      yield return null;
    }

    [UnityTest]
    public IEnumerator BundleSource_HasModule_ChecksCorrectly()
    {
      var bundle = new BundleScriptSource("test-bundle-has", 10);
      bundle.Add("utils/helper", "export const x = 1;");

      Assert.IsTrue(bundle.HasModule("utils/helper.js"), "Should find module by name+ext");
      Assert.IsFalse(bundle.HasModule("utils/nope.js"), "Should not find missing module");

      yield return null;
    }

    [UnityTest]
    public IEnumerator TryReadModuleBytes_BundlePath_Works()
    {
      var bundle = new BundleScriptSource("test-bytes", 10);
      bundle.Add("utils/data", "export const val = 123;");
      JsScriptSourceRegistry.Register(bundle);

      Assert.IsTrue(
        JsScriptSourceRegistry.TryReadModuleBytes("bundle://test-bytes/utils/data.js", out var data)
      );
      var text = Encoding.UTF8.GetString(data);
      Assert.IsTrue(text.Contains("123"));

      JsScriptSourceRegistry.Unregister("test-bytes");
      yield return null;
    }

    [UnityTest]
    public IEnumerator TryFindModule_FileSystemSource_ReturnsAbsolutePath()
    {
      var dir = CreateTempDir();
      File.WriteAllText(Path.Combine(dir, "findme.js"), "export const x = 1;");

      var source = new FileSystemScriptSource("test-findmod", dir, 10);
      JsScriptSourceRegistry.Register(source);

      Assert.IsTrue(JsScriptSourceRegistry.TryFindModule("findme.js", out var resolved));
      Assert.IsTrue(
        Path.IsPathRooted(resolved),
        "Filesystem module should resolve to absolute path"
      );
      Assert.IsTrue(resolved.EndsWith("findme.js"));

      JsScriptSourceRegistry.Unregister("test-findmod");
      yield return null;
    }

    [UnityTest]
    public IEnumerator TryFindModule_BundleSource_ReturnsBundlePath()
    {
      var bundle = new BundleScriptSource("test-findmod-b", 10);
      bundle.Add("findme_b", "export const x = 1;");
      JsScriptSourceRegistry.Register(bundle);

      Assert.IsTrue(JsScriptSourceRegistry.TryFindModule("findme_b.js", out var resolved));
      Assert.IsTrue(
        resolved.StartsWith("bundle://"),
        "Bundle module should resolve to bundle:// path"
      );

      JsScriptSourceRegistry.Unregister("test-findmod-b");
      yield return null;
    }

    #endregion
  }
}
