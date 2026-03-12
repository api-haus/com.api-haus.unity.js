namespace UnityJS.Entities.PlayModeTests
{
	using System.Collections;
	using System.Runtime.InteropServices;
	using System.Text;
	using Components;
	using Core;
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
	/// End-to-end tests that exercise the real ECS pipeline:
	/// JsScriptFulfillmentSystem parses annotations → tick systems execute scripts.
	/// Uses actual JS files on disk with // @tick: annotations.
	/// </summary>
	public class JsTickSystemE2ETests
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
			m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
			JsECSBridge.Initialize(m_World);

			if (!JsEntityRegistry.IsCreated)
				JsEntityRegistry.Initialize(64);
			else
				JsEntityRegistry.Clear();

			// Clear all test globals
			EvalGlobal("for (var k in globalThis) { if (k.startsWith('_e2e')) delete globalThis[k]; }");

			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			// Destroy all test entities
			var query = m_EntityManager.CreateEntityQuery(typeof(JsEntityId));
			m_EntityManager.DestroyEntity(query);
			var cleanupQuery = m_EntityManager.CreateEntityQuery(typeof(JsScript));
			m_EntityManager.DestroyEntity(cleanupQuery);

			yield return null;
		}

		#region Helpers

		Entity CreateScriptedEntity(string scriptName)
		{
			var entity = m_EntityManager.CreateEntity();
			m_EntityManager.AddComponentData(entity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1f });
			var entityId = JsEntityRegistry.AllocateId();
			m_EntityManager.AddComponentData(entity, new JsEntityId { value = entityId });
			var requests = m_EntityManager.AddBuffer<JsScriptRequest>(entity);
			requests.Add(new JsScriptRequest
			{
				scriptName = new FixedString64Bytes(scriptName),
				requestHash = JsScriptPathUtility.HashScriptName(scriptName),
				fulfilled = false,
			});
			return entity;
		}

		Entity CreateMultiScriptEntity(params string[] scriptNames)
		{
			var entity = m_EntityManager.CreateEntity();
			m_EntityManager.AddComponentData(entity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1f });
			var entityId = JsEntityRegistry.AllocateId();
			m_EntityManager.AddComponentData(entity, new JsEntityId { value = entityId });
			var requests = m_EntityManager.AddBuffer<JsScriptRequest>(entity);
			foreach (var name in scriptNames)
			{
				requests.Add(new JsScriptRequest
				{
					scriptName = new FixedString64Bytes(name),
					requestHash = JsScriptPathUtility.HashScriptName(name),
					fulfilled = false,
				});
			}
			return entity;
		}

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

		unsafe bool GetGlobalBool(string name)
		{
			var code = $"globalThis.{name}";
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

		unsafe string GetGlobalString(string name)
		{
			var code = $"globalThis.{name} || ''";
			var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
			var sourceLen = sourceBytes.Length - 1;
			var filenameBytes = Encoding.UTF8.GetBytes("<eval>\0");

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);
				var ptr = QJS.JS_ToCString(m_Vm.Context, val);
				var result = Marshal.PtrToStringUTF8((nint)ptr);
				QJS.JS_FreeCString(m_Vm.Context, ptr);
				QJS.JS_FreeValue(m_Vm.Context, val);
				return result;
			}
		}

		#endregion

		#region Fulfillment + Annotation Parsing E2E

		[UnityTest]
		public IEnumerator E2E_VariableScript_FulfilledAndTicked()
		{
			var entity = CreateScriptedEntity("test_tick_variable");

			// Let fulfillment system process + first tick
			yield return null;
			yield return null;
			yield return null;

			Assert.IsTrue(GetGlobalBool("_e2eVarInit"), "OnInit should have been called");
			Assert.Greater(GetGlobalInt("_e2eVarCount"), 0, "OnTick should have been called at least once");

			// Verify the script component has correct tick group
			Assert.IsTrue(m_EntityManager.HasBuffer<JsScript>(entity));
			var scripts = m_EntityManager.GetBuffer<JsScript>(entity);
			Assert.AreEqual(1, scripts.Length);
			Assert.AreEqual(JsTickGroup.Variable, scripts[0].tickGroup);
			Assert.IsFalse(scripts[0].disabled);
			Assert.GreaterOrEqual(scripts[0].stateRef, 0);
		}

		[UnityTest]
		public IEnumerator E2E_FixedScript_FulfilledWithCorrectTickGroup()
		{
			var entity = CreateScriptedEntity("test_tick_fixed");

			// Fulfillment frame
			yield return null;
			// Wait for fixed update to ensure FixedStepSimulationSystemGroup runs
			yield return new WaitForFixedUpdate();
			yield return new WaitForFixedUpdate();
			yield return null;

			Assert.IsTrue(GetGlobalBool("_e2eFixInit"), "OnInit should have been called");

			var scripts = m_EntityManager.GetBuffer<JsScript>(entity);
			Assert.AreEqual(1, scripts.Length);
			Assert.AreEqual(JsTickGroup.Fixed, scripts[0].tickGroup);

			Assert.Greater(GetGlobalInt("_e2eFixCount"), 0, "Fixed OnTick should have been called");
		}

		[UnityTest]
		public IEnumerator E2E_AllFiveTickGroups_CorrectAssignment()
		{
			var varEntity = CreateScriptedEntity("test_tick_variable");
			var fixEntity = CreateScriptedEntity("test_tick_fixed");
			var bpEntity = CreateScriptedEntity("test_tick_before_physics");
			var apEntity = CreateScriptedEntity("test_tick_after_physics");
			var atEntity = CreateScriptedEntity("test_tick_after_transform");

			// Let fulfillment process all 5
			yield return null;
			yield return null;

			void AssertGroup(Entity e, JsTickGroup expected, string label)
			{
				Assert.IsTrue(m_EntityManager.HasBuffer<JsScript>(e), $"{label}: should have JsScript buffer");
				var s = m_EntityManager.GetBuffer<JsScript>(e);
				Assert.AreEqual(1, s.Length, $"{label}: should have 1 script");
				Assert.AreEqual(expected, s[0].tickGroup, $"{label}: wrong tick group");
			}

			AssertGroup(varEntity, JsTickGroup.Variable, "variable");
			AssertGroup(fixEntity, JsTickGroup.Fixed, "fixed");
			AssertGroup(bpEntity, JsTickGroup.BeforePhysics, "before_physics");
			AssertGroup(apEntity, JsTickGroup.AfterPhysics, "after_physics");
			AssertGroup(atEntity, JsTickGroup.AfterTransform, "after_transform");
		}

		#endregion

		#region Tick Execution E2E

		[UnityTest]
		public IEnumerator E2E_VariableScript_TicksEveryFrame()
		{
			CreateScriptedEntity("test_tick_variable");

			// Fulfillment frame
			yield return null;

			var countAfterFulfillment = GetGlobalInt("_e2eVarCount");

			// Run 5 more frames
			for (var i = 0; i < 5; i++)
				yield return null;

			var countAfter5 = GetGlobalInt("_e2eVarCount");
			// Should have gained exactly 5 ticks (one per frame)
			Assert.AreEqual(5, countAfter5 - countAfterFulfillment,
				"Variable script should tick exactly once per frame");
		}

		[UnityTest]
		public IEnumerator E2E_FixedScript_TicksAtFixedRate()
		{
			CreateScriptedEntity("test_tick_fixed");

			// Fulfillment + initial fixed updates
			yield return null;
			yield return new WaitForFixedUpdate();
			yield return new WaitForFixedUpdate();

			var countEarly = GetGlobalInt("_e2eFixCount");

			// Wait real time for more fixed steps
			yield return new WaitForSeconds(0.15f);

			var countLate = GetGlobalInt("_e2eFixCount");
			Assert.Greater(countLate, countEarly, "Fixed script should accumulate ticks over time");
		}

		[UnityTest]
		public IEnumerator E2E_FixedScript_DoesNotRunInVariableGroup()
		{
			CreateScriptedEntity("test_tick_fixed");
			CreateScriptedEntity("test_tick_variable");

			// Fulfillment
			yield return null;

			// Both should have inited
			Assert.IsTrue(GetGlobalBool("_e2eFixInit"));
			Assert.IsTrue(GetGlobalBool("_e2eVarInit"));

			// Wait enough time for fixed steps to accumulate
			yield return new WaitForSeconds(0.15f);

			var fixCount = GetGlobalInt("_e2eFixCount");
			var varCount = GetGlobalInt("_e2eVarCount");

			Assert.Greater(varCount, 0, "Variable should tick");
			Assert.Greater(fixCount, 0, "Fixed should tick");
			// In batchmode, variable ticks once per frame, fixed ticks at 50Hz —
			// they should differ unless frames happen to align perfectly
			Assert.AreNotEqual(fixCount, varCount,
				"Fixed and variable scripts should tick at different rates");
		}

		[UnityTest]
		public IEnumerator E2E_AllPhysicsGroups_AllTick()
		{
			CreateScriptedEntity("test_tick_before_physics");
			CreateScriptedEntity("test_tick_fixed");
			CreateScriptedEntity("test_tick_after_physics");

			// Fulfillment
			yield return null;
			// Wait for fixed updates to fire
			yield return new WaitForSeconds(0.15f);

			Assert.IsTrue(GetGlobalBool("_e2eBpInit"), "BeforePhysics OnInit");
			Assert.IsTrue(GetGlobalBool("_e2eFixInit"), "Fixed OnInit");
			Assert.IsTrue(GetGlobalBool("_e2eApInit"), "AfterPhysics OnInit");

			Assert.Greater(GetGlobalInt("_e2eBpCount"), 0, "BeforePhysics should tick");
			Assert.Greater(GetGlobalInt("_e2eFixCount"), 0, "Fixed should tick");
			Assert.Greater(GetGlobalInt("_e2eApCount"), 0, "AfterPhysics should tick");
		}

		[UnityTest]
		public IEnumerator E2E_AfterTransform_Ticks()
		{
			CreateScriptedEntity("test_tick_after_transform");

			yield return null;
			yield return null;
			for (var i = 0; i < 5; i++)
				yield return null;

			Assert.IsTrue(GetGlobalBool("_e2eAtInit"), "AfterTransform OnInit");
			Assert.Greater(GetGlobalInt("_e2eAtCount"), 0, "AfterTransform should tick");
		}

		#endregion

		#region Multi-Script Entity E2E

		[UnityTest]
		public IEnumerator E2E_MultiScriptEntity_BothScriptsRun()
		{
			CreateMultiScriptEntity("test_multi_a", "test_multi_b");

			yield return null;
			yield return null;
			for (var i = 0; i < 5; i++)
				yield return null;

			var countA = GetGlobalInt("_e2eMultiA");
			var countB = GetGlobalInt("_e2eMultiB");
			Assert.Greater(countA, 0, "Script A should tick");
			Assert.Greater(countB, 0, "Script B should tick");
			// Both variable, same entity, should have same tick count
			Assert.AreEqual(countA, countB, "Both scripts on same entity should tick equally");
		}

		[UnityTest]
		public IEnumerator E2E_MixedTickGroupsOnEntity_BothRun()
		{
			CreateMultiScriptEntity("test_tick_variable", "test_tick_fixed");

			// Fulfillment
			yield return null;
			// Wait for fixed steps
			yield return new WaitForSeconds(0.15f);

			Assert.IsTrue(GetGlobalBool("_e2eVarInit"));
			Assert.IsTrue(GetGlobalBool("_e2eFixInit"));
			Assert.Greater(GetGlobalInt("_e2eVarCount"), 0, "Variable script should tick");
			Assert.Greater(GetGlobalInt("_e2eFixCount"), 0, "Fixed script should tick");
		}

		#endregion

		#region Ordering E2E

		[UnityTest]
		public IEnumerator E2E_VariableAndFixed_BothAppearInLog()
		{
			CreateScriptedEntity("test_tick_order");
			CreateScriptedEntity("test_tick_order_fixed");

			// Fulfillment
			yield return null;
			// Wait for fixed steps to fire
			yield return new WaitForSeconds(0.15f);

			var log = GetGlobalString("_e2eOrderLog");
			Assert.IsNotEmpty(log, "Order log should have entries");
			Assert.IsTrue(log.Contains("V"), "Log should contain variable ticks 'V'");
			Assert.IsTrue(log.Contains("F"), "Log should contain fixed ticks 'F'");
		}

		#endregion

		#region Multiple Entities E2E

		[UnityTest]
		public IEnumerator E2E_MultipleEntities_SameScript_IndependentCounts()
		{
			// Two entities both using test_multi_a — each gets its own state,
			// but they both increment the same global (that's expected, it's a shared VM)
			CreateScriptedEntity("test_multi_a");
			CreateScriptedEntity("test_multi_a");

			yield return null;
			yield return null;
			for (var i = 0; i < 3; i++)
				yield return null;

			// Both entities share the same global, so count should be ~2x frames
			var count = GetGlobalInt("_e2eMultiA");
			// After ~5 frames, each entity ticks once per frame = ~10 total
			Assert.Greater(count, 5, "Two entities with same script should both tick");
		}

		#endregion
	}
}
