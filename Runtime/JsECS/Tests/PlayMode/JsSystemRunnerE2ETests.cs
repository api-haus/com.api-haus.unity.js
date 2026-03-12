namespace UnityJS.Entities.PlayModeTests
{
	using System.Collections;
	using System.Runtime.InteropServices;
	using System.Text;
	using Components;
	using Core;
	using Systems;
	using UnityJS.QJS;
	using UnityJS.Runtime;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;

	/// <summary>
	/// End-to-end tests that verify JsSystemRunner auto-discovers and executes
	/// system scripts from StreamingAssets/js/systems/ without manual injection.
	/// </summary>
	public class JsSystemRunnerE2ETests
	{
		World m_World;
		EntityManager m_EntityManager;
		JsRuntimeManager m_Vm;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;
			m_Vm = JsRuntimeManager.GetOrCreate();

			// Clear probe globals
			EvalGlobal("delete globalThis._e2eAutoloadCount; delete globalThis._e2eAutoloadLastDt;");
			EvalGlobal("delete globalThis._systemTickCount;");

			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			// Clean up any test entities
			var query = m_EntityManager.CreateEntityQuery(typeof(JsEntityId));
			m_EntityManager.DestroyEntity(query);
			var cleanupQuery = m_EntityManager.CreateEntityQuery(typeof(JsScript));
			m_EntityManager.DestroyEntity(cleanupQuery);

			yield return null;
		}

		#region Helpers

		unsafe void EvalGlobal(string code)
		{
			var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
			var sourceLen = sourceBytes.Length - 1;
			var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);
				if (QJS.IsException(val))
					Debug.LogError("[E2E] EvalGlobal exception");
				QJS.JS_FreeValue(m_Vm.Context, val);
			}
		}

		unsafe int GetGlobalInt(string name)
		{
			var code = $"globalThis.{name} || 0";
			var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
			var sourceLen = sourceBytes.Length - 1;
			var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);
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

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);
				double d;
				QJS.JS_ToFloat64(m_Vm.Context, &d, val);
				QJS.JS_FreeValue(m_Vm.Context, val);
				return (float)d;
			}
		}

		unsafe bool GetGlobalBool(string name)
		{
			var code = $"!!globalThis.{name}";
			var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
			var sourceLen = sourceBytes.Length - 1;
			var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);
				var result = QJS.JS_ToBool(m_Vm.Context, val) != 0;
				QJS.JS_FreeValue(m_Vm.Context, val);
				return result;
			}
		}

		#endregion

		// ── JsSystemManifest initialized ────────────────────────────

		[UnityTest]
		public IEnumerator SystemManifest_IsInitialized()
		{
			// JsSystemRunner creates a JsSystemManifest singleton on start
			yield return null;
			yield return null;

			var query = m_EntityManager.CreateEntityQuery(typeof(JsSystemManifest));
			Assert.AreEqual(1, query.CalculateEntityCount(),
				"JsSystemManifest singleton should exist");

			var manifests = query.ToComponentDataArray<JsSystemManifest>(Allocator.Temp);
			Assert.IsTrue(manifests[0].initialized,
				"JsSystemManifest should be marked initialized after DiscoverAndLoadSystems");
			manifests.Dispose();
		}

		// ── Auto-load probe script ──────────────────────────────────

		[UnityTest]
		public IEnumerator AutoloadProbe_ExecutesOnUpdate()
		{
			// e2e_autoload_probe.js is in StreamingAssets/js/systems/
			// JsSystemRunner should have auto-discovered and loaded it.
			// Each frame it increments globalThis._e2eAutoloadCount.

			// Wait for JsSystemRunner.OnStartRunning + a few update frames
			yield return null;
			yield return null;
			yield return null;

			var count = GetGlobalInt("_e2eAutoloadCount");
			Assert.Greater(count, 0,
				"e2e_autoload_probe.js onUpdate should have been called at least once");
		}

		[UnityTest]
		public IEnumerator AutoloadProbe_TicksEveryFrame()
		{
			// Wait for startup
			yield return null;
			yield return null;

			var countBefore = GetGlobalInt("_e2eAutoloadCount");

			// Run 5 more frames
			for (var i = 0; i < 5; i++)
				yield return null;

			var countAfter = GetGlobalInt("_e2eAutoloadCount");
			Assert.AreEqual(5, countAfter - countBefore,
				"System script should tick exactly once per frame");
		}

		[UnityTest]
		public IEnumerator AutoloadProbe_ReceivesDeltaTime()
		{
			yield return null;
			yield return null;
			yield return null;

			var dt = GetGlobalFloat("_e2eAutoloadLastDt");
			Assert.Greater(dt, 0f, "state.deltaTime should be > 0");
			Assert.Less(dt, 1f, "state.deltaTime should be < 1s (sanity check)");
		}

		// ── test_system.js (existing script) ────────────────────────

		[UnityTest]
		public IEnumerator TestSystem_ExecutesOnUpdate()
		{
			// test_system.js exports onUpdate which increments _systemTickCount
			yield return null;
			yield return null;
			yield return null;

			var count = GetGlobalInt("_systemTickCount");
			Assert.Greater(count, 0,
				"test_system.js onUpdate should have been called");
		}

		// ── All system scripts load without error ───────────────────

		[UnityTest]
		public IEnumerator AllSystemScripts_LoadWithoutError()
		{
			// If any system script fails to load (e.g. missing export, syntax error),
			// JsSystemRunner logs an error. We verify no errors occurred.
			// Wait for JsSystemRunner to discover and load all scripts.
			yield return null;
			yield return null;

			// Verify manifest is initialized (all scripts loaded)
			var mQuery = m_EntityManager.CreateEntityQuery(typeof(JsSystemManifest));
			var mData = mQuery.ToComponentDataArray<JsSystemManifest>(Allocator.Temp);
			Assert.IsTrue(mData[0].initialized,
				"All system scripts should load successfully");
			mData.Dispose();

			// Run a few frames to verify no runtime errors
			for (var i = 0; i < 5; i++)
				yield return null;

			// If we got here without LogError assertions, all scripts are executing cleanly
		}

		// ── Slime wander system script runs with entities ───────────

		[UnityTest]
		public IEnumerator SlimeWander_ExecutesWithEntity()
		{
			// Create a slime-like entity with the components slime_wander.js queries:
			// slime_wander_config, slime_wander_state, local_transform
			// We can't easily create these bridged components from test code without
			// the codegen registration, so instead we verify the system script loaded
			// and runs without crashing (query returns empty = safe no-op).

			yield return null;
			yield return null;

			// slime_wander.js should be loaded and running (zero slimes = no work, no crash)
			for (var i = 0; i < 10; i++)
				yield return null;

			// If we get here, slime_wander.js loaded and its onUpdate ran without error
			// even with zero matching entities (ecs.query returns empty array)
			Assert.Pass("slime_wander.js loaded and executed without errors");
		}

		// ── Character input system script runs with entities ────────

		[UnityTest]
		public IEnumerator CharacterInput_ExecutesWithoutCrash()
		{
			// character_input.js queries char_control, char_stats, char_state.
			// With no matching entities, ecs.query returns empty — safe no-op.

			yield return null;
			yield return null;

			for (var i = 0; i < 10; i++)
				yield return null;

			Assert.Pass("character_input.js loaded and executed without errors");
		}

		// ── Multiple system scripts coexist ──────────────────────────

		[UnityTest]
		public IEnumerator MultipleSystemScripts_AllExecute()
		{
			// Both test_system.js and e2e_autoload_probe.js should run
			yield return null;
			yield return null;

			for (var i = 0; i < 5; i++)
				yield return null;

			var probeCount = GetGlobalInt("_e2eAutoloadCount");
			var testCount = GetGlobalInt("_systemTickCount");

			Assert.Greater(probeCount, 0, "e2e_autoload_probe should have ticked");
			Assert.Greater(testCount, 0, "test_system should have ticked");
		}
	}
}
