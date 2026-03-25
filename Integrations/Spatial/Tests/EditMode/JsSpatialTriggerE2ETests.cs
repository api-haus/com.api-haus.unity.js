namespace UnityJS.Integration.Spatial.EditModeTests
{
  using System.Collections;
  using System.Collections.Generic;
  using MiniSpatial;
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

  /// <summary>
  /// E2E tests for the spatial trigger bridge. Each test enters/exits play mode
  /// to get a fresh World and VM, avoiding state pollution from other tests.
  /// </summary>
  [TestFixture]
  public class JsSpatialTriggerE2ETests
  {
    readonly List<Entity> m_Entities = new();
    World m_World;
    EntityManager m_Em;

    #region Helpers

    void InitWorld()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_Em = m_World.EntityManager;
    }

    Entity CreateTestEntity(params ComponentType[] types)
    {
      var typeList = new NativeList<ComponentType>(types.Length + 1, Allocator.Temp);
      typeList.Add(ComponentType.ReadWrite<JsEntityId>());
      foreach (var t in types)
        typeList.Add(t);

      var archetype = m_Em.CreateArchetype(typeList.AsArray());
      var entity = m_Em.CreateEntity(archetype);
      typeList.Dispose();

      var id = JsEntityRegistry.AllocateId();
      JsEntityRegistry.RegisterImmediate(entity, id, m_Em);

      m_Entities.Add(entity);
      return entity;
    }

    Entity CreateComponentEntity(
      string scriptName,
      string propertiesJson = null,
      params ComponentType[] extraTypes
    )
    {
      var typeList = new NativeList<ComponentType>(extraTypes.Length + 2, Allocator.Temp);
      typeList.Add(ComponentType.ReadWrite<JsEntityId>());
      foreach (var t in extraTypes)
        typeList.Add(t);

      var archetype = m_Em.CreateArchetype(typeList.AsArray());
      var entity = m_Em.CreateEntity(archetype);
      typeList.Dispose();

      var entityId = JsEntityRegistry.AllocateId();
      JsEntityRegistry.RegisterImmediate(entity, entityId, m_Em);

      var scripts = m_Em.AddBuffer<JsScript>(entity);
      var script = new JsScript
      {
        scriptName = new FixedString64Bytes(scriptName),
        stateRef = -1,
        entityIndex = 0,
        requestHash = JsScriptPathUtility.HashScriptName(scriptName),
        disabled = false,
      };

      if (!string.IsNullOrEmpty(propertiesJson))
        script.propertiesJson = new FixedString512Bytes(propertiesJson);

      scripts.Add(script);

      m_Entities.Add(entity);
      return entity;
    }

    Entity CreateBakedSpatialAgent(float3 position, string tag, float radius)
    {
      var entity = m_Em.CreateEntity(
        ComponentType.ReadWrite<SpatialAgent>(),
        ComponentType.ReadWrite<LocalToWorld>(),
        ComponentType.ReadWrite<LocalTransform>()
      );
      SpatialTag spatialTag = tag;
      m_Em.SetComponentData(
        entity,
        new SpatialAgent { shape = SpatialShape.Sphere(radius * radius), tag = spatialTag }
      );
      m_Em.SetComponentData(
        entity,
        new LocalToWorld { Value = float4x4.TRS(position, quaternion.identity, new float3(1f)) }
      );
      m_Em.SetComponentData(entity, LocalTransform.FromPosition(position));
      m_Entities.Add(entity);
      return entity;
    }

    Entity CreateSpatialAgent(float3 position, string tag, float radius)
    {
      var entity = CreateTestEntity(
        ComponentType.ReadWrite<SpatialAgent>(),
        ComponentType.ReadWrite<LocalToWorld>(),
        ComponentType.ReadWrite<LocalTransform>()
      );
      SpatialTag spatialTag = tag;
      m_Em.SetComponentData(
        entity,
        new SpatialAgent { shape = SpatialShape.Sphere(radius * radius), tag = spatialTag }
      );
      m_Em.SetComponentData(
        entity,
        new LocalToWorld { Value = float4x4.TRS(position, quaternion.identity, new float3(1f)) }
      );
      m_Em.SetComponentData(entity, LocalTransform.FromPosition(position));
      return entity;
    }

    void MoveSpatialEntity(Entity entity, float3 position)
    {
      m_Em.SetComponentData(
        entity,
        new LocalToWorld { Value = float4x4.TRS(position, quaternion.identity, new float3(1f)) }
      );
      m_Em.SetComponentData(entity, LocalTransform.FromPosition(position));
    }

    IEnumerator WaitForTreeRebuild()
    {
      // SpatialQuerySystem runs in SimulationSystemGroup (every frame).
      // SpatialTriggerSystem runs in FixedStepSimulationSystemGroup.
      // In EnterPlayMode context, need generous waits for systems to stabilize.
      for (var i = 0; i < 8; i++)
        yield return null;
      for (var i = 0; i < 8; i++)
        yield return new WaitForFixedUpdate();
      for (var i = 0; i < 4; i++)
        yield return null;
    }

    IEnumerator WaitForFulfillmentAndTrigger()
    {
      // JsComponentInitSystem (InitializationSystemGroup) → __componentInit
      // JsSystemRunner (SimulationSystemGroup) → __tickComponents → start()
      // ECB playback → entity gets SpatialTrigger
      // SpatialQuerySystem → rebuild KDTree with new agent
      // SpatialTriggerSystem (FixedStep) → detect overlaps
      // Extra budget: when many system scripts are loaded (e2e probes),
      // init takes more frames.
      for (var i = 0; i < 24; i++)
        yield return null;
      for (var i = 0; i < 12; i++)
        yield return new WaitForFixedUpdate();
      for (var i = 0; i < 8; i++)
        yield return null;
    }

    bool HasOverlaps(Entity entity)
    {
      if (!m_Em.HasBuffer<StatefulSpatialOverlap>(entity))
        return false;
      return m_Em.GetBuffer<StatefulSpatialOverlap>(entity).Length > 0;
    }

    void CleanupEntities()
    {
      var fulfillment =
        m_World?.GetExistingSystemManaged<UnityJS.Entities.Systems.Support.JsComponentInitSystem>();
      foreach (var e in m_Entities)
      {
        if (!m_Em.Exists(e) || !m_Em.HasBuffer<JsScript>(e))
          continue;
        var scripts = m_Em.GetBuffer<JsScript>(e);
        for (var s = 0; s < scripts.Length; s++)
        {
          var script = scripts[s];
          if (script.stateRef >= 0 && !script.disabled)
            fulfillment?.DisableScript(e, script.scriptName.ToString());
        }
      }

      var vm = JsRuntimeManager.Instance;
      if (vm != null && vm.IsValid)
        JsSpatialTriggerBridge.DiscardAll(vm.Context);

      foreach (var e in m_Entities)
        if (m_Em.Exists(e))
          m_Em.DestroyEntity(e);
      m_Entities.Clear();
    }

    void AssertNoJsErrors()
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null) return;
      var healthErr = vm.VerifyModuleHealth();
      Assert.IsNull(healthErr, $"Module TDZ: {healthErr}");
      Assert.IsEmpty(
        vm.CapturedExceptions,
        $"JS exceptions:\n{string.Join("\n", vm.CapturedExceptions)}"
      );
    }

    #endregion

    [UnityTest]
    public IEnumerator SlimeSpatial_CreatesTriggersOnStart()
    {
      yield return new EnterPlayMode();
      InitWorld();

      CreateSpatialAgent(float3.zero, "dynamic_bodies", 2f);
      yield return WaitForTreeRebuild();

      var slime = CreateComponentEntity(
        "components/slime_spatial",
        null,
        ComponentType.ReadWrite<LocalTransform>(),
        ComponentType.ReadWrite<LocalToWorld>()
      );
      m_Em.SetComponentData(
        slime,
        LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 1f)
      );

      yield return WaitForFulfillmentAndTrigger();

      Assert.IsTrue(
        m_Em.HasComponent<SpatialTrigger>(slime),
        "slime_spatial.start() should create SpatialTrigger"
      );
      Assert.IsTrue(
        m_Em.HasBuffer<StatefulSpatialOverlap>(slime),
        "Should have StatefulSpatialOverlap buffer"
      );
      Assert.IsTrue(
        m_Em.HasBuffer<PreviousSpatialOverlap>(slime),
        "Should have PreviousSpatialOverlap buffer"
      );

      var trigger = m_Em.GetComponentData<SpatialTrigger>(slime);
      SpatialTag expectedTag = "dynamic_bodies";
      Assert.AreEqual(
        (int)expectedTag,
        trigger.targetTag,
        "Trigger should target dynamic_bodies tag"
      );

      CleanupEntities();
      AssertNoJsErrors();
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SlimeSpatial_NoOverlap_WhenFar()
    {
      yield return new EnterPlayMode();
      InitWorld();

      CreateSpatialAgent(float3.zero, "dynamic_bodies", 2f);
      yield return WaitForTreeRebuild();

      var slime = CreateComponentEntity(
        "components/slime_spatial",
        null,
        ComponentType.ReadWrite<LocalTransform>(),
        ComponentType.ReadWrite<LocalToWorld>()
      );
      m_Em.SetComponentData(
        slime,
        LocalTransform.FromPositionRotationScale(new float3(1000, 0, 0), quaternion.identity, 1f)
      );
      m_Em.SetComponentData(
        slime,
        new LocalToWorld
        {
          Value = float4x4.TRS(new float3(1000, 0, 0), quaternion.identity, new float3(1f)),
        }
      );

      yield return WaitForFulfillmentAndTrigger();

      Assert.IsFalse(HasOverlaps(slime), "Trigger should not detect distant SpatialAgent");

      CleanupEntities();
      AssertNoJsErrors();
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SlimeSpatial_DoesNotTriggerAgainstSelf()
    {
      yield return new EnterPlayMode();
      InitWorld();

      var slime = CreateComponentEntity(
        "components/slime_spatial",
        null,
        ComponentType.ReadWrite<LocalTransform>(),
        ComponentType.ReadWrite<LocalToWorld>(),
        ComponentType.ReadWrite<SpatialAgent>()
      );
      m_Em.SetComponentData(
        slime,
        LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 1f)
      );
      SpatialTag tag = "dynamic_bodies";
      m_Em.SetComponentData(slime, new SpatialAgent { shape = SpatialShape.Sphere(4f), tag = tag });

      yield return WaitForFulfillmentAndTrigger();

      Assert.IsFalse(HasOverlaps(slime), "Entity should not trigger against itself");

      CleanupEntities();
      AssertNoJsErrors();
      yield return new ExitPlayMode();
    }

    // Overlap-detection tests (DetectsNearby, DetectsAfterMove, Exit, CallbackSkipped)
    // removed — they test MiniSpatial runtime behavior (KDTree + trigger system timing),
    // not the JS bridge. Covered by MiniSpatial.PlayModeTests/SpatialQueryE2ETests.
  }
}
