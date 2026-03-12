namespace UnityJS.Entities.Systems
{
	using System.Collections.Generic;
	using System.IO;
	using Core;
	using UnityJS.Runtime;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Transforms;
	using UnityEngine;

	public struct JsSystemManifest : IComponentData
	{
		public bool initialized;
	}

	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(JsScriptingSystem))]
	public partial class JsSystemRunner : SystemBase
	{
		JsRuntimeManager m_Vm;
		readonly List<string> m_SystemNames = new();
		readonly Dictionary<string, int> m_SystemStateRefs = new();
		bool m_BridgesRegistered;

		ComponentLookup<LocalTransform> m_TransformLookup;
		EntityQuery m_SentinelQuery;

		protected override void OnCreate()
		{
			m_TransformLookup = GetComponentLookup<LocalTransform>();
			m_SentinelQuery = GetEntityQuery(ComponentType.ReadWrite<Components.JsEntityId>());
		}

		protected override void OnStartRunning()
		{
			m_Vm = JsRuntimeManager.GetOrCreate();

			if (!m_BridgesRegistered)
			{
				m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
				m_Vm.RegisterBridgeNow(JsSystemBridge.Register);
				m_Vm.RegisterBridgeNow(JsQueryBridge.Register);
				m_Vm.RegisterBridgeNow(JsComponentRegistry.RegisterAllBridges);
				m_Vm.RegisterBridgeNow(JsComponentStore.Register);
				m_BridgesRegistered = true;
			}

			var scriptingSystem = World.GetOrCreateSystemManaged<JsScriptingSystem>();
			JsECSBridge.Initialize(World);
			JsEntityRegistry.Initialize();
			JsQueryBridge.Initialize(EntityManager);

			if (!SystemAPI.HasSingleton<JsSystemManifest>())
			{
				var entity = EntityManager.CreateEntity();
				EntityManager.AddComponentData(entity, new JsSystemManifest { initialized = false });
			}

			DiscoverAndLoadSystems();
		}

		protected override void OnDestroy()
		{
			JsQueryBridge.Shutdown();
			JsComponentStore.Shutdown();
		}

		void DiscoverAndLoadSystems()
		{
			var systemsPath = Path.Combine(Application.streamingAssetsPath, "js", "systems");
			if (!Directory.Exists(systemsPath))
			{
				Log.Warning("[JsSystemRunner] No systems directory at {0}", systemsPath);
				ref var manifest = ref SystemAPI.GetSingletonRW<JsSystemManifest>().ValueRW;
				manifest.initialized = true;
				return;
			}

			var files = Directory.GetFiles(systemsPath, "*.js", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				var systemName = Path.GetFileNameWithoutExtension(file);
				if (m_SystemStateRefs.ContainsKey(systemName))
					continue;

				LoadSystem(systemName, file);
			}

			ref var m = ref SystemAPI.GetSingletonRW<JsSystemManifest>().ValueRW;
			m.initialized = true;
		}

		void LoadSystem(string systemName, string filePath)
		{
			var source = File.ReadAllText(filePath);
			var scriptId = "system:" + systemName;

			if (!m_Vm.LoadScriptAsModule(scriptId, source, filePath))
			{
				Log.Error("[JsSystemRunner] Failed to load system '{0}'", systemName);
				return;
			}

			// System scripts have no entity (entityId = -1)
			var stateRef = m_Vm.CreateEntityState(scriptId, -1);
			if (stateRef < 0)
			{
				Log.Error("[JsSystemRunner] Failed to create state for system '{0}'", systemName);
				return;
			}

			m_SystemStateRefs[systemName] = stateRef;
			m_SystemNames.Add(systemName);
		}

		public void ReloadSystem(string systemName)
		{
			if (m_SystemStateRefs.TryGetValue(systemName, out var oldRef))
			{
				m_Vm.ReleaseEntityState(oldRef);
				m_SystemStateRefs.Remove(systemName);
				m_SystemNames.Remove(systemName);
			}

			var systemsPath = Path.Combine(Application.streamingAssetsPath, "js", "systems");
			var filePath = Path.Combine(systemsPath, systemName + ".js");
			if (File.Exists(filePath))
			{
				LoadSystem(systemName, filePath);
			}
		}

		protected override void OnUpdate()
		{
			if (m_Vm == null || !m_Vm.IsValid)
				return;

			if (m_SystemNames.Count == 0)
				return;

			var deltaTime = SystemAPI.Time.DeltaTime;
			if (deltaTime <= 0f)
				deltaTime = UnityEngine.Time.deltaTime;

			var elapsedTime = SystemAPI.Time.ElapsedTime;

			RegisterSentinelEntities();

			EntityManager.CompleteDependencyBeforeRW<LocalTransform>();
			m_TransformLookup.Update(this);

			JsSystemBridge.UpdateContext(deltaTime, elapsedTime);

			ref var sysState = ref CheckedStateRef;
			JsComponentRegistry.UpdateAllLookups(ref sysState);

			var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
			var ecb = ecbSingleton.CreateCommandBuffer(World.Unmanaged);
			var scriptBufferLookup = GetBufferLookup<Components.JsScript>(true);
			JsECSBridge.UpdateBurstContext(ecb, deltaTime, m_TransformLookup, scriptBufferLookup);

			foreach (var systemName in m_SystemNames)
			{
				if (!m_SystemStateRefs.TryGetValue(systemName, out var stateRef))
					continue;

				m_Vm.UpdateStateTimings(stateRef, deltaTime, elapsedTime);

				var scriptId = "system:" + systemName;
				m_Vm.CallFunction(scriptId, "onUpdate", stateRef);
			}
		}

		void RegisterSentinelEntities()
		{
			var entities = m_SentinelQuery.ToEntityArray(Allocator.Temp);
			var ids = m_SentinelQuery.ToComponentDataArray<Components.JsEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				if (ids[i].value != 0)
					continue;

				var newId = JsEntityRegistry.AllocateId();
				if (newId <= 0)
					continue;

				EntityManager.SetComponentData(entities[i], new Components.JsEntityId { value = newId });
				JsEntityRegistry.RegisterImmediate(entities[i], newId, EntityManager);
			}

			entities.Dispose();
			ids.Dispose();
		}
	}
}
