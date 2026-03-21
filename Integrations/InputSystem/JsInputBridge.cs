namespace UnityJS.Integration.InputSystem
{
  using System.Collections.Generic;
  using AOT;
  using UnityJS.QJS;
  using UnityJS.Runtime;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.InputSystem;
  using static UnityJS.Runtime.QJSHelpers;

  /// <summary>
  /// Bridge functions for input operations.
  /// JS API: input.readValue(), input.wasPressed(), input.isHeld(), input.wasReleased()
  /// </summary>
  public static class JsInputBridge
  {
    static Dictionary<string, InputAction> s_inputActions;
    static bool s_inputInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
      s_inputActions = null;
      s_inputInitialized = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void AutoRegister() =>
      UnityJS.Entities.Core.JsFunctionRegistry.Register("input", RegisterInputFunctions);

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

      var actionName = ArgString(ctx, argv, index);
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

    static unsafe void RegisterInputFunctions(JSContext ctx, JSValue ns)
    {
      AddFunction(ctx, ns, "readValue", Input_ReadValue, 1);
      AddFunction(ctx, ns, "wasPressed", Input_WasPressed, 1);
      AddFunction(ctx, ns, "isHeld", Input_IsHeld, 1);
      AddFunction(ctx, ns, "wasReleased", Input_WasReleased, 1);
    }
  }
}
