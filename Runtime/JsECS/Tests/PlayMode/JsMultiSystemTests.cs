namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using Components;
  using Core;
  using NUnit.Framework;
  using QJS;
  using Runtime;
  using Unity.Entities;
  using Unity.Logging;
  using UnityEngine;
  using UnityEngine.TestTools;

  public class JsMultiSystemTests
  {
    JsRuntimeManager m_Vm;
    World m_World;
    EntityManager m_EntityManager;
    List<(string scriptId, int stateRef)> m_LoadedScripts;
    int m_UniqueCounter;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_EntityManager = m_World.EntityManager;

      m_Vm = JsRuntimeManager.GetOrCreate();
      m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
      JsECSBridge.Initialize(m_World);

      if (!JsEntityRegistry.IsCreated)
        JsEntityRegistry.Initialize(64);
      else
        JsEntityRegistry.Clear();

      m_LoadedScripts = new List<(string, int)>();
      m_UniqueCounter = 0;

      // Bulk-clear test globals
      EvalGlobal("for (var k in globalThis) { if (k.startsWith('_')) delete globalThis[k]; }");

      yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      foreach (var (_, stateRef) in m_LoadedScripts)
        if (m_Vm.ValidateStateRef(stateRef))
          m_Vm.ReleaseEntityState(stateRef);
      m_LoadedScripts.Clear();

      JsEntityRegistry.Clear();
      var query = m_EntityManager.CreateEntityQuery(typeof(JsEntityId));
      m_EntityManager.DestroyEntity(query);
      var cleanupQuery = m_EntityManager.CreateEntityQuery(typeof(JsScript));
      m_EntityManager.DestroyEntity(cleanupQuery);

      yield return null;
    }

    #region Helpers

    (string scriptId, int stateRef) LoadInlineScript(
      string scriptId,
      string source,
      int entityId = -1
    )
    {
      var filename = $"<test>/{scriptId}_{m_UniqueCounter++}.js";
      Assert.IsTrue(
        m_Vm.LoadScriptAsModule(scriptId, source, filename),
        $"Failed to load inline script '{scriptId}'"
      );

      var eid = entityId >= 0 ? entityId : JsEntityRegistry.AllocateId();
      var stateRef = m_Vm.CreateEntityState(scriptId, eid);
      Assert.Greater(stateRef, 0);
      m_LoadedScripts.Add((scriptId, stateRef));
      return (scriptId, stateRef);
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
          Log.Error("[Test] EvalGlobal exception");
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

    unsafe float GetGlobalFloat(string name)
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
        double d;
        QJS.JS_ToFloat64(m_Vm.Context, &d, val);
        QJS.JS_FreeValue(m_Vm.Context, val);
        return (float)d;
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

    struct ScriptEntry
    {
      public string scriptId;
      public int stateRef;
      public JsTickGroup group;
    }

    void SimulateFrameLoop(float[] frameDts, float fixedDt, List<ScriptEntry> scripts)
    {
      var fixedAccumulator = 0f;
      foreach (var frameDt in frameDts)
      {
        fixedAccumulator += frameDt;
        while (fixedAccumulator >= fixedDt)
        {
          foreach (var s in scripts)
            if (s.group == JsTickGroup.BeforePhysics)
              m_Vm.CallTick(s.scriptId, s.stateRef, fixedDt);
          foreach (var s in scripts)
            if (s.group == JsTickGroup.Fixed)
              m_Vm.CallTick(s.scriptId, s.stateRef, fixedDt);
          foreach (var s in scripts)
            if (s.group == JsTickGroup.AfterPhysics)
              m_Vm.CallTick(s.scriptId, s.stateRef, fixedDt);
          fixedAccumulator -= fixedDt;
        }

        foreach (var s in scripts)
          if (s.group == JsTickGroup.Variable)
            m_Vm.CallTick(s.scriptId, s.stateRef, frameDt);
        foreach (var s in scripts)
          if (s.group == JsTickGroup.AfterTransform)
            m_Vm.CallTick(s.scriptId, s.stateRef, frameDt);
      }
    }

    static int ComputeFixedTicks(float[] frameDts, float fixedDt)
    {
      var acc = 0f;
      var count = 0;
      foreach (var dt in frameDts)
      {
        acc += dt;
        while (acc >= fixedDt)
        {
          count++;
          acc -= fixedDt;
        }
      }

      return count;
    }

    #endregion

    #region Category 1: Multiple Concurrent Scripts

    [UnityTest]
    public IEnumerator MultipleScripts_StateIsolation()
    {
      for (var i = 0; i < 4; i++)
      {
        var src =
          $"export function onTick(state) {{ globalThis._iso_{i} = (globalThis._iso_{i} || 0) + 1; }}";
        LoadInlineScript($"iso_{i}", src);
      }

      for (var frame = 0; frame < 10; frame++)
      for (var i = 0; i < 4; i++)
        m_Vm.CallTick($"iso_{i}", m_LoadedScripts[i].stateRef, 0.016f);

      for (var i = 0; i < 4; i++)
        Assert.AreEqual(10, GetGlobalInt($"_iso_{i}"), $"Script {i} should have ticked 10 times");

      yield return null;
    }

    [UnityTest]
    public IEnumerator MultipleScripts_SharedGlobal()
    {
      for (var i = 0; i < 3; i++)
      {
        var src =
          "export function onTick(state) { globalThis._shared = (globalThis._shared || 0) + 1; }";
        LoadInlineScript($"shared_{i}", src);
      }

      for (var frame = 0; frame < 10; frame++)
      for (var i = 0; i < 3; i++)
        m_Vm.CallTick($"shared_{i}", m_LoadedScripts[i].stateRef, 0.016f);

      Assert.AreEqual(30, GetGlobalInt("_shared"));
      yield return null;
    }

    #endregion

    #region Category 2: Tick Group Filtering

    [UnityTest]
    public IEnumerator TickGroupFiltering_OnlyMatchingGroupExecutes()
    {
      var varSrc =
        "// @tick: variable\nexport function onTick(state) { globalThis._fVar = (globalThis._fVar || 0) + 1; }";
      var fixSrc =
        "// @tick: fixed\nexport function onTick(state) { globalThis._fFix = (globalThis._fFix || 0) + 1; }";
      var aftSrc =
        "// @tick: after_transform\nexport function onTick(state) { globalThis._fAft = (globalThis._fAft || 0) + 1; }";

      var varScript = LoadInlineScript("filt_var", varSrc);
      var fixScript = LoadInlineScript("filt_fix", fixSrc);
      var aftScript = LoadInlineScript("filt_aft", aftSrc);

      var varAnn = JsScriptAnnotationParser.Parse(varSrc);
      var fixAnn = JsScriptAnnotationParser.Parse(fixSrc);
      var aftAnn = JsScriptAnnotationParser.Parse(aftSrc);

      Assert.AreEqual(JsTickGroup.Variable, varAnn.tickGroup);
      Assert.AreEqual(JsTickGroup.Fixed, fixAnn.tickGroup);
      Assert.AreEqual(JsTickGroup.AfterTransform, aftAnn.tickGroup);

      // Simulate variable-only pass (5 frames)
      for (var i = 0; i < 5; i++)
        m_Vm.CallTick(varScript.scriptId, varScript.stateRef, 0.016f);

      // Simulate fixed-only pass (3 frames)
      for (var i = 0; i < 3; i++)
        m_Vm.CallTick(fixScript.scriptId, fixScript.stateRef, 0.02f);

      // Simulate after_transform-only pass (2 frames)
      for (var i = 0; i < 2; i++)
        m_Vm.CallTick(aftScript.scriptId, aftScript.stateRef, 0.016f);

      Assert.AreEqual(5, GetGlobalInt("_fVar"));
      Assert.AreEqual(3, GetGlobalInt("_fFix"));
      Assert.AreEqual(2, GetGlobalInt("_fAft"));
      yield return null;
    }

    [UnityTest]
    public IEnumerator TickGroupFiltering_DefaultIsVariable()
    {
      var source = "export function onTick(state) {}";
      var ann = JsScriptAnnotationParser.Parse(source);
      Assert.AreEqual(JsTickGroup.Variable, ann.tickGroup);
      Assert.IsFalse(ann.hasTickAnnotation);
      yield return null;
    }

    [UnityTest]
    public IEnumerator TickGroupFiltering_AllFiveGroups_Parsed()
    {
      var groups = new[]
      {
        ("// @tick: variable", JsTickGroup.Variable),
        ("// @tick: fixed", JsTickGroup.Fixed),
        ("// @tick: before_physics", JsTickGroup.BeforePhysics),
        ("// @tick: after_physics", JsTickGroup.AfterPhysics),
        ("// @tick: after_transform", JsTickGroup.AfterTransform),
      };

      foreach (var (annotation, expected) in groups)
      {
        var source = annotation + "\nexport function onTick(state) {}";
        var ann = JsScriptAnnotationParser.Parse(source);
        Assert.AreEqual(
          expected,
          ann.tickGroup,
          $"Annotation '{annotation}' should parse to {expected}"
        );
        Assert.IsTrue(ann.hasTickAnnotation);
      }

      yield return null;
    }

    #endregion

    #region Category 3: Execution Order

    [UnityTest]
    public IEnumerator ExecutionOrder_WithinSameGroup_Deterministic()
    {
      var labels = new[] { "A", "B", "C", "D" };
      for (var i = 0; i < 4; i++)
      {
        var label = labels[i];
        var src =
          $"export function onTick(state) {{ globalThis._log = (globalThis._log || '') + '{label}'; }}";
        LoadInlineScript($"order_{label}", src);
      }

      for (var frame = 0; frame < 3; frame++)
      for (var i = 0; i < 4; i++)
        m_Vm.CallTick($"order_{labels[i]}", m_LoadedScripts[i].stateRef, 0.016f);

      Assert.AreEqual("ABCDABCDABCD", GetGlobalString("_log"));
      yield return null;
    }

    [UnityTest]
    public IEnumerator ExecutionOrder_EntityBeforeSystem()
    {
      var entitySrc =
        "export function onTick(state) { globalThis._log = (globalThis._log || '') + 'E'; }";
      var systemSrc =
        "export function onUpdate(state) { globalThis._log = (globalThis._log || '') + 'S'; }";

      var entity = LoadInlineScript("ord_entity", entitySrc);
      var system = LoadInlineScript("ord_system", systemSrc);

      for (var frame = 0; frame < 4; frame++)
      {
        m_Vm.CallTick(entity.scriptId, entity.stateRef, 0.016f);
        m_Vm.CallFunction(system.scriptId, "onUpdate", system.stateRef);
      }

      Assert.AreEqual("ESESESESES".Substring(0, 8), GetGlobalString("_log"));
      yield return null;
    }

    [UnityTest]
    public IEnumerator ExecutionOrder_MixedTickGroups_CorrectPhaseOrder()
    {
      var scripts = new[]
      {
        ("BP", "before_physics"),
        ("FX", "fixed"),
        ("AP", "after_physics"),
        ("VA", "variable"),
        ("AT", "after_transform"),
      };

      var entries = new List<ScriptEntry>();
      foreach (var (label, group) in scripts)
      {
        var src =
          $"// @tick: {group}\nexport function onTick(state) {{ globalThis._log = (globalThis._log || '') + '{label},'; }}";
        var loaded = LoadInlineScript($"phase_{label}", src);
        var ann = JsScriptAnnotationParser.Parse(src);
        entries.Add(
          new ScriptEntry
          {
            scriptId = loaded.scriptId,
            stateRef = loaded.stateRef,
            group = ann.tickGroup,
          }
        );
      }

      // Also add a system script called via CallFunction
      var sysSrc =
        "export function onUpdate(state) { globalThis._log = (globalThis._log || '') + 'SR,'; }";
      var sys = LoadInlineScript("phase_SR", sysSrc);

      // Simulate 1 frame where fixedDt == frameDt so exactly 1 fixed step
      SimulateFrameLoop(new[] { 0.02f }, 0.02f, entries);
      m_Vm.CallFunction(sys.scriptId, "onUpdate", sys.stateRef);

      Assert.AreEqual("BP,FX,AP,VA,AT,SR,", GetGlobalString("_log"));
      yield return null;
    }

    #endregion

    #region Category 4: Fixed vs Variable Rate

    [UnityTest]
    public IEnumerator FixedVsVariable_UniformFrames()
    {
      var varSrc =
        "// @tick: variable\nexport function onTick(state) { globalThis._rateVar = (globalThis._rateVar || 0) + 1; }";
      var fixSrc =
        "// @tick: fixed\nexport function onTick(state) { globalThis._rateFix = (globalThis._rateFix || 0) + 1; }";

      var varS = LoadInlineScript("rate_var", varSrc);
      var fixS = LoadInlineScript("rate_fix", fixSrc);

      var frameDts = new float[60];
      for (var i = 0; i < 60; i++)
        frameDts[i] = 0.033f;
      const float fixedDt = 0.02f;

      var entries = new List<ScriptEntry>
      {
        new()
        {
          scriptId = varS.scriptId,
          stateRef = varS.stateRef,
          group = JsTickGroup.Variable,
        },
        new()
        {
          scriptId = fixS.scriptId,
          stateRef = fixS.stateRef,
          group = JsTickGroup.Fixed,
        },
      };

      SimulateFrameLoop(frameDts, fixedDt, entries);

      var expectedFix = ComputeFixedTicks(frameDts, fixedDt);
      Assert.AreEqual(60, GetGlobalInt("_rateVar"));
      Assert.AreEqual(expectedFix, GetGlobalInt("_rateFix"));
      yield return null;
    }

    [UnityTest]
    public IEnumerator FixedVsVariable_VariableFramerate()
    {
      var varSrc =
        "// @tick: variable\nexport function onTick(state) { globalThis._rateVar = (globalThis._rateVar || 0) + 1; }";
      var fixSrc =
        "// @tick: fixed\nexport function onTick(state) { globalThis._rateFix = (globalThis._rateFix || 0) + 1; }";

      var varS = LoadInlineScript("vfr_var", varSrc);
      var fixS = LoadInlineScript("vfr_fix", fixSrc);

      var frameDts = new float[40];
      for (var i = 0; i < 20; i++)
        frameDts[i] = 0.008f;
      for (var i = 20; i < 40; i++)
        frameDts[i] = 0.05f;
      const float fixedDt = 0.02f;

      var entries = new List<ScriptEntry>
      {
        new()
        {
          scriptId = varS.scriptId,
          stateRef = varS.stateRef,
          group = JsTickGroup.Variable,
        },
        new()
        {
          scriptId = fixS.scriptId,
          stateRef = fixS.stateRef,
          group = JsTickGroup.Fixed,
        },
      };

      SimulateFrameLoop(frameDts, fixedDt, entries);

      var expectedFix = ComputeFixedTicks(frameDts, fixedDt);
      Assert.AreEqual(40, GetGlobalInt("_rateVar"));
      Assert.AreEqual(expectedFix, GetGlobalInt("_rateFix"));
      yield return null;
    }

    [UnityTest]
    public IEnumerator FixedVsVariable_LargeSpike()
    {
      var fixSrc =
        "// @tick: fixed\nexport function onTick(state) { globalThis._rateFix = (globalThis._rateFix || 0) + 1; }";
      var fixS = LoadInlineScript("spike_fix", fixSrc);

      var frameDts = new float[10];
      for (var i = 0; i < 9; i++)
        frameDts[i] = 0.016f;
      frameDts[9] = 0.2f; // spike
      const float fixedDt = 0.02f;

      var entries = new List<ScriptEntry>
      {
        new()
        {
          scriptId = fixS.scriptId,
          stateRef = fixS.stateRef,
          group = JsTickGroup.Fixed,
        },
      };

      // Run first 9 frames
      var pre9 = new float[9];
      System.Array.Copy(frameDts, pre9, 9);
      SimulateFrameLoop(pre9, fixedDt, entries);
      var countBefore = GetGlobalInt("_rateFix");

      // Run spike frame
      SimulateFrameLoop(new[] { 0.2f }, fixedDt, entries);
      var countAfter = GetGlobalInt("_rateFix");

      var spikeTicks = countAfter - countBefore;
      Assert.GreaterOrEqual(
        spikeTicks,
        10,
        $"Spike frame should produce >=10 fixed steps, got {spikeTicks}"
      );
      yield return null;
    }

    [UnityTest]
    public IEnumerator FixedVsVariable_DtAccumulation()
    {
      var varSrc =
        "// @tick: variable\nexport function onTick(state) { globalThis._dtVar = (globalThis._dtVar || 0) + state.deltaTime; }";
      var fixSrc =
        "// @tick: fixed\nexport function onTick(state) { globalThis._dtFix = (globalThis._dtFix || 0) + state.deltaTime; }";

      var varS = LoadInlineScript("dtacc_var", varSrc);
      var fixS = LoadInlineScript("dtacc_fix", fixSrc);

      var frameDts = new[]
      {
        0.016f,
        0.033f,
        0.025f,
        0.050f,
        0.008f,
        0.016f,
        0.033f,
        0.025f,
        0.050f,
        0.008f,
        0.040f,
        0.012f,
        0.028f,
        0.045f,
        0.010f,
        0.016f,
        0.033f,
        0.025f,
        0.050f,
        0.008f,
        0.040f,
        0.012f,
        0.028f,
        0.045f,
        0.010f,
        0.016f,
        0.033f,
        0.025f,
        0.050f,
        0.008f,
      };
      const float fixedDt = 0.02f;

      var entries = new List<ScriptEntry>
      {
        new()
        {
          scriptId = varS.scriptId,
          stateRef = varS.stateRef,
          group = JsTickGroup.Variable,
        },
        new()
        {
          scriptId = fixS.scriptId,
          stateRef = fixS.stateRef,
          group = JsTickGroup.Fixed,
        },
      };

      SimulateFrameLoop(frameDts, fixedDt, entries);

      var expectedVarDt = 0f;
      foreach (var dt in frameDts)
        expectedVarDt += dt;
      var expectedFixTicks = ComputeFixedTicks(frameDts, fixedDt);

      Assert.AreEqual(expectedVarDt, GetGlobalFloat("_dtVar"), 0.001f);
      Assert.AreEqual(expectedFixTicks * fixedDt, GetGlobalFloat("_dtFix"), 0.001f);
      yield return null;
    }

    [UnityTest]
    public IEnumerator FixedRate_ZeroFixedTicksOnFastFrame()
    {
      var varSrc =
        "// @tick: variable\nexport function onTick(state) { globalThis._rateVar = (globalThis._rateVar || 0) + 1; }";
      var fixSrc =
        "// @tick: fixed\nexport function onTick(state) { globalThis._rateFix = (globalThis._rateFix || 0) + 1; }";

      var varS = LoadInlineScript("fast_var", varSrc);
      var fixS = LoadInlineScript("fast_fix", fixSrc);

      // 4 fast frames (each < fixedDt) accumulate, 5th triggers first fixed tick
      var frameDts = new[] { 0.0125f, 0.0125f, 0.0125f, 0.0125f, 0.0125f };
      const float fixedDt = 0.05f;

      var entries = new List<ScriptEntry>
      {
        new()
        {
          scriptId = varS.scriptId,
          stateRef = varS.stateRef,
          group = JsTickGroup.Variable,
        },
        new()
        {
          scriptId = fixS.scriptId,
          stateRef = fixS.stateRef,
          group = JsTickGroup.Fixed,
        },
      };

      SimulateFrameLoop(frameDts, fixedDt, entries);

      var expectedFix = ComputeFixedTicks(frameDts, fixedDt);
      Assert.AreEqual(5, GetGlobalInt("_rateVar"));
      Assert.AreEqual(expectedFix, GetGlobalInt("_rateFix"));
      Assert.Greater(expectedFix, 0, "Should have at least 1 fixed tick");
      yield return null;
    }

    #endregion

    #region Category 5: Variable DeltaTime Under Load

    [UnityTest]
    public IEnumerator VariableDt_ScriptReceivesCorrectDt()
    {
      var src =
        "export function onTick(state) { globalThis._dtLog = (globalThis._dtLog || '') + state.deltaTime.toFixed(4) + ','; }";
      var script = LoadInlineScript("dtlog", src);

      var dts = new[] { 0.016f, 0.033f, 0.100f, 0.005f, 0.050f };
      foreach (var dt in dts)
        m_Vm.CallTick(script.scriptId, script.stateRef, dt);

      var log = GetGlobalString("_dtLog");
      var parts = log.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
      Assert.AreEqual(dts.Length, parts.Length);

      for (var i = 0; i < dts.Length; i++)
      {
        var parsed = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(dts[i], parsed, 0.001f, $"Frame {i} dt mismatch");
      }

      yield return null;
    }

    [UnityTest]
    public IEnumerator VariableDt_ManyScripts_AllReceiveSameDt()
    {
      const int scriptCount = 20;
      for (var i = 0; i < scriptCount; i++)
      {
        var src = $"export function onTick(state) {{ globalThis._dtLoad_{i} = state.deltaTime; }}";
        LoadInlineScript($"dtload_{i}", src);
      }

      var frameDts = new[] { 0.016f, 0.033f, 0.050f, 0.008f, 0.100f };
      foreach (var frameDt in frameDts)
      {
        for (var i = 0; i < scriptCount; i++)
          m_Vm.CallTick($"dtload_{i}", m_LoadedScripts[i].stateRef, frameDt);

        for (var i = 0; i < scriptCount; i++)
          Assert.AreEqual(
            frameDt,
            GetGlobalFloat($"_dtLoad_{i}"),
            0.001f,
            $"Script {i} should receive dt={frameDt}"
          );
      }

      yield return null;
    }

    #endregion

    #region Category 6: System + Entity Scripts

    [UnityTest]
    public IEnumerator SystemAndEntity_BothExecute()
    {
      var entitySrc =
        "export function onTick(state) { globalThis._log = (globalThis._log || '') + 'E'; }";
      var systemSrc =
        "export function onUpdate(state) { globalThis._log = (globalThis._log || '') + 'S'; }";

      var entity = LoadInlineScript("se_entity", entitySrc);
      var system = LoadInlineScript("se_system", systemSrc);

      for (var frame = 0; frame < 5; frame++)
      {
        m_Vm.CallTick(entity.scriptId, entity.stateRef, 0.016f);
        m_Vm.CallFunction(system.scriptId, "onUpdate", system.stateRef);
      }

      Assert.AreEqual("ESESESESESES".Substring(0, 10), GetGlobalString("_log"));
      yield return null;
    }

    [UnityTest]
    public IEnumerator SystemAndEntity_FixedEntityVariableSystem()
    {
      var fixSrc =
        "// @tick: fixed\nexport function onTick(state) { globalThis._fixCount = (globalThis._fixCount || 0) + 1; }";
      var sysSrc =
        "export function onUpdate(state) { globalThis._sysCount = (globalThis._sysCount || 0) + 1; }";

      var fixS = LoadInlineScript("sefx_entity", fixSrc);
      var sysS = LoadInlineScript("sefx_system", sysSrc);

      var frameDts = new float[10];
      for (var i = 0; i < 10; i++)
        frameDts[i] = 0.033f;
      const float fixedDt = 0.02f;

      // Simulate manually: fixed entity ticks in fixed loop, system once per frame
      var fixedAccumulator = 0f;
      foreach (var frameDt in frameDts)
      {
        fixedAccumulator += frameDt;
        while (fixedAccumulator >= fixedDt)
        {
          m_Vm.CallTick(fixS.scriptId, fixS.stateRef, fixedDt);
          fixedAccumulator -= fixedDt;
        }

        m_Vm.CallFunction(sysS.scriptId, "onUpdate", sysS.stateRef);
      }

      var expectedFix = ComputeFixedTicks(frameDts, fixedDt);
      Assert.AreEqual(10, GetGlobalInt("_sysCount"));
      Assert.AreEqual(expectedFix, GetGlobalInt("_fixCount"));
      yield return null;
    }

    #endregion

    #region Category 7: Edge Cases & Stress

    [UnityTest]
    public IEnumerator Stress_50Scripts_100Frames()
    {
      const int scriptCount = 50;
      const int frameCount = 100;

      for (var i = 0; i < scriptCount; i++)
      {
        var src =
          $"export function onTick(state) {{ globalThis._stress_{i} = (globalThis._stress_{i} || 0) + 1; }}";
        LoadInlineScript($"stress_{i}", src);
      }

      for (var frame = 0; frame < frameCount; frame++)
      for (var i = 0; i < scriptCount; i++)
        m_Vm.CallTick($"stress_{i}", m_LoadedScripts[i].stateRef, 0.016f);

      for (var i = 0; i < scriptCount; i++)
        Assert.AreEqual(frameCount, GetGlobalInt($"_stress_{i}"), $"Script {i}");

      yield return null;
    }

    [UnityTest]
    public IEnumerator DisabledScript_OnlyEnabledRuns()
    {
      var enabledSrc =
        "export function onTick(state) { globalThis._enabled = (globalThis._enabled || 0) + 1; }";
      var disabledSrc =
        "export function onTick(state) { globalThis._disabled = (globalThis._disabled || 0) + 1; }";

      var enabled = LoadInlineScript("dis_enabled", enabledSrc);
      LoadInlineScript("dis_disabled", disabledSrc); // loaded but never ticked

      for (var frame = 0; frame < 10; frame++)
        m_Vm.CallTick(enabled.scriptId, enabled.stateRef, 0.016f);

      Assert.AreEqual(10, GetGlobalInt("_enabled"));
      Assert.AreEqual(0, GetGlobalInt("_disabled"));
      yield return null;
    }

    [UnityTest]
    public IEnumerator InvalidStateRef_AfterRelease()
    {
      var src = "export function onTick(state) { globalThis._inv = (globalThis._inv || 0) + 1; }";
      var script = LoadInlineScript("inv_release", src);

      m_Vm.CallTick(script.scriptId, script.stateRef, 0.016f);
      Assert.AreEqual(1, GetGlobalInt("_inv"));

      m_Vm.ReleaseEntityState(script.stateRef);
      Assert.IsFalse(m_Vm.ValidateStateRef(script.stateRef));

      // CallTick with invalid stateRef should not crash (passes JS_UNDEFINED as state)
      var result = m_Vm.CallTick(script.scriptId, script.stateRef, 0.016f);
      // Should still "succeed" (function exists, just gets undefined state)
      Assert.IsTrue(result);

      yield return null;
    }

    #endregion
  }
}
