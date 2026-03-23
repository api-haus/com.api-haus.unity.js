namespace UnityJS.Runtime
{
  using QJS;

  /// <summary>
  /// Static helpers for evaluating JS expressions against the active VM.
  /// No test framework dependencies — safe for runtime use.
  /// Returns (value, error) tuples; caller decides how to handle errors.
  /// </summary>
  public static unsafe class JsEvalUtility
  {
    public static (int value, string error) EvalInt(string expr)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null) return (0, "VM not initialized");
      var ctx = vm.Context;
      var val = QJS.EvalGlobal(ctx, expr, "eval");
      if (QJS.IsException(val))
        return (0, QJS.GetExceptionMessage(ctx));
      int result;
      QJS.JS_ToInt32(ctx, &result, val);
      QJS.JS_FreeValue(ctx, val);
      return (result, null);
    }

    public static (double value, string error) EvalDouble(string expr)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null) return (0, "VM not initialized");
      var ctx = vm.Context;
      var val = QJS.EvalGlobal(ctx, expr, "eval");
      if (QJS.IsException(val))
        return (0, QJS.GetExceptionMessage(ctx));
      double result;
      QJS.JS_ToFloat64(ctx, &result, val);
      QJS.JS_FreeValue(ctx, val);
      return (result, null);
    }

    public static (bool value, string error) EvalBool(string expr)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null) return (false, "VM not initialized");
      var ctx = vm.Context;
      var val = QJS.EvalGlobal(ctx, expr, "eval");
      if (QJS.IsException(val))
        return (false, QJS.GetExceptionMessage(ctx));
      var result = QJS.JS_ToBool(ctx, val);
      QJS.JS_FreeValue(ctx, val);
      return (result != 0, null);
    }

    public static string EvalVoid(string expr)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null) return "VM not initialized";
      var ctx = vm.Context;
      var val = QJS.EvalGlobal(ctx, expr, "eval");
      if (QJS.IsException(val))
        return QJS.GetExceptionMessage(ctx);
      QJS.JS_FreeValue(ctx, val);
      return null;
    }
  }
}
