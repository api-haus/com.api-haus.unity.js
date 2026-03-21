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
  using UnityJS.Editor;
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

    const string SyntaxErrorMarker = "{{{SYNTAX_ERROR}}}";

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

      TscCompiler.Instance?.Recompile();

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

      TscCompiler.Instance?.Recompile();

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
          catch
          { /* file may be locked by tsc — ignore */
          }
          Thread.Sleep(rng.Next(minIntervalMs, maxIntervalMs));
        }
      });
      m_MutatorThread.IsBackground = true;
      m_MutatorThread.Start();
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

      var compiler = TscCompiler.Instance;
      var initialEpoch = compiler.Epoch;

      StartMutatorThread(seed: 42);

      for (var cycle = 0; cycle < 15; cycle++)
      {
        for (var f = 0; f < 5; f++)
          yield return null;

        var epochBefore = compiler.Epoch;
        var success = compiler.Recompile();

        if (success)
        {
          TscFileWatcher.ReloadAllCompiledScripts(vm, compiler);

          Assert.AreEqual(
            epochBefore + 1,
            compiler.Epoch,
            $"Cycle {cycle}: epoch should advance by 1 on success"
          );

          var health = vm.VerifyModuleHealth();
          Assert.IsNull(health, $"Cycle {cycle}: TDZ after reload: {health}");

          Assert.IsEmpty(
            vm.CapturedExceptions,
            $"Cycle {cycle}: JS exceptions:\n" + string.Join("\n", vm.CapturedExceptions)
          );
        }
        else
        {
          Assert.AreEqual(
            epochBefore,
            compiler.Epoch,
            $"Cycle {cycle}: epoch advanced despite failed compile"
          );
          Assert.IsNotEmpty(
            compiler.LastErrors,
            $"Cycle {cycle}: failed compile but no errors reported"
          );
        }
      }

      Assert.Greater(compiler.Epoch, initialEpoch, "At least one successful compile expected");
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

      var compiler = TscCompiler.Instance;

      for (var cycle = 0; cycle < 10; cycle++)
      {
        var path = GetTsPath("components/slime_wander.ts");
        File.AppendAllText(path, $"// reload-{cycle}\n");

        Assert.IsTrue(compiler.Recompile(), $"Cycle {cycle}: comment-only change must compile");
        TscFileWatcher.ReloadAllCompiledScripts(vm, compiler);

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

      var compiler = TscCompiler.Instance;

      // Background thread does comment-only mutations for pressure
      StartMutatorThread(seed: 99, commentOnly: true);

      for (var cycle = 0; cycle < 10; cycle++)
      {
        var targetFile = "components/slime_wander.ts";

        ApplyMutation(targetFile, MutationType.InjectSyntaxError, new System.Random(cycle));

        for (var f = 0; f < 3; f++)
          yield return null;

        var errorResult = compiler.Recompile();
        Assert.IsFalse(errorResult, $"Cycle {cycle}: compilation should fail with syntax error");

        ApplyMutation(targetFile, MutationType.FixSyntaxError, new System.Random(cycle));

        for (var f = 0; f < 3; f++)
          yield return null;

        var fixResult = compiler.Recompile();
        Assert.IsTrue(fixResult, $"Cycle {cycle}: compilation should succeed after fix");

        TscFileWatcher.ReloadAllCompiledScripts(vm, compiler);

        var health = vm.VerifyModuleHealth();
        Assert.IsNull(health, $"Cycle {cycle}: TDZ after recovery: {health}");
      }
    }

    [UnityTest]
    public IEnumerator StressTest_RapidCommentMutations_AllCompileSucceed()
    {
      yield return new EnterPlayMode();
      yield return null;

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm);
      vm.ClearCapturedExceptions();

      var compiler = TscCompiler.Instance;
      var initialEpoch = compiler.Epoch;
      var ourSuccessCount = 0;

      StartMutatorThread(seed: 777, minIntervalMs: 10, maxIntervalMs: 30, commentOnly: true);

      for (var cycle = 0; cycle < 10; cycle++)
      {
        for (var f = 0; f < 5; f++)
          yield return null;

        var epochBefore = compiler.Epoch;
        Assert.IsTrue(
          compiler.Recompile(),
          $"Cycle {cycle}: comment-only mutations must always compile"
        );

        Assert.AreEqual(
          epochBefore + 1,
          compiler.Epoch,
          $"Cycle {cycle}: epoch should advance by 1 on success"
        );
        ourSuccessCount++;

        TscFileWatcher.ReloadAllCompiledScripts(vm, compiler);
      }

      Assert.AreEqual(10, ourSuccessCount, "All 10 cycles should have succeeded");
      Assert.GreaterOrEqual(
        compiler.Epoch,
        initialEpoch + 10,
        "Epoch should advance at least 10 times"
      );

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
      var requests = em.AddBuffer<JsScriptRequest>(entity);
      requests.Add(
        new JsScriptRequest
        {
          scriptName = new FixedString64Bytes(scriptName),
          requestHash = JsScriptPathUtility.HashScriptName(scriptName),
          fulfilled = false,
        }
      );
      return entity;
    }
  }
}
