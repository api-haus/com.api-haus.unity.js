namespace UnityJS.Integration.CharacterController.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Components;
  using UnityJS.Entities.Core;
  using UnityJS.Integrations.Editor;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E tests verifying CharacterController integration.
  /// The fixture character_input system script reads _testInput and writes
  /// ECSCharacterControl/Stats on matching entities.
  /// </summary>
  [TestFixture]
  public class JsCharacterControllerE2ETests
  {
    /// <summary>
    /// Creates an entity with character components and a JsEntityId.
    /// No script attached — the system script finds it via ecs.query.
    /// </summary>
    static Entity CreateCharacterEntity(EntityManager em)
    {
      var types = new NativeList<ComponentType>(6, Allocator.Temp);
      types.Add(ComponentType.ReadWrite<JsEntityId>());
      types.Add(ComponentType.ReadWrite<LocalTransform>());
      types.Add(ComponentType.ReadWrite<ECSCharacterControl>());
      types.Add(ComponentType.ReadWrite<ECSCharacterStats>());
      types.Add(ComponentType.ReadWrite<ECSCharacterState>());
      types.Add(ComponentType.ReadWrite<ECSCharacterFixedInput>());
      var entity = em.CreateEntity(types.AsArray());
      types.Dispose();

      var entityId = JsEntityRegistry.AllocateId();
      JsEntityRegistry.RegisterImmediate(entity, entityId, em);
      em.SetComponentData(entity, new JsEntityId { value = entityId });
      em.SetComponentData(entity, LocalTransform.FromPosition(float3.zero));
      em.SetComponentData(entity, ECSCharacterStats.Default());

      return entity;
    }

    [UnityTest]
    public IEnumerator CharacterInput_MoveVector_WrittenFromTestInput()
    {
      // Register fixture search path BEFORE play mode so JsSystemRunner discovers the system
      var fixturesPath = IntegrationTestHarness.GetFixturesPath(
        "Integrations/CharacterController/Fixtures~"
      );
      var compiledPath = IntegrationTestHarness.CompileFixtures(fixturesPath);

      yield return new EnterPlayMode();

      // Register search path so system discovery finds systems/character_input
      using var searchPath = IntegrationTestHarness.UseIsolatedSearchPath(compiledPath);

      var world = World.DefaultGameObjectInjectionWorld;
      var em = world.EntityManager;

      // Create target entity with character components (system finds it via query)
      var entity = CreateCharacterEntity(em);

      // Wait for system discovery + query pipeline:
      // Frame 1: JsSystemRunner.EnsureVmReady discovers and loads systems/character_input
      // Frame 2: system's onUpdate runs, query registers as pending
      // Frame 3: PrewarmComponentQueries creates EntityQuery, precomputes
      // Frame 4+: system's query returns the entity, writes moveVector
      for (var frame = 0; frame < 10; frame++)
        yield return null;

      IntegrationTestHarness.AssertNoJsErrors("character input setup");

      // Inject movement input
      IntegrationTestHarness.SetTestInput(moveX: 1f, moveZ: 0.5f);

      // Let the system tick with input applied
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

      using var searchPath = IntegrationTestHarness.UseIsolatedSearchPath(compiledPath);

      var world = World.DefaultGameObjectInjectionWorld;
      var em = world.EntityManager;
      var entity = CreateCharacterEntity(em);

      // Wait for system discovery + query pipeline
      for (var frame = 0; frame < 10; frame++)
        yield return null;

      var initialStamina = em.GetComponentData<ECSCharacterStats>(entity).stamina;

      // Inject sprint input
      IntegrationTestHarness.SetTestInput(moveX: 1f, sprint: true);

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
  }
}
