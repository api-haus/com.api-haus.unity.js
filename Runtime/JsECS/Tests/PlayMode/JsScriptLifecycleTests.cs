namespace UnityJS.Entities.PlayModeTests
{
	using System.Collections;
	using System.Runtime.InteropServices;
	using Core;
	using Components;
	using UnityJS.QJS;
	using UnityJS.Runtime;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;

	public class JsScriptLifecycleTests
	{
		JsRuntimeManager m_Vm;
		World m_World;
		EntityManager m_EntityManager;


		static readonly string s_testsPath = System.IO.Path.Combine(
			Application.streamingAssetsPath, "unity.js", "tests");

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;

			m_Vm = JsRuntimeManager.GetOrCreate();
			m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
			JsECSBridge.Initialize(m_World);

			JsScriptSearchPaths.AddSearchPath(s_testsPath, priority: 0);

			if (!JsEntityRegistry.IsCreated)
				JsEntityRegistry.Initialize(16);
			else
				JsEntityRegistry.Clear();

			// Clear globals
			ClearGlobal("_testInitCalled");
			ClearGlobal("_testTickCount");
			ClearGlobal("_testDestroyCalled");

			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			JsEntityRegistry.Clear();
			JsScriptSearchPaths.RemoveSearchPath(s_testsPath);
			var query = m_EntityManager.CreateEntityQuery(typeof(JsEntityId));
			m_EntityManager.DestroyEntity(query);

			// Clean up JsScript cleanup entities too
			var cleanupQuery = m_EntityManager.CreateEntityQuery(typeof(JsScript));
			m_EntityManager.DestroyEntity(cleanupQuery);

			yield return null;
		}

		[UnityTest]
		public IEnumerator ScriptLoad_OnInit_Called()
		{
			// Load the test script
			var scriptId = "test_lifecycle";
			var scriptPath = JsScriptPathUtility.GetScriptFilePath(scriptId);
			Assert.IsTrue(System.IO.File.Exists(scriptPath), $"Test script not found at {scriptPath}");

			var source = System.IO.File.ReadAllText(scriptPath);
			Assert.IsTrue(m_Vm.LoadScriptAsModule(scriptId, source, scriptPath));

			// Create entity state and call OnInit
			var entityId = JsEntityRegistry.AllocateId();
			var stateRef = m_Vm.CreateEntityState(scriptId, entityId);
			Assert.Greater(stateRef, 0);

			m_Vm.CallInit(scriptId, stateRef);

			// Verify OnInit set the global
			var initCalled = GetGlobalBool("_testInitCalled");
			Assert.IsTrue(initCalled, "OnInit should have set _testInitCalled to true");

			var tickCount = GetGlobalInt("_testTickCount");
			Assert.AreEqual(0, tickCount, "OnInit should have reset _testTickCount to 0");

			m_Vm.ReleaseEntityState(stateRef);
			yield return null;
		}

		[UnityTest]
		public IEnumerator OnTick_Called_EachFrame()
		{
			var scriptId = "test_lifecycle";
			var scriptPath = JsScriptPathUtility.GetScriptFilePath(scriptId);
			var source = System.IO.File.ReadAllText(scriptPath);
			m_Vm.LoadScriptAsModule(scriptId, source, scriptPath);

			var entityId = JsEntityRegistry.AllocateId();
			var stateRef = m_Vm.CreateEntityState(scriptId, entityId);
			m_Vm.CallInit(scriptId, stateRef);

			const int frameCount = 5;
			for (var i = 0; i < frameCount; i++)
			{
				m_Vm.CallTick(scriptId, stateRef, 0.016f);
			}

			var tickCount = GetGlobalInt("_testTickCount");
			Assert.AreEqual(frameCount, tickCount, $"OnTick should have been called {frameCount} times");

			m_Vm.ReleaseEntityState(stateRef);
			yield return null;
		}

		[UnityTest]
		public IEnumerator EntityDestroy_OnDestroy_Called()
		{
			var scriptId = "test_lifecycle";
			var scriptPath = JsScriptPathUtility.GetScriptFilePath(scriptId);
			var source = System.IO.File.ReadAllText(scriptPath);
			m_Vm.LoadScriptAsModule(scriptId, source, scriptPath);

			var entityId = JsEntityRegistry.AllocateId();
			var stateRef = m_Vm.CreateEntityState(scriptId, entityId);
			m_Vm.CallInit(scriptId, stateRef);

			// Verify not yet destroyed
			var destroyedBefore = GetGlobalBool("_testDestroyCalled");
			Assert.IsFalse(destroyedBefore, "OnDestroy should not have been called yet");

			// Call OnDestroy
			m_Vm.CallFunction(scriptId, "onDestroy", stateRef);

			var destroyedAfter = GetGlobalBool("_testDestroyCalled");
			Assert.IsTrue(destroyedAfter, "OnDestroy should have set _testDestroyCalled to true");

			m_Vm.ReleaseEntityState(stateRef);
			yield return null;
		}

		#region Helpers

		unsafe void ClearGlobal(string name)
		{
			var code = $"globalThis.{name} = undefined;";
			var sourceBytes = System.Text.Encoding.UTF8.GetBytes(code + '\0');
			var sourceLen = sourceBytes.Length - 1;
			var filenameBytes = System.Text.Encoding.UTF8.GetBytes("<clear>\0");

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);
				QJS.JS_FreeValue(m_Vm.Context, val);
			}
		}

		unsafe bool GetGlobalBool(string name)
		{
			var code = $"globalThis.{name}";
			var sourceBytes = System.Text.Encoding.UTF8.GetBytes(code + '\0');
			var sourceLen = sourceBytes.Length - 1;
			var filenameBytes = System.Text.Encoding.UTF8.GetBytes("<eval>\0");

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Vm.Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);

				var result = QJS.JS_ToBool(m_Vm.Context, val) != 0;
				QJS.JS_FreeValue(m_Vm.Context, val);
				return result;
			}
		}

		unsafe int GetGlobalInt(string name)
		{
			var code = $"globalThis.{name} || 0";
			var sourceBytes = System.Text.Encoding.UTF8.GetBytes(code + '\0');
			var sourceLen = sourceBytes.Length - 1;
			var filenameBytes = System.Text.Encoding.UTF8.GetBytes("<eval>\0");

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

		#endregion
	}
}
