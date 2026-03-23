namespace UnityJS.Entities.EditModeTests
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using Components;
  using Core;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;

  /// <summary>
  /// Programmatic scene builder for E2E tests. Creates entities the same way
  /// JsScriptBufferAuthoring.Baker does — JsScript buffer with stateRef=-1,
  /// JsEntityId, LocalTransform, JsEvent buffer. The fulfillment system
  /// (JsComponentInitSystem) processes them through the real pipeline.
  ///
  /// Usage:
  ///   using var scene = new SceneFixture(world);
  ///   scene.Spawn("components/slime_wander", new float3(0, 1, 0));
  ///   scene.Spawn("components/my_comp", float3.zero, @"{""speed"":10}");
  ///   yield return null; // let pipeline process
  ///
  /// Disposing destroys all entities created by this fixture.
  /// </summary>
  public sealed class SceneFixture : IDisposable
  {
    const string PACKAGE_NAME = "com.api-haus.unity.js";

    readonly World m_World;
    readonly EntityManager m_Em;
    readonly List<Entity> m_Entities = new();
    bool m_Disposed;
    bool m_FixturesRegistered;

    public SceneFixture(World world)
    {
      m_World = world ?? throw new ArgumentNullException(nameof(world));
      m_Em = world.EntityManager;
      RegisterFixturesSearchPath();
    }

    /// <summary>
    /// Resolve the package's Fixtures~ directory and register it as a script search path.
    /// Works for both embedded packages (Packages/com.api-haus.unity.js/) and
    /// resolved packages (Library/PackageCache/).
    /// </summary>
    void RegisterFixturesSearchPath()
    {
      var fixturesPath = GetPackageFixturesPath();
      if (fixturesPath != null && Directory.Exists(fixturesPath))
      {
        JsScriptSearchPaths.AddSearchPath(fixturesPath, 0);
        m_FixturesRegistered = true;
      }
    }

    /// <summary>
    /// Get the absolute path to the package's Fixtures~ directory.
    /// Returns the TscBuild output path (compiled JS) for runtime,
    /// or the source path for TS file access (hot reload tests).
    /// </summary>
    public static string GetPackageFixturesPath()
    {
      // TscBuild output: Library/TscBuild/Packages/com.api-haus.unity.js/Fixtures~
      // This is where compiled .js files live — the runtime loads these.
      var tscBuild = Path.GetFullPath(Path.Combine(
        "Library", "TscBuild", "Packages", PACKAGE_NAME, "Fixtures~"));
      if (Directory.Exists(tscBuild))
        return tscBuild;

      // Fallback: source directory (works if TS files have been compiled in-place)
      var embedded = Path.GetFullPath(Path.Combine("Packages", PACKAGE_NAME, "Fixtures~"));
      if (Directory.Exists(embedded))
        return embedded;

#if UNITY_EDITOR
      var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneFixture).Assembly);
      if (info != null)
      {
        var resolved = Path.Combine(info.resolvedPath, "Fixtures~");
        if (Directory.Exists(resolved))
          return resolved;
      }
#endif
      return null;
    }

    /// <summary>
    /// Get the source TS fixtures path (for hot reload tests that mutate files).
    /// </summary>
    public static string GetPackageFixturesSourcePath()
    {
      var embedded = Path.GetFullPath(Path.Combine("Packages", PACKAGE_NAME, "Fixtures~"));
      if (Directory.Exists(embedded))
        return embedded;

#if UNITY_EDITOR
      var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneFixture).Assembly);
      if (info != null)
      {
        var resolved = Path.Combine(info.resolvedPath, "Fixtures~");
        if (Directory.Exists(resolved))
          return resolved;
      }
