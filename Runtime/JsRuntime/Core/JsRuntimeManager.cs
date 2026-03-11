namespace UnityJS.Runtime
{
	using System;
	using System.Collections.Generic;
	using System.Runtime.InteropServices;
	using System.Text;
	using UnityEngine;
	using UnityJS.QJS;

	/// <summary>
	/// Core runtime host. Owns JSRuntime + JSContext lifecycle.
	/// </summary>
	public class JsRuntimeManager : IDisposable
	{
		static JsRuntimeManager s_Instance;
		public static JsRuntimeManager Instance => s_Instance;

		JSRuntime m_Runtime;
		JSContext m_Context;
		readonly Dictionary<string, JSValue> m_ScriptRegistry = new();

		public JSRuntime Runtime => m_Runtime;
		public JSContext Context => m_Context;
		public bool IsValid => !m_Context.IsNull;

		public JsRuntimeManager(string basePath = null)
		{
			m_Runtime = QJS.JS_NewRuntime();
			m_Context = QJS.JS_NewContext(m_Runtime);

			if (!string.IsNullOrEmpty(basePath))
				JsScriptSearchPaths.AddSearchPath(basePath, 0);

			JsModuleLoader.Install(m_Context);

			s_Instance = this;
		}

		public static JsRuntimeManager GetOrCreate(string basePath = null)
		{
			if (s_Instance != null && s_Instance.IsValid)
				return s_Instance;

			return new JsRuntimeManager(basePath);
		}

		public bool LoadScript(string scriptName)
		{
			var result = JsScriptLoader.FromSearchPaths(scriptName);
			if (!result.isValid)
			{
				Debug.LogError($"[JsRuntime] Failed to find script '{scriptName}': {result.error}");
				return false;
			}

			return LoadScript(result);
		}

		public bool LoadScript(JsScriptLoadResult result)
		{
			if (!result.isValid)
				return false;

			if (!JsScriptLoader.TryReadSource(in result, out var source))
			{
				Debug.LogError($"[JsRuntime] Failed to read source for '{result.scriptId}'");
				return false;
			}

			var scriptId = result.scriptId.ToString();
			var filePath = result.filePath.ToString();
			return EvalAndStore(scriptId, source, filePath);
		}

		public unsafe bool LoadScriptFromString(string scriptId, string source)
		{
			return EvalAndStore(scriptId, source, scriptId);
		}

		public void RegisterBridgeNow(Action<JSContext> registration)
		{
			if (!IsValid)
			{
				Debug.LogError("[JsRuntime] Cannot register bridge — context is not valid");
				return;
			}

			registration(m_Context);
		}

		public void Dispose()
		{
			foreach (var kv in m_ScriptRegistry)
				QJS.JS_FreeValue(m_Context, kv.Value);
			m_ScriptRegistry.Clear();

			if (!m_Context.IsNull)
			{
				QJS.JS_FreeContext(m_Context);
				m_Context = default;
			}

			if (!m_Runtime.IsNull)
			{
				QJS.JS_FreeRuntime(m_Runtime);
				m_Runtime = default;
			}

			if (s_Instance == this)
				s_Instance = null;
		}

		unsafe bool EvalAndStore(string scriptId, string source, string filename)
		{
			var sourceBytes = Encoding.UTF8.GetBytes(source + '\0');
			var sourceLen = sourceBytes.Length - 1; // exclude null terminator from length
			var filenameBytes = Encoding.UTF8.GetBytes(filename + '\0');

			fixed (byte* pSource = sourceBytes, pFilename = filenameBytes)
			{
				var val = QJS.JS_Eval(m_Context, pSource, sourceLen, pFilename,
					QJS.JS_EVAL_TYPE_MODULE);

				if (QJS.IsException(val))
				{
					var exc = QJS.JS_GetException(m_Context);
					var ptr = QJS.JS_ToCString(m_Context, exc);
					var msg = Marshal.PtrToStringUTF8((nint)ptr);
					QJS.JS_FreeCString(m_Context, ptr);
					QJS.JS_FreeValue(m_Context, exc);
					Debug.LogError($"[JsRuntime] Error evaluating '{scriptId}': {msg}");
					return false;
				}

				// Store the module value (may be undefined for modules with no default export)
				if (m_ScriptRegistry.TryGetValue(scriptId, out var old))
					QJS.JS_FreeValue(m_Context, old);
				m_ScriptRegistry[scriptId] = val;
				return true;
			}
		}
	}
}
