namespace UnityJS.Entities.PlayModeTests
{
  using System.Runtime.InteropServices;
  using System.Text;
  using Core;
  using NUnit.Framework;
  using QJS;
  using Runtime;

  /// <summary>
  /// Test helper: creates a JsRuntimeManager and registers bridge functions.
  /// Provides EvalGlobal for running JS code in tests.
  /// </summary>
  public unsafe class JsBridgeTestFixture
  {
    protected JsRuntimeManager m_Manager;
    protected JSContext Ctx => m_Manager.Context;

    [SetUp]
    public virtual void SetUp()
    {
      QJSShim.qjs_shim_reset();
      m_Manager = new JsRuntimeManager();
      m_Manager.BridgeState ??= new JsBridgeState();

      // Register all ECS bridge functions
      JsECSBridge.RegisterFunctions(Ctx);
    }

    [TearDown]
    public virtual void TearDown()
    {
      m_Manager?.Dispose();
      m_Manager = null;
    }

    /// <summary>
    /// Evaluates JS code as global scope and returns the result.
    /// Asserts that no JS exception was thrown.
    /// Caller owns the returned JSValue (must free).
    /// </summary>
    protected JSValue EvalGlobal(string code)
    {
      var sourceBytes = Encoding.UTF8.GetBytes(code + '\0');
      var sourceLen = sourceBytes.Length - 1; // exclude null terminator from length
      var fileBytes = Encoding.UTF8.GetBytes("<test>\0");
      fixed (
        byte* pSrc = sourceBytes,
          pFile = fileBytes
      )
      {
        var result = QJS.JS_Eval(Ctx, pSrc, sourceLen, pFile, QJS.JS_EVAL_TYPE_GLOBAL);
        if (QJS.IsException(result))
        {
          var exc = QJS.JS_GetException(Ctx);
          var eptr = QJS.JS_ToCString(Ctx, exc);
          var emsg = Marshal.PtrToStringUTF8((nint)eptr) ?? "unknown error";
          QJS.JS_FreeCString(Ctx, eptr);
          QJS.JS_FreeValue(Ctx, exc);
          Assert.Fail($"JS exception: {emsg}");
        }

        return result;
      }
    }

    /// <summary>
    /// Evaluates JS code, asserts no exception, and frees the result.
    /// </summary>
    protected void EvalGlobalVoid(string code)
    {
      var result = EvalGlobal(code);
      QJS.JS_FreeValue(Ctx, result);
    }

    /// <summary>
    /// Evaluates JS code, asserts no exception, and returns int result.
    /// </summary>
    protected int EvalGlobalInt(string code)
    {
      var result = EvalGlobal(code);
      int val;
      QJS.JS_ToInt32(Ctx, &val, result);
      QJS.JS_FreeValue(Ctx, result);
      return val;
    }

    /// <summary>
    /// Evaluates JS code, asserts no exception, and returns double result.
    /// </summary>
    protected double EvalGlobalFloat(string code)
    {
      var result = EvalGlobal(code);
      double val;
      QJS.JS_ToFloat64(Ctx, &val, result);
      QJS.JS_FreeValue(Ctx, result);
      return val;
    }

    /// <summary>
    /// Evaluates JS code, asserts no exception, and returns bool result.
    /// </summary>
    protected bool EvalGlobalBool(string code)
    {
      var result = EvalGlobal(code);
      var val = QJS.JS_ToBool(Ctx, result);
      QJS.JS_FreeValue(Ctx, result);
      return val != 0;
    }
  }
}
