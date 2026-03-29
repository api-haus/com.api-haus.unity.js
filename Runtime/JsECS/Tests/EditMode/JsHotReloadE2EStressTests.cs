namespace UnityJS.Entities.EditModeTests
{
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Text.RegularExpressions;
  using System.Threading;
  using NUnit.Framework;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Components;
  using UnityJS.Entities.Core;
  using UnityJS.Runtime;

  [TestFixture]
  [Timeout(180000)]
  public class JsHotReloadE2EStressTests
  {
    static readonly string[] MutableFiles =
    {
      "components/slime_wander.ts",
      "components/slime_spatial.ts",
      "systems/test_system.ts",
    };

    static readonly Dictionary<string, string> ConstantPatterns = new()
    {
      { "components/slime_wander.ts", @"(speed\s*=\s*)\d+" },
      { "components/slime_spatial.ts", @"(strength\s*=\s*)\d+" },
      { "systems/test_system.ts", @"(\+\s*)\d+" },
    };

    const string SyntaxErrorMarker = "{{{SYNTAX_ERROR";

    Dictionary<string, string> m_OriginalContents;
    Dictionary<string, bool> m_HasSyntaxError;
    volatile bool m_MutatorRunning;
    Thread m_MutatorThread;
    int m_MutationCount;
    readonly object m_FileLock = new();

    string TsRoot => Path.Combine(Application.streamingAssetsPath, "unity.js");

    string GetTsPath(string relativePath) => Path.Combine(TsRoot, relativePath);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_OriginalContents = new Dictionary<string, string>();
      m_HasSyntaxError = new Dictionary<string, bool>();
      m_MutationCount = 0;

      foreach (var file in MutableFiles)
      {
        var path = GetTsPath(file);
        m_OriginalContents[file] = File.ReadAllText(path);
        m_HasSyntaxError[file] = false;
      }

      yield break;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      m_MutatorRunning = false;
      m_MutatorThread?.Join(5000);
      m_MutatorThread = null;

      foreach (var file in MutableFiles)
      {
        var path = GetTsPath(file);
        if (m_OriginalContents.TryGetValue(file, out var original))
          File.WriteAllText(path, original);
      }

      if (UnityEditor.EditorApplication.isPlaying)
        yield return new ExitPlayMode();
    }

    // ── Mutation engine ──

    enum MutationType
    {
      TouchComment,
      ChangeConstant,
      InjectSyntaxError,
      FixSyntaxError,
    }

    MutationType PickMutation(System.Random rng, string file)
    {
      if (m_HasSyntaxError[file])
        return rng.NextDouble() < 0.5 ? MutationType.FixSyntaxError : MutationType.TouchComment;

      var roll = rng.NextDouble();
      if (roll < 0.4)
        return MutationType.TouchComment;
      if (roll < 0.8)
        return MutationType.ChangeConstant;
      if (roll < 0.9)
        return MutationType.InjectSyntaxError;
      return MutationType.TouchComment;
    }

    void ApplyMutation(string file, MutationType type, System.Random rng)
    {
      lock (m_FileLock)
      {
        var path = GetTsPath(file);
        var content = File.ReadAllText(path);

        switch (type)
        {
          case MutationType.TouchComment:
            var n = Interlocked.Increment(ref m_MutationCount);
            content += $"// stress-{n}\n";
            break;

          case MutationType.ChangeConstant:
            if (ConstantPatterns.TryGetValue(file, out var pattern))
            {
              var newVal = rng.Next(1, 100);
              content = Regex.Replace(
                content,
                pattern,
                $"${{1}}{newVal}",
                RegexOptions.None,
                TimeSpan.FromSeconds(1)
              );
            }
            break;

          case MutationType.InjectSyntaxError:
            if (content.Contains(SyntaxErrorMarker))
              break;
            var importIdx = content.IndexOf("import", StringComparison.Ordinal);
            if (importIdx < 0)
              break;
            var eol = content.IndexOf('\n', importIdx);
            if (eol < 0)
              break;
            content = content.Insert(eol + 1, SyntaxErrorMarker + "\n");
            m_HasSyntaxError[file] = true;
            break;

          case MutationType.FixSyntaxError:
            content = content.Replace(SyntaxErrorMarker + "\n", "");
            content = content.Replace(SyntaxErrorMarker, "");
            m_HasSyntaxError[file] = false;
            break;
        }

        File.WriteAllText(path, content);
      }
    }

    void StartMutatorThread(
      int seed,
      int minIntervalMs = 50,
      int maxIntervalMs = 200,
      bool commentOnly = false
    )
    {
      m_MutatorRunning = true;
      m_MutatorThread = new Thread(() =>
      {
        var rng = new System.Random(seed);
        while (m_MutatorRunning)
        {
          var file = MutableFiles[rng.Next(MutableFiles.Length)];
          var type = commentOnly ? MutationType.TouchComment : PickMutation(rng, file);
          try
          {
            ApplyMutation(file, type, rng);
          }
          catch { /* file may be locked — ignore */ }
          Thread.Sleep(rng.Next(minIntervalMs, maxIntervalMs));
        }
      });
      m_MutatorThread.IsBackground = true;
      m_MutatorThread.Start();
    }

    /// <summary>
    /// Reload a script by reading its .ts source, transpiling, and hot-reloading.
    /// Returns true if transpilation + reload succeeded.
    /// </summary>
    bool ReloadScript(JsRuntimeManager vm, string relPath)
    {
      var tsPath = GetTsPath(relPath);
      if (!File.Exists(tsPath))
        return false;

      var tsSource = File.ReadAllText(tsPath);
      var jsSource = JsTranspiler.Transpile(tsSource, tsPath);
      if (jsSource == null)
        return false;

      var scriptName = relPath.EndsWith(".ts")
        ? relPath[..^3]
        : relPath;

      if (!vm.HasScript(scriptName))
        return false;

      return vm.ReloadScript(scriptName, jsSource, tsPath);
    }

    void ReloadAllScripts(JsRuntimeManager vm)
    {
      foreach (var file in MutableFiles)
      {
        var scriptName = file.EndsWith(".ts") ? file[..^3] : file;
        if (!vm.HasScript(scriptName))
          continue;

        var tsPath = GetTsPath(file);
        if (!File.Exists(tsPath))
          continue;

        var tsSource = File.ReadAllText(tsPath);
        var jsSource = JsTranspiler.Transpile(tsSource, tsPath);
        if (jsSource != null)
        {
          vm.ReloadScript(scriptName, jsSource, tsPath);
          if (scriptName.StartsWith("components/"))
            vm.ComponentReload(scriptName);
        }
      }
    }

    // ── Helpers ──

    void PreloadScripts(JsRuntimeManager vm)
    {
      foreach (var file in MutableFiles)
      {
        var scriptName = file.EndsWith(".ts") ? file[..^3] : file;
        if (!vm.HasScript(scriptName))
          vm.LoadScript(scriptName);
      }
    }

    // ── Tests ──

    [UnityTest]
    public IEnumerator StressTest_ConcurrentMutations_VmSurvives()
    {
      yield return new EnterPlayMode();
      yield return null;

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, "VM must exist in play mode");
      vm.ClearCapturedExceptions();
      PreloadScripts(vm);

      var initialSuccessCount = JsTranspiler.SuccessCount;

      LogAssert.ignoreFailingMessages = true;
      StartMutatorThread(seed: 42);

      for (var cycle = 0; cycle < 15; cycle++)
      {
        for (var f = 0; f < 5; f++)
          yield return null;

        var errBefore = JsTranspiler.ErrorCount;
        ReloadAllScripts(vm);

        if (JsTranspiler.ErrorCount == errBefore)
        {
          // No new errors — verify health
          var health = vm.VerifyModuleHealth();
          Assert.IsNull(health, $"Cycle {cycle}: TDZ after reload: {health}");

          Assert.IsEmpty(
            vm.CapturedExceptions,
            $"Cycle {cycle}: JS exceptions:\n" + string.Join("\n", vm.CapturedExceptions)
          );
        }
      }

      LogAssert.ignoreFailingMessages = false;

      Assert.Greater(JsTranspiler.SuccessCount, initialSuccessCount,
        "At least some transpilations should have succeeded");
    }

    [UnityTest]
    public IEnumerator StressTest_EntitySurvivesReloads()
    {
      yield return new EnterPlayMode();
      yield return null;

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm);
      vm.ClearCapturedExceptions();

      var em = World.DefaultGameObjectInjectionWorld.EntityManager;
      var entity = CreateScriptedEntity(em, "components/slime_wander");

      // Let fulfillment system process the request
      yield return null;
      yield return null;
      yield return null;

      Assert.IsTrue(
        em.HasBuffer<JsScript>(entity),
        "Entity must be fulfilled before reload cycles"
      );

      for (var cycle = 0; cycle < 10; cycle++)
      {
        var path = GetTsPath("components/slime_wander.ts");
        File.AppendAllText(path, $"// reload-{cycle}\n");

        Assert.IsTrue(
          ReloadScript(vm, "components/slime_wander.ts"),
          $"Cycle {cycle}: comment-only change must reload successfully"
        );
        vm.ComponentReload("components/slime_wander");

        yield return null;

        Assert.IsTrue(
          em.HasBuffer<JsScript>(entity),
          $"Cycle {cycle}: entity lost JsScript buffer after reload"
        );

        var health = vm.VerifyModuleHealth();
        Assert.IsNull(health, $"Cycle {cycle}: module health failed: {health}");
      }
    }

    [UnityTest]
    public IEnumerator StressTest_SyntaxErrorRecovery_UnderPressure()
    {
      yield return new EnterPlayMode();
      yield return null;

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm);
      vm.ClearCapturedExceptions();
      PreloadScripts(vm);

      // Transpile errors are expected — suppress so test runner doesn't fail on them
      LogAssert.ignoreFailingMessages = true;

      // Background thread does comment-only mutations for pressure
      StartMutatorThread(seed: 99, commentOnly: true);

      for (var cycle = 0; cycle < 10; cycle++)
      {
        var targetFile = "components/slime_wander.ts";

        ApplyMutation(targetFile, MutationType.InjectSyntaxError, new System.Random(cycle));

        for (var f = 0; f < 3; f++)
          yield return null;

        Assert.IsFalse(ReloadScript(vm, targetFile),
          $"Cycle {cycle}: transpilation should fail with syntax error");

        ApplyMutation(targetFile, MutationType.FixSyntaxError, new System.Random(cycle));

        for (var f = 0; f < 3; f++)
          yield return null;

        Assert.IsTrue(
          ReloadScript(vm, targetFile),
          $"Cycle {cycle}: transpilation should succeed after fix"
        );
        vm.ComponentReload("components/slime_wander");

        var health = vm.VerifyModuleHealth();
        Assert.IsNull(health, $"Cycle {cycle}: TDZ after recovery: {health}");
      }

      LogAssert.ignoreFailingMessages = false;
    }

    [UnityTest]
    public IEnumerator StressTest_RapidCommentMutations_AllTranspileSucceed()
    {
      yield return new EnterPlayMode();
      yield return null;

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm);
      vm.ClearCapturedExceptions();
      PreloadScripts(vm);

      var initialSuccessCount = JsTranspiler.SuccessCount;

      StartMutatorThread(seed: 777, minIntervalMs: 10, maxIntervalMs: 30, commentOnly: true);

      for (var cycle = 0; cycle < 10; cycle++)
      {
        for (var f = 0; f < 5; f++)
          yield return null;

        var errBefore = JsTranspiler.ErrorCount;
        ReloadAllScripts(vm);
        Assert.AreEqual(errBefore, JsTranspiler.ErrorCount,
          $"Cycle {cycle}: comment-only mutations must always transpile");
      }

      Assert.Greater(JsTranspiler.SuccessCount, initialSuccessCount + 10,
        "Should have many successful transpilations");

      Assert.IsEmpty(
        vm.CapturedExceptions,
        "No JS exceptions expected:\n" + string.Join("\n", vm.CapturedExceptions)
      );
    }

    // ── Entity helper ──

    static Entity CreateScriptedEntity(EntityManager em, string scriptName)
    {
      var entity = em.CreateEntity();
      em.AddComponentData(
        entity,
        new LocalTransform
        {
          Position = float3.zero,
          Rotation = quaternion.identity,
          Scale = 1f,
        }
      );
      var entityId = JsEntityRegistry.AllocateId();
      em.AddComponentData(entity, new JsEntityId { value = entityId });
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
  }
}
