namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for cross-entity event operations.
	/// JS API: events.send_attack()
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Events_SendAttack(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterEventsFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pSendAttackBytes = Encoding.UTF8.GetBytes("send_attack\0");
			fixed (byte* pSendAttack = pSendAttackBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Events_SendAttack, pSendAttack, 3);
				QJS.JS_SetPropertyStr(ctx, ns, pSendAttack, fn);
			}

			var global = QJS.JS_GetGlobalObject(ctx);
			var pNameBytes = Encoding.UTF8.GetBytes("events\0");
			fixed (byte* pName = pNameBytes)
				QJS.JS_SetPropertyStr(ctx, global, pName, ns);
			QJS.JS_FreeValue(ctx, global);
		}
	}
}
