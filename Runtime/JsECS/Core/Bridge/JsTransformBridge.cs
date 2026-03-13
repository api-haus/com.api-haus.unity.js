namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;
	using UnityJS.Runtime;
	using Unity.Mathematics;

	/// <summary>
	/// Bridge functions for transform operations.
	/// JS API: transform.getPosition(), transform.setPosition(), transform.getRotation(), transform.moveToward()
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_GetPosition(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			int entityId;
			QJS.JS_ToInt32(ctx, &entityId, argv[0]);
			var entity = GetEntityFromIdBurst(entityId);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				var n = QJS.JS_NULL;
				*outU = n.u; *outTag = n.tag;
				return;
			}

			var result = JsStateExtensions.Float3ToJsObject(ctx, transform.Position);
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_SetPosition(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			int entityId;
			QJS.JS_ToInt32(ctx, &entityId, argv[0]);
			var entity = GetEntityFromIdBurst(entityId);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				SetUndefined(outU, outTag);
				return;
			}

			double dx, dy, dz;
			QJS.JS_ToFloat64(ctx, &dx, argv[1]);
			QJS.JS_ToFloat64(ctx, &dy, argv[2]);
			QJS.JS_ToFloat64(ctx, &dz, argv[3]);

			transform.Position = new float3((float)dx, (float)dy, (float)dz);
			TrySetTransformBurst(entity, transform);

			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_GetRotation(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			int entityId;
			QJS.JS_ToInt32(ctx, &entityId, argv[0]);
			var entity = GetEntityFromIdBurst(entityId);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				var n = QJS.JS_NULL;
				*outU = n.u; *outTag = n.tag;
				return;
			}

			var euler = JsStateExtensions.QuaternionToEuler(transform.Rotation);
			var result = JsStateExtensions.Float3ToJsObject(ctx, euler);
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Transform_MoveToward(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			int entityId;
			QJS.JS_ToInt32(ctx, &entityId, argv[0]);
			var entity = GetEntityFromIdBurst(entityId);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				SetUndefined(outU, outTag);
				return;
			}

			float3 targetPos;
			if (QJS.IsObject(argv[1]))
			{
				targetPos = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			}
			else if (QJS.IsNumber(argv[1]))
			{
				int targetId;
				QJS.JS_ToInt32(ctx, &targetId, argv[1]);
				var targetEntity = GetEntityFromIdBurst(targetId);

				if (!TryGetTransformBurst(targetEntity, out var targetTransform))
				{
					SetUndefined(outU, outTag);
					return;
				}

				targetPos = targetTransform.Position;
			}
			else
			{
				SetUndefined(outU, outTag);
				return;
			}

			double dSpeed;
			QJS.JS_ToFloat64(ctx, &dSpeed, argv[2]);
			var speed = (float)dSpeed;

			ref var bctx = ref s_burstContext.Data;
			if (!bctx.isValid)
			{
				SetUndefined(outU, outTag);
				return;
			}

			var direction = targetPos - transform.Position;
			var distance = math.length(direction);

			if (distance > 0.01f)
			{
				var normalizedDir = direction / distance;
				var moveDistance = math.min(speed * bctx.deltaTime, distance);
				transform.Position += normalizedDir * moveDistance;

				var targetRot = quaternion.LookRotationSafe(normalizedDir, math.up());
				transform.Rotation = math.slerp(transform.Rotation, targetRot, bctx.deltaTime * 10f);

				TrySetTransformBurst(entity, transform);
			}

			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterTransformFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pGetPosBytes = Encoding.UTF8.GetBytes("getPosition\0");
			fixed (byte* pGetPos = pGetPosBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Transform_GetPosition, pGetPos, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pGetPos, fn);
			}
			var pSetPosBytes = Encoding.UTF8.GetBytes("setPosition\0");
			fixed (byte* pSetPos = pSetPosBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Transform_SetPosition, pSetPos, 4);
				QJS.JS_SetPropertyStr(ctx, ns, pSetPos, fn);
			}
			var pGetRotBytes = Encoding.UTF8.GetBytes("getRotation\0");
			fixed (byte* pGetRot = pGetRotBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Transform_GetRotation, pGetRot, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pGetRot, fn);
			}
			var pMoveTowardBytes = Encoding.UTF8.GetBytes("moveToward\0");
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
