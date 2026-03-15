namespace UnityJS.Runtime.Tests
{
  using System;
  using System.IO;
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using NUnit.Framework;
  using QJS;

  [TestFixture]
  public unsafe class JsRuntimeManagerTests
  {
    JsRuntimeManager m_Manager;
    string m_TempDir;

    [SetUp]
    public void SetUp()
    {
      QJSShim.qjs_shim_reset();
      m_TempDir = Path.Combine(
        Path.GetTempPath(),
        "jsruntime_tests_" + Guid.NewGuid().ToString("N")[..8]
      );
      Directory.CreateDirectory(m_TempDir);
      JsScriptSearchPaths.Reset();
      JsScriptSearchPaths.AddSearchPath(m_TempDir, 0);
      m_Manager = new JsRuntimeManager();
    }

    [TearDown]
    public void TearDown()
    {
      m_Manager?.Dispose();
      m_Manager = null;
      if (Directory.Exists(m_TempDir))
        Directory.Delete(m_TempDir, true);
    }

    [Test]
    public void Create_Dispose_Lifecycle()
    {
      Assert.IsTrue(m_Manager.IsValid);
      Assert.IsFalse(m_Manager.Runtime.IsNull);
      Assert.IsFalse(m_Manager.Context.IsNull);

      m_Manager.Dispose();
      Assert.IsFalse(m_Manager.IsValid);
      m_Manager = null;
    }

    [Test]
    public void LoadScriptFromString_EvalSucceeds()
    {
      var ok = m_Manager.LoadScriptFromString("test_mod", "export function foo() { return 42; }");
      Assert.IsTrue(ok);
    }

    [Test]
    public void LoadScript_FromSearchPaths()
    {
      File.WriteAllText(Path.Combine(m_TempDir, "hello.js"), "export const greeting = 'hi';");
      var ok = m_Manager.LoadScript("hello");
      Assert.IsTrue(ok);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void BridgeReturns999(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var v = QJS.NewInt32(ctx, 999);
      *outU = v.u;
      *outTag = v.tag;
    }

    [Test]
    public void RegisterBridge_CallableFromJS()
    {
      m_Manager.RegisterBridgeNow(ctx =>
      {
        var nameBytes = Encoding.UTF8.GetBytes("bridgeFn\0");
        fixed (byte* pName = nameBytes)
        {
          var fn = QJSShim.qjs_shim_new_function(ctx, BridgeReturns999, pName, 0);
          var global = QJS.JS_GetGlobalObject(ctx);
          QJS.JS_SetPropertyStr(ctx, global, pName, fn);
          QJS.JS_FreeValue(ctx, global);
        }
      });

      // Eval as global (not module) so we can get the return value directly
      var sourceBytes = Encoding.UTF8.GetBytes("bridgeFn()\0");
      var sourceLen = sourceBytes.Length - 1;
      var fileBytes = Encoding.UTF8.GetBytes("test\0");
      fixed (
        byte* pSrc = sourceBytes,
          pFile = fileBytes
      )
      {
        var result = QJS.JS_Eval(
          m_Manager.Context,
          pSrc,
          sourceLen,
          pFile,
          QJS.JS_EVAL_TYPE_GLOBAL
        );
        if (QJS.IsException(result))
        {
          var exc = QJS.JS_GetException(m_Manager.Context);
          var eptr = QJS.JS_ToCString(m_Manager.Context, exc);
          var emsg = Marshal.PtrToStringUTF8((nint)eptr);
          QJS.JS_FreeCString(m_Manager.Context, eptr);
          QJS.JS_FreeValue(m_Manager.Context, exc);
          Assert.Fail($"bridgeFn() returned exception: {emsg}");
        }

        int intResult;
        QJS.JS_ToInt32(m_Manager.Context, &intResult, result);
        Assert.AreEqual(999, intResult);
        QJS.JS_FreeValue(m_Manager.Context, result);
      }
    }

    [Test]
    public void ModuleImport_ResolvesRelative()
    {
      File.WriteAllText(
        Path.Combine(m_TempDir, "helper.js"),
        "export function add(a, b) { return a + b; }"
      );
      File.WriteAllText(
        Path.Combine(m_TempDir, "main.js"),
        "import { add } from './helper.js';\nexport const result = add(2, 3);"
      );

      var ok = m_Manager.LoadScript("main");
      Assert.IsTrue(ok);
    }
  }
}
