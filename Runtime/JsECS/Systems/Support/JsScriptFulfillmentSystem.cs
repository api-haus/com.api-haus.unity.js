namespace UnityJS.Entities.Systems.Support
{
	using Components;
	using Core;
	using UnityJS.Runtime;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;

	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial class JsScriptFulfillmentSystem : SystemBase
	{
		JsRuntimeManager m_Vm;
		EntityQuery m_RequestQuery;

		protected override void OnCreate()
		{
			JsEntityRegistry.Initialize();
			m_RequestQuery = GetEntityQuery(ComponentType.ReadWrite<JsScriptRequest>());
		}

		protected override void OnDestroy()
		{
			JsEntityRegistry.Dispose();
		}

		protected override void OnStartRunning()
		{
			m_Vm = JsRuntimeManager.Instance ?? JsRuntimeManager.GetOrCreate();

			m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
		}

		protected override void OnUpdate()
		{
			JsEntityRegistry.BeginFrame(EntityManager);

			if (m_Vm == null || !m_Vm.IsValid)
			{
				if (JsRuntimeManager.Instance != null && JsRuntimeManager.Instance.IsValid)
					m_Vm = JsRuntimeManager.Instance;
				else
					return;
			}

			if (m_RequestQuery.IsEmptyIgnoreFilter)
				return;

			using var ecb = new EntityCommandBuffer(Allocator.Temp);
			var entities = m_RequestQuery.ToEntityArray(Allocator.Temp);

			foreach (var entity in entities)
			{
				if (!EntityManager.Exists(entity))
					continue;

				if (!EntityManager.HasComponent<JsEntityId>(entity))
					continue;

				var requests = EntityManager.GetBuffer<JsScriptRequest>(entity);
				if (requests.Length == 0)
					continue;

				var hasUnfulfilled = false;
				for (var i = 0; i < requests.Length; i++)
				{
					if (!requests[i].fulfilled)
					{
						hasUnfulfilled = true;
						break;
					}
				}

				if (!hasUnfulfilled)
					continue;

				var entityId = JsEntityRegistry.GetOrAssignEntityId(entity, ecb, EntityManager);

				if (!EntityManager.HasBuffer<JsScript>(entity))
				{
					EntityManager.AddBuffer<JsScript>(entity);
					requests = EntityManager.GetBuffer<JsScriptRequest>(entity);
				}

				var scripts = EntityManager.GetBuffer<JsScript>(entity);

				for (var i = 0; i < requests.Length; i++)
				{
					var request = requests[i];
					if (request.fulfilled)
						continue;

					if (HasScriptWithHash(scripts, request.requestHash))
					{
						request.fulfilled = true;
						requests[i] = request;
						Log.Verbose("[JsFulfillment] Skipping duplicate request hash for entity {0}", entity);
						continue;
					}

					var scriptName = JsScriptPathUtility.NormalizeScriptId(request.scriptName.ToString());

					if (string.IsNullOrEmpty(scriptName))
					{
						Log.Error("[JsFulfillment] Script name is empty for entity {0}", entity);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

					if (!JsScriptPathUtility.ScriptExists(scriptName))
					{
						Log.Error(
							"[JsFulfillment] Script file missing: {0}",
							JsScriptPathUtility.GetScriptFilePath(scriptName)
						);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

		var scriptPath = JsScriptPathUtility.GetScriptFilePath(scriptName);
					var source = System.IO.File.ReadAllText(scriptPath);
					if (!m_Vm.LoadScriptAsModule(scriptName, source, scriptPath))
					{
						Log.Error("[JsFulfillment] Failed to load script: {0}", scriptName);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

					// Parse script annotations for tick group
					var annotations = JsScriptAnnotationParser.Parse(source);

					var stateRef = m_Vm.CreateEntityState(scriptName, entityId);
					if (stateRef < 0)
					{
						Log.Error(
							"[JsFulfillment] CreateEntityState failed for {0} entity={1}",
							scriptName,
							entityId
						);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

					scripts = EntityManager.GetBuffer<JsScript>(entity);
					scripts.Add(
						new JsScript
						{
							scriptName = new FixedString64Bytes(scriptName),
							stateRef = stateRef,
							entityIndex = entityId,
							requestHash = request.requestHash,
							disabled = false,
							tickGroup = annotations.tickGroup,
						}
					);

					Log.Verbose(
						"[JsFulfillment] Fulfilled script '{0}' on entity {1}, stateRef={2}",
						scriptName,
						entityId,
						stateRef
					);

					m_Vm.CallInit(scriptName, stateRef);

					requests = EntityManager.GetBuffer<JsScriptRequest>(entity);
					request.fulfilled = true;
					requests[i] = request;
				}
			}

			entities.Dispose();
			ecb.Playback(EntityManager);
		}

		static bool HasScriptWithHash(DynamicBuffer<JsScript> scripts, Hash128 hash)
		{
			for (var i = 0; i < scripts.Length; i++)
			{
				if (scripts[i].requestHash == hash)
					return true;
			}
			return false;
		}

		public bool DisableScript(Entity entity, string scriptName)
		{
			if (!EntityManager.HasBuffer<JsScript>(entity))
				return false;

			var scripts = EntityManager.GetBuffer<JsScript>(entity);
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				if (script.scriptName.ToString() == scriptName && !script.disabled && script.stateRef >= 0)
				{
					DisableScriptAtIndex(entity, scripts, i);
					return true;
				}
			}
			return false;
		}

		public bool DisableScriptByHash(Entity entity, Hash128 hash)
		{
			if (!EntityManager.HasBuffer<JsScript>(entity))
				return false;

			var scripts = EntityManager.GetBuffer<JsScript>(entity);
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				if (script.requestHash == hash && !script.disabled && script.stateRef >= 0)
				{
					DisableScriptAtIndex(entity, scripts, i);
					return true;
				}
			}
			return false;
		}

		void DisableScriptAtIndex(Entity entity, DynamicBuffer<JsScript> scripts, int index)
		{
			var script = scripts[index];
			var scriptName = script.scriptName.ToString();

			if (m_Vm != null && m_Vm.IsValid)
			{
				m_Vm.CallFunction(scriptName, "onDestroy", script.stateRef);
				m_Vm.ReleaseEntityState(script.stateRef);
			}

			Log.Verbose(
				"[JsFulfillment] Disabled script '{0}' on entity {1}",
				scriptName,
				script.entityIndex
			);

			script.stateRef = -1;
			script.disabled = true;
			scripts[index] = script;
		}

		public int GetEntityIdFromEntity(Entity entity) =>
			JsEntityRegistry.GetEntityIdFromEntity(entity, EntityManager);

		public Entity GetEntityFromId(int entityId) => JsEntityRegistry.GetEntityFromId(entityId);
	}
}
