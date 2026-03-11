namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for entity lifecycle operations.
	/// JS API: entities.create(), entities.destroy(), entities.add_script(), entities.has_script(), entities.remove_component()
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Entities_Create(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Entities_Destroy(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Entities_AddScript(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Entities_HasScript(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Entities_RemoveComponent(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterEntitiesFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pCreateBytes = Encoding.UTF8.GetBytes("create\0");
			fixed (byte* pCreate = pCreateBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Entities_Create, pCreate, 0);
				QJS.JS_SetPropertyStr(ctx, ns, pCreate, fn);
			}
			var pDestroyBytes = Encoding.UTF8.GetBytes("destroy\0");
			fixed (byte* pDestroy = pDestroyBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Entities_Destroy, pDestroy, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pDestroy, fn);
			}
			var pAddScriptBytes = Encoding.UTF8.GetBytes("add_script\0");
			fixed (byte* pAddScript = pAddScriptBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Entities_AddScript, pAddScript, 2);
				QJS.JS_SetPropertyStr(ctx, ns, pAddScript, fn);
			}
			var pHasScriptBytes = Encoding.UTF8.GetBytes("has_script\0");
			fixed (byte* pHasScript = pHasScriptBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Entities_HasScript, pHasScript, 2);
				QJS.JS_SetPropertyStr(ctx, ns, pHasScript, fn);
			}
			var pRemoveComponentBytes = Encoding.UTF8.GetBytes("remove_component\0");
			fixed (byte* pRemoveComponent = pRemoveComponentBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Entities_RemoveComponent, pRemoveComponent, 2);
				QJS.JS_SetPropertyStr(ctx, ns, pRemoveComponent, fn);
			}

			var global = QJS.JS_GetGlobalObject(ctx);
			var pNameBytes = Encoding.UTF8.GetBytes("entities\0");
			fixed (byte* pName = pNameBytes)
				QJS.JS_SetPropertyStr(ctx, global, pName, ns);
			QJS.JS_FreeValue(ctx, global);
		}
	}
}
