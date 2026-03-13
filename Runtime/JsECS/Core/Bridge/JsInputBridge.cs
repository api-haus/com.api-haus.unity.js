namespace UnityJS.Entities.Core
{
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.InputSystem;
  using UnityJS.QJS;
  using UnityJS.Runtime;

  /// <summary>
  /// Bridge functions for input operations.
  /// JS API: input.readValue(), input.wasPressed(), input.isHeld(), input.wasReleased()
  /// </summary>
  static partial class JsECSBridge
  {
    static Dictionary<string, InputAction> s_inputActions;
    static bool s_inputInitialized;

    public static void InitializeInputSystem(InputActionAsset inputActionAsset)
    {
      if (s_inputActions == null)
        s_inputActions = new Dictionary<string, InputAction>();

      s_inputActions.Clear();

      if (inputActionAsset == null)
      {
        s_inputInitialized = false;
        return;
      }

      foreach (var action in inputActionAsset)
      {
        s_inputActions[action.name] = action;
        action.Enable();
      }

      s_inputInitialized = true;
    }

    static unsafe InputAction GetInputAction(JSContext ctx, JSValue* argv, int index)
    {
      if (!s_inputInitialized || s_inputActions == null)
        return null;

      var ptr = QJS.JS_ToCString(ctx, argv[index]);
      if (ptr == null)
        return null;

      var actionName = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(ctx, ptr);

      if (string.IsNullOrEmpty(actionName))
        return null;
      s_inputActions.TryGetValue(actionName, out var action);
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
        var n = QJS.JS_NULL;
        *outU = n.u;
        *outTag = n.tag;
        return;
      }

      if (action.expectedControlType == "Vector2")
      {
        var value = action.ReadValue<Vector2>();
        var result = JsStateExtensions.Float3ToJsObject(ctx, new float3(value.x, value.y, 0));
        *outU = result.u;
        *outTag = result.tag;
        return;
      }

      if (action.expectedControlType is "Axis" or "")
      {
        var value = action.ReadValue<float>();
        var result = QJS.NewFloat64(ctx, value);
        *outU = result.u;
        *outTag = result.tag;
        return;
      }

      var isPressed = action.IsPressed();
      var bResult = QJS.NewBool(ctx, isPressed);
      *outU = bResult.u;
      *outTag = bResult.tag;
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
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var result = QJS.NewBool(ctx, action.WasPressedThisFrame());
      *outU = result.u;
      *outTag = result.tag;
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
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var result = QJS.NewBool(ctx, action.IsPressed());
      *outU = result.u;
      *outTag = result.tag;
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
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var result = QJS.NewBool(ctx, action.WasReleasedThisFrame());
      *outU = result.u;
      *outTag = result.tag;
    }

    static unsafe void RegisterInputFunctions(JSContext ctx)
    {
      var ns = QJS.JS_NewObject(ctx);

      var pReadValueBytes = Encoding.UTF8.GetBytes("readValue\0");
      fixed (byte* pReadValue = pReadValueBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Input_ReadValue, pReadValue, 1);
        QJS.JS_SetPropertyStr(ctx, ns, pReadValue, fn);
      }
      var pWasPressedBytes = Encoding.UTF8.GetBytes("wasPressed\0");
      fixed (byte* pWasPressed = pWasPressedBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Input_WasPressed, pWasPressed, 1);
        QJS.JS_SetPropertyStr(ctx, ns, pWasPressed, fn);
      }
      var pIsHeldBytes = Encoding.UTF8.GetBytes("isHeld\0");
      fixed (byte* pIsHeld = pIsHeldBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Input_IsHeld, pIsHeld, 1);
        QJS.JS_SetPropertyStr(ctx, ns, pIsHeld, fn);
      }
      var pWasReleasedBytes = Encoding.UTF8.GetBytes("wasReleased\0");
      fixed (byte* pWasReleased = pWasReleasedBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Input_WasReleased, pWasReleased, 1);
        QJS.JS_SetPropertyStr(ctx, ns, pWasReleased, fn);
      }

      var global = QJS.JS_GetGlobalObject(ctx);
      var pNameBytes = Encoding.UTF8.GetBytes("input\0");
      fixed (byte* pName = pNameBytes)
        QJS.JS_SetPropertyStr(ctx, global, pName, ns);
      QJS.JS_FreeValue(ctx, global);
    }
  }
}