#endif
      return null;
    }

    /// <summary>All entities created by this fixture.</summary>
    public IReadOnlyList<Entity> Entities => m_Entities;

    /// <summary>Number of entities in this fixture.</summary>
    public int Count => m_Entities.Count;

    /// <summary>Get entity by index in spawn order.</summary>
    public Entity this[int index] => m_Entities[index];

    /// <summary>The World this fixture operates on.</summary>
    public World World => m_World;

    /// <summary>The EntityManager for direct component reads in assertions.</summary>
    public EntityManager EntityManager => m_Em;

    /// <summary>
    /// Spawn an entity with a single component script. Replicates what
    /// JsScriptBufferAuthoring.Baker produces: JsScript buffer (stateRef=-1),
    /// JsEntityId, LocalTransform, JsEvent buffer.
    ///
    /// The entity is NOT immediately initialized — JsComponentInitSystem will
    /// process it on the next InitializationSystemGroup update, exactly like
    /// a baked subscene entity.
    /// </summary>
    /// <param name="scriptName">Script ID, e.g. "components/slime_wander"</param>
    /// <param name="position">World position (default: origin)</param>
    /// <param name="propertiesJson">Optional JSON property overrides, e.g. {"speed":10}</param>
    /// <returns>The spawned Entity for direct component assertions.</returns>
    public Entity Spawn(string scriptName, float3 position = default, string propertiesJson = null)
    {
      return Spawn(new[] { scriptName }, position, propertiesJson);
    }

    /// <summary>
    /// Spawn an entity with multiple component scripts (like multiple
    /// JsScriptAuthoring on one GameObject).
    /// </summary>
    public Entity Spawn(string[] scriptNames, float3 position = default, string propertiesJson = null)
    {
      if (m_Disposed)
        throw new ObjectDisposedException(nameof(SceneFixture));

      var entity = m_Em.CreateEntity();

      // LocalTransform + LocalToWorld — same as baker with TransformUsageFlags.Dynamic
      m_Em.AddComponentData(entity, new LocalTransform
      {
        Position = position,
        Rotation = quaternion.identity,
        Scale = 1f,
      });
      m_Em.AddComponentData(entity, new LocalToWorld
      {
        Value = float4x4.TRS(position, quaternion.identity, new float3(1f)),
      });

      // JsEntityId — persistent ID for JS references
      if (!JsEntityRegistry.IsCreated)
        JsEntityRegistry.Initialize(64);

      var entityId = JsEntityRegistry.AllocateId();
      JsEntityRegistry.RegisterImmediate(entity, entityId, m_Em);
      m_Em.AddComponentData(entity, new JsEntityId { value = entityId });

      // JsScript buffer — one entry per script, stateRef=-1 triggers fulfillment
      var scripts = m_Em.AddBuffer<JsScript>(entity);
      foreach (var name in scriptNames)
      {
        var scriptId = JsScriptPathUtility.NormalizeScriptId(name);
        var script = new JsScript
        {
          scriptName = new FixedString64Bytes(scriptId),
          stateRef = -1,
          entityIndex = 0,
          requestHash = JsScriptPathUtility.HashScriptName(scriptId),
          disabled = false,
          tickGroup = default,
        };

        if (!string.IsNullOrEmpty(propertiesJson))
          script.propertiesJson = new FixedString512Bytes(propertiesJson);

        scripts.Add(script);
      }

      // JsEvent buffer — same as baker
      m_Em.AddBuffer<JsEvent>(entity);

      m_Entities.Add(entity);
      return entity;
    }

    /// <summary>
    /// Spawn a bare entity with only LocalTransform and JsEntityId (no scripts).
    /// Useful for entities that systems query via ECS components.
    /// </summary>
    public Entity SpawnBare(float3 position = default)
    {
      if (m_Disposed)
        throw new ObjectDisposedException(nameof(SceneFixture));

      var entity = m_Em.CreateEntity();

      m_Em.AddComponentData(entity, new LocalTransform
      {
        Position = position,
        Rotation = quaternion.identity,
        Scale = 1f,
      });
      m_Em.AddComponentData(entity, new LocalToWorld
      {
        Value = float4x4.TRS(position, quaternion.identity, new float3(1f)),
      });

      if (!JsEntityRegistry.IsCreated)
        JsEntityRegistry.Initialize(64);

      var entityId = JsEntityRegistry.AllocateId();
      JsEntityRegistry.RegisterImmediate(entity, entityId, m_Em);
      m_Em.AddComponentData(entity, new JsEntityId { value = entityId });

      m_Entities.Add(entity);
      return entity;
    }

    /// <summary>
    /// Add a C# component to an entity in this fixture.
    /// For setting up test preconditions (e.g. adding Health before a system queries it).
    /// </summary>
    public void AddComponent<T>(Entity entity, T data) where T : unmanaged, IComponentData
    {
      m_Em.AddComponentData(entity, data);
    }

    /// <summary>
    /// Check if all scripted entities have been fulfilled (stateRef >= 0).
    /// </summary>
    public bool AllFulfilled()
    {
      foreach (var entity in m_Entities)
      {
        if (!m_Em.Exists(entity)) continue;
        if (!m_Em.HasBuffer<JsScript>(entity)) continue;

        var scripts = m_Em.GetBuffer<JsScript>(entity, true);
        for (var i = 0; i < scripts.Length; i++)
        {
          if (scripts[i].stateRef < 0)
            return false;
        }
      }
      return true;
    }

    /// <summary>
    /// Get the JS entity ID for an entity in this fixture.
    /// </summary>
    public int GetEntityId(Entity entity)
    {
      return m_Em.GetComponentData<JsEntityId>(entity).value;
    }

    /// <summary>
    /// Read LocalTransform.Position for an entity.
    /// </summary>
    public float3 GetPosition(Entity entity)
    {
      return m_Em.GetComponentData<LocalTransform>(entity).Position;
    }

    /// <summary>
    /// Destroy all entities and release resources.
    /// </summary>
    public void Dispose()
    {
      if (m_Disposed) return;
      m_Disposed = true;

      // Clean up _e2e_* globals to prevent cross-test state bleed
      var vm = Runtime.JsRuntimeManager.Instance;
      if (vm != null && vm.IsValid)
      {
        Runtime.JsEvalUtility.EvalVoid(
          "for(var k in globalThis){if(k.startsWith('_e2e'))delete globalThis[k]}");
      }

      // Unregister fixtures search path
      if (m_FixturesRegistered)
      {
        var path = GetPackageFixturesPath();
        if (path != null)
          JsScriptSearchPaths.RemoveSearchPath(path);
        m_FixturesRegistered = false;
      }

      // World may already be destroyed (e.g. after ExitPlayMode) — safe to skip
      if (m_World.IsCreated)
      {
        foreach (var entity in m_Entities)
        {
          if (m_Em.Exists(entity))
            m_Em.DestroyEntity(entity);
        }
      }
      m_Entities.Clear();
    }
  }
}
