namespace UnityJS.Integration.CharacterController.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Integrations.Editor;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E tests verifying CharacterController integration survives play mode cycles
  /// and fixture scripts execute correctly.
  /// </summary>
  [TestFixture]
  public class JsCharacterControllerE2ETests
  {
    [UnityTest]
    public IEnumerator PlayModeCycle_CharacterInputFixture_RunsWithoutErrors()
    {
      var fixturesPath = IntegrationTestHarness.GetFixturesPath(
        "Integrations/CharacterController/Fixtures~");
      var compiledPath = IntegrationTestHarness.CompileFixtures(fixturesPath);

      yield return new EnterPlayMode();

      using var searchPath = IntegrationTestHarness.UseSearchPath(compiledPath);
      var em = World.DefaultGameObjectInjectionWorld.EntityManager;
      var entity = IntegrationTestHarness.CreateScriptedEntity(em,
        "systems/character_input",
        ComponentType.ReadWrite<ECSCharacterControl>(),
        ComponentType.ReadWrite<ECSCharacterStats>(),
        ComponentType.ReadWrite<ECSCharacterState>(),
        ComponentType.ReadWrite<ECSCharacterFixedInput>());
      em.SetComponentData(entity, ECSCharacterStats.Default());

      yield return new WaitForSeconds(0.3f);

      IntegrationTestHarness.AssertNoJsErrors("character input fixture");
      IntegrationTestHarness.AssertEntitiesFulfilled(em, entity);

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
  }
}
