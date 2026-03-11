namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for transform operations.
	/// JS API: transform.get_position(), transform.set_position(), transform.get_rotation(), transform.move_toward()
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_GetPosition(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_SetPosition(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_GetRotation(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_MoveToward(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterTransformFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pGetPosBytes = Encoding.UTF8.GetBytes("get_position\0");
			fixed (byte* pGetPos = pGetPosBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Transform_GetPosition, pGetPos, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pGetPos, fn);
			}
			var pSetPosBytes = Encoding.UTF8.GetBytes("set_position\0");
			fixed (byte* pSetPos = pSetPosBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Transform_SetPosition, pSetPos, 4);
				QJS.JS_SetPropertyStr(ctx, ns, pSetPos, fn);
			}
			var pGetRotBytes = Encoding.UTF8.GetBytes("get_rotation\0");
			fixed (byte* pGetRot = pGetRotBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Transform_GetRotation, pGetRot, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pGetRot, fn);
			}
			var pMoveTowardBytes = Encoding.UTF8.GetBytes("move_toward\0");
			fixed (byte* pMoveToward = pMoveTowardBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Transform_MoveToward, pMoveToward, 3);
				QJS.JS_SetPropertyStr(ctx, ns, pMoveToward, fn);
			}

			var global = QJS.JS_GetGlobalObject(ctx);
			var pNameBytes = Encoding.UTF8.GetBytes("transform\0");
			fixed (byte* pName = pNameBytes)
				QJS.JS_SetPropertyStr(ctx, global, pName, ns);
			QJS.JS_FreeValue(ctx, global);
		}
	}
}
