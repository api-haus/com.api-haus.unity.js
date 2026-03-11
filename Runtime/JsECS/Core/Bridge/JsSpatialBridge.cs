namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for spatial queries.
	/// JS API: spatial.distance(), spatial.query_near(), spatial.get_entity_count()
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Spatial_Distance(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Spatial_QueryNear(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Spatial_GetEntityCount(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterSpatialFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pDistanceBytes = Encoding.UTF8.GetBytes("distance\0");
			fixed (byte* pDistance = pDistanceBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Spatial_Distance, pDistance, 2);
				QJS.JS_SetPropertyStr(ctx, ns, pDistance, fn);
			}
			var pQueryNearBytes = Encoding.UTF8.GetBytes("query_near\0");
			fixed (byte* pQueryNear = pQueryNearBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Spatial_QueryNear, pQueryNear, 2);
				QJS.JS_SetPropertyStr(ctx, ns, pQueryNear, fn);
			}
			var pGetEntityCountBytes = Encoding.UTF8.GetBytes("get_entity_count\0");
			fixed (byte* pGetEntityCount = pGetEntityCountBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Spatial_GetEntityCount, pGetEntityCount, 0);
				QJS.JS_SetPropertyStr(ctx, ns, pGetEntityCount, fn);
			}

			var global = QJS.JS_GetGlobalObject(ctx);
			var pNameBytes = Encoding.UTF8.GetBytes("spatial\0");
			fixed (byte* pName = pNameBytes)
				QJS.JS_SetPropertyStr(ctx, global, pName, ns);
			QJS.JS_FreeValue(ctx, global);
		}
	}
}
