namespace UnityJS.Entities.Core
{
  using System.Collections.Generic;
  using AOT;
  using QJS;
  using Runtime;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.InputSystem;
  using static Runtime.QJSHelpers;

  /// <summary>
  /// Bridge functions for input operations.
  /// JS API: input.readValue(), input.wasPressed(), input.isHeld(), input.wasReleased()
  /// </summary>
  static partial class JsECSBridge
  {
    public static void InitializeInputSystem(InputActionAsset inputActionAsset)
    {
      var b = B;
      if (b == null) return;

      if (b.InputActions == null)
        b.InputActions = new Dictionary<string, InputAction>();

      b.InputActions.Clear();

      if (inputActionAsset == null)
      {
        b.InputInitialized = false;
        return;
      }

      foreach (var action in inputActionAsset)
      {
        b.InputActions[action.name] = action;
        action.Enable();
      }

      b.InputInitialized = true;
    }

    static unsafe InputAction GetInputAction(JSContext ctx, JSValue* argv, int index)
    {
      var b = B;
      if (b == null || !b.InputInitialized || b.InputActions == null)
        return null;

      var actionName = ArgString(ctx, argv, index);
      if (string.IsNullOrEmpty(actionName))
        return null;
      b.InputActions.TryGetValue(actionName, out var action);
      return action;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Input_ReadValue(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var action = GetInputAction(ctx, argv, 0);
      if (action == null)
      {
        SetNull(outU, outTag);
        return;
      }

      if (action.expectedControlType == "Vector2")
      {
        var value = action.ReadValue<Vector2>();
        SetResult(outU, outTag, JsStateExtensions.Float3ToJsObject(ctx, new float3(value.x, value.y, 0)));
        return;
      }

      if (action.expectedControlType is "Axis" or "")
      {
        SetFloat(outU, outTag, ctx, action.ReadValue<float>());
        return;
      }

      SetBool(outU, outTag, ctx, action.IsPressed());
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Input_WasPressed(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var action = GetInputAction(ctx, argv, 0);
      if (action == null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      SetBool(outU, outTag, ctx, action.WasPressedThisFrame());
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Input_IsHeld(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var action = GetInputAction(ctx, argv, 0);
      if (action == null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      SetBool(outU, outTag, ctx, action.IsPressed());
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Input_WasReleased(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var action = GetInputAction(ctx, argv, 0);
      if (action == null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      SetBool(outU, outTag, ctx, action.WasReleasedThisFrame());
    }

    static unsafe void RegisterInputFunctions(JSContext ctx)
    {
      var ns = QJS.JS_NewObject(ctx);

      AddFunction(ctx, ns, "readValue", Input_ReadValue, 1);
      AddFunction(ctx, ns, "wasPressed", Input_WasPressed, 1);
      AddFunction(ctx, ns, "isHeld", Input_IsHeld, 1);
      AddFunction(ctx, ns, "wasReleased", Input_WasReleased, 1);

      var global = QJS.JS_GetGlobalObject(ctx);
      SetNamespace(ctx, global, "input", ns);
      QJS.JS_FreeValue(ctx, global);
    }
  }
}
