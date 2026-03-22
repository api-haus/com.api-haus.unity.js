namespace UnityJS.Integration.CharacterController.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Integrations.Editor;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E tests verifying CharacterController integration: fixture script reads
  /// _testInput and writes ECSCharacterControl, stamina, and jump state.
  /// No scene loading — entities created programmatically.
  /// </summary>
  [TestFixture]
  public class JsCharacterControllerE2ETests
  {
    [UnityTest]
    public IEnumerator CharacterInput_MoveVector_WrittenFromTestInput()
    {
      var fixturesPath = IntegrationTestHarness.GetFixturesPath(
        "Integrations/CharacterController/Fixtures~"
      );
      var compiledPath = IntegrationTestHarness.CompileFixtures(fixturesPath);

      yield return new EnterPlayMode();

      using var searchPath = IntegrationTestHarness.UseSearchPath(compiledPath);
      var em = World.DefaultGameObjectInjectionWorld.EntityManager;
      var entity = IntegrationTestHarness.CreateScriptedEntity(
        em,
        "systems/character_input",
        ComponentType.ReadWrite<ECSCharacterControl>(),
        ComponentType.ReadWrite<ECSCharacterStats>(),
        ComponentType.ReadWrite<ECSCharacterState>(),
        ComponentType.ReadWrite<ECSCharacterFixedInput>()
      );
      em.SetComponentData(entity, ECSCharacterStats.Default());

      // Wait for fulfillment + query pipeline:
      // Frame 1: fulfillment processes request
      // Frame 2: script ticks, query registers as pending
      // Frame 3: JsSystemRunner flushes pending query, precomputes
      // Frame 4: script ticks with query results
      // Frame 5+: stable
      for (var frame = 0; frame < 10; frame++)
        yield return null;

      IntegrationTestHarness.AssertNoJsErrors("character input setup");
      IntegrationTestHarness.AssertEntitiesFulfilled(em, entity);

      // Inject movement input
      IntegrationTestHarness.SetTestInput(moveX: 1f, moveZ: 0.5f);

      // Let the script tick with the input applied
      for (var frame = 0; frame < 10; frame++)
        yield return null;

      IntegrationTestHarness.AssertNoJsErrors("character input moveVector");

      var ctrl = em.GetComponentData<ECSCharacterControl>(entity);
      Assert.AreEqual(1f, ctrl.moveVector.x, 0.01f, "moveVector.x should match injected moveX");
      Assert.AreEqual(0f, ctrl.moveVector.y, 0.01f, "moveVector.y should be 0 (XZ plane)");
      Assert.AreEqual(0.5f, ctrl.moveVector.z, 0.01f, "moveVector.z should match injected moveZ");

      IntegrationTestHarness.ClearTestInput();
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator CharacterInput_Sprint_DrainsStamina()
    {
      var fixturesPath = IntegrationTestHarness.GetFixturesPath(
        "Integrations/CharacterController/Fixtures~"
      );
      var compiledPath = IntegrationTestHarness.CompileFixtures(fixturesPath);

      yield return new EnterPlayMode();

      using var searchPath = IntegrationTestHarness.UseSearchPath(compiledPath);
      var em = World.DefaultGameObjectInjectionWorld.EntityManager;
      var entity = IntegrationTestHarness.CreateScriptedEntity(
        em,
        "systems/character_input",
        ComponentType.ReadWrite<ECSCharacterControl>(),
        ComponentType.ReadWrite<ECSCharacterStats>(),
        ComponentType.ReadWrite<ECSCharacterState>(),
        ComponentType.ReadWrite<ECSCharacterFixedInput>()
      );
      em.SetComponentData(entity, ECSCharacterStats.Default());

      // Wait for fulfillment + query pipeline
      for (var frame = 0; frame < 10; frame++)
        yield return null;

      IntegrationTestHarness.AssertEntitiesFulfilled(em, entity);

      var initialStamina = em.GetComponentData<ECSCharacterStats>(entity).stamina;

      // Inject sprint input
      IntegrationTestHarness.SetTestInput(moveX: 1f, sprint: true);

      // Let the script tick with sprint active
      for (var frame = 0; frame < 10; frame++)
        yield return null;

      var stats = em.GetComponentData<ECSCharacterStats>(entity);
      var ctrl = em.GetComponentData<ECSCharacterControl>(entity);

      Assert.Less(stats.stamina, initialStamina, "Stamina should decrease while sprinting");
      Assert.IsTrue(ctrl.sprint, "ctrl.sprint should be true");

      IntegrationTestHarness.ClearTestInput();
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PlayModeCycle_CharacterSystems_Exist()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      Assert.IsNotNull(world, "World should exist in play mode");

      var inputBridge = world.GetExistingSystem<ECSCharacterInputBridgeSystem>();
      var physicsUpdate = world.GetExistingSystem<ECSCharacterPhysicsUpdateSystem>();
      var variableUpdate = world.GetExistingSystem<ECSCharacterVariableUpdateSystem>();
      var fixedTick = world.GetExistingSystem<FixedTickSystem>();

      Assert.AreNotEqual(default(SystemHandle), inputBridge, "ECSCharacterInputBridgeSystem should exist");
      Assert.AreNotEqual(default(SystemHandle), physicsUpdate, "ECSCharacterPhysicsUpdateSystem should exist");
      Assert.AreNotEqual(default(SystemHandle), variableUpdate, "ECSCharacterVariableUpdateSystem should exist");
      Assert.AreNotEqual(default(SystemHandle), fixedTick, "FixedTickSystem should exist");

      yield return null;

      var vm = JsRuntimeManager.Instance;
      if (vm != null)
        Assert.IsEmpty(vm.CapturedExceptions, "No JS exceptions after character system frame");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator MultiplePlayModeCycles_CharacterIntegration_Stable()
    {
      for (var cycle = 0; cycle < 2; cycle++)
      {
        yield return new EnterPlayMode();

        var world = World.DefaultGameObjectInjectionWorld;
        Assert.IsNotNull(world, $"World should exist in cycle {cycle}");

        yield return null;

        var vm = JsRuntimeManager.Instance;
        if (vm != null)
          Assert.IsEmpty(vm.CapturedExceptions, $"No JS exceptions in cycle {cycle}");

        yield return new ExitPlayMode();
      }
    }
    static unsafe string EvalString(JsRuntimeManager vm, string code)
    {
      var sourceBytes = System.Text.Encoding.UTF8.GetBytes(code + '\0');
      var fileBytes = System.Text.Encoding.UTF8.GetBytes("<diag>\0");
      fixed (byte* pSrc = sourceBytes, pFile = fileBytes)
      {
        var result = UnityJS.QJS.QJS.JS_Eval(vm.Context, pSrc, sourceBytes.Length - 1, pFile,
          UnityJS.QJS.QJS.JS_EVAL_TYPE_GLOBAL);
        if (UnityJS.QJS.QJS.IsException(result))
        {
          UnityJS.QJS.QJS.JS_FreeValue(vm.Context, UnityJS.QJS.QJS.JS_GetException(vm.Context));
          return "<exception>";
        }
        var str = UnityJS.QJS.QJS.ToManagedString(vm.Context, result);
        UnityJS.QJS.QJS.JS_FreeValue(vm.Context, result);
        return str ?? "<null>";
      }
    }
  }
}
