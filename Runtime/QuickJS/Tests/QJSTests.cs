namespace UnityJS.QJS.Tests
{
  using System;
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using NUnit.Framework;

  [TestFixture]
  public unsafe class QJSTests
  {
    JSRuntime m_Rt;
    JSContext m_Ctx;

    [SetUp]
    public void SetUp()
    {
      m_Rt = QJS.JS_NewRuntime();
      m_Ctx = QJS.JS_NewContext(m_Rt);
      QJSShim.qjs_shim_reset();
    }

    [TearDown]
    public void TearDown()
    {
      if (!m_Ctx.IsNull)
        QJS.JS_FreeContext(m_Ctx);
      if (!m_Rt.IsNull)
        QJS.JS_FreeRuntime(m_Rt);
    }

    JSValue Eval(string code, int flags = QJS.JS_EVAL_TYPE_GLOBAL)
    {
      var bytes = Encoding.UTF8.GetBytes(code + '\0');
      var len = bytes.Length - 1;
      fixed (byte* pCode = bytes)
      {
        var pFile = stackalloc byte[] { (byte)'t', (byte)'e', (byte)'s', (byte)'t', 0 };
        return QJS.JS_Eval(m_Ctx, pCode, len, pFile, flags);
      }
    }

    int ToInt32(JSValue val)
    {
      int result;
      QJS.JS_ToInt32(m_Ctx, &result, val);
      return result;
    }

    double ToFloat64(JSValue val)
    {
      double result;
      QJS.JS_ToFloat64(m_Ctx, &result, val);
      return result;
    }

    string ToCString(JSValue val)
    {
      var ptr = QJS.JS_ToCString(m_Ctx, val);
      var str = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(m_Ctx, ptr);
      return str;
    }

    void SetGlobalFunction(string name, QJSShimCallback cb, int length)
    {
      var nameBytes = Encoding.UTF8.GetBytes(name + '\0');
      fixed (byte* pName = nameBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(m_Ctx, cb, pName, length);
        var global = QJS.JS_GetGlobalObject(m_Ctx);
        QJS.JS_SetPropertyStr(m_Ctx, global, pName, fn);
        QJS.JS_FreeValue(m_Ctx, global);
      }
    }

    // ── Callback stubs ──

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReturns777(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var v = QJS.NewInt32(ctx, 777);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackAdd(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      int a,
        b;
      QJS.JS_ToInt32(ctx, &a, argv[0]);
      QJS.JS_ToInt32(ctx, &b, argv[1]);
      var v = QJS.NewInt32(ctx, a + b);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReturnsHello(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var pStr = stackalloc byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0 };
      var v = QJS.JS_NewString(ctx, pStr);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackThrows(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var v = new JSValue { u = 0, tag = QJS.JS_TAG_EXCEPTION };
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReturnsA(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var v = QJS.NewInt32(ctx, 1);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReturnsB(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var v = QJS.NewInt32(ctx, 2);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReturnsC(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var v = QJS.NewInt32(ctx, 3);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReadsThis(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      // this_val should be an object (tag == JS_TAG_OBJECT)
      var v = QJS.NewInt32(ctx, (int)thisTag);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReturnsPi(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var v = QJS.NewFloat64(ctx, 3.14);
      *outU = v.u;
      *outTag = v.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReadsObjectProperty(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var pX = stackalloc byte[] { (byte)'x', 0 };
      var prop = QJS.JS_GetPropertyStr(ctx, argv[0], pX);
      *outU = prop.u;
      *outTag = prop.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackReadsArrayLength(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var pLen = stackalloc byte[] { (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'t', (byte)'h', 0 };
      var prop = QJS.JS_GetPropertyStr(ctx, argv[0], pLen);
      *outU = prop.u;
      *outTag = prop.tag;
    }

    /// <summary>
    /// Reproduces the exact chain from JsQueryBridge.Query:
    /// 1. argv[0] is an object {all: ['X']}
    /// 2. JS_GetPropertyStr(ctx, argv[0], "all") → allArr
    /// 3. JS_GetPropertyStr(ctx, allArr, "length") → lenVal (chained call on returned JSValue)
    /// 4. JS_GetPropertyUint32(ctx, allArr, 0) → elem
    /// Returns: [allArr.tag, len, elem.tag] encoded as allArr.tag * 10000 + len * 100 + elem.tag_abs
    /// </summary>
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackChainedPropertyAccess(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      // Step 1: read "all" from argv[0]
      var pAll = stackalloc byte[] { (byte)'a', (byte)'l', (byte)'l', 0 };
      var allArr = QJS.JS_GetPropertyStr(ctx, argv[0], pAll);

      // Save to statics IMMEDIATELY
      s_diagAllU = allArr.u;
      s_diagAllTag = allArr.tag;

      // Step 2: read "length" from allArr
      var pLen = stackalloc byte[] { (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'t', (byte)'h', 0 };
      var lenVal = QJS.JS_GetPropertyStr(ctx, allArr, pLen);
      int len;
      QJS.JS_ToInt32(ctx, &len, lenVal);
      QJS.JS_FreeValue(ctx, lenVal);
      s_diagLen = len;

      // Step 3: read element 0 from allArr
      var elem = QJS.JS_GetPropertyUint32(ctx, allArr, 0);
      s_diagElemIsStr = QJS.IsString(elem) ? 1 : 0;

      QJS.JS_FreeValue(ctx, elem);
      QJS.JS_FreeValue(ctx, allArr);

      var v = QJS.NewInt32(ctx, 0);
      *outU = v.u;
      *outTag = v.tag;
    }

    /// <summary>
    /// Same as above but passes allArr through a helper method (tests method-call passing).
    /// </summary>
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static void CallbackChainedViaHelper(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var pAll = stackalloc byte[] { (byte)'a', (byte)'l', (byte)'l', 0 };
      var allArr = QJS.JS_GetPropertyStr(ctx, argv[0], pAll);

      int len = ReadArrayLength(ctx, allArr);

      QJS.JS_FreeValue(ctx, allArr);

      var v = QJS.NewInt32(ctx, len);
      *outU = v.u;
      *outTag = v.tag;
    }

    static unsafe int ReadArrayLength(JSContext ctx, JSValue arr)
    {
      var pLen = stackalloc byte[] { (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'t', (byte)'h', 0 };
      var lenVal = QJS.JS_GetPropertyStr(ctx, arr, pLen);
      int len;
      QJS.JS_ToInt32(ctx, &len, lenVal);
      QJS.JS_FreeValue(ctx, lenVal);
      return len;
    }

    // ── Original tests ──

    [Test]
    public void NewRuntime_NewContext_Roundtrip()
    {
      Assert.IsFalse(m_Rt.IsNull);
      Assert.IsFalse(m_Ctx.IsNull);
    }

    [Test]
    public void Eval_Arithmetic_ReturnsInt()
    {
      var result = Eval("2+3");
      Assert.AreEqual(5, ToInt32(result));
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Eval_FloatArithmetic_ReturnsFloat()
    {
      var result = Eval("1.5+2.5");
      Assert.AreEqual(4.0, ToFloat64(result), 0.0001);
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void NewInt32_ToInt32_Roundtrip()
    {
      var val = QJS.NewInt32(m_Ctx, 42);
      Assert.AreEqual(42, ToInt32(val));
    }

    [Test]
    public void NewFloat64_ToFloat64_Roundtrip()
    {
      var val = QJS.NewFloat64(m_Ctx, 3.14);
      Assert.AreEqual(3.14, ToFloat64(val), 0.0001);
    }

    [Test]
    public void NewBool_ToBool_Roundtrip()
    {
      var val = QJS.NewBool(m_Ctx, true);
      Assert.AreEqual(1, QJS.JS_ToBool(m_Ctx, val));
    }

    [Test]
    public void NewString_ToCString_Roundtrip()
    {
      var pStr = stackalloc byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0 };
      var val = QJS.JS_NewString(m_Ctx, pStr);
      Assert.AreEqual("hello", ToCString(val));
      QJS.JS_FreeValue(m_Ctx, val);
    }

    [Test]
    public void NewObject_SetGetProperty()
    {
      var obj = QJS.JS_NewObject(m_Ctx);
      var pProp = stackalloc byte[] { (byte)'x', 0 };
      QJS.JS_SetPropertyStr(m_Ctx, obj, pProp, QJS.NewInt32(m_Ctx, 42));
      var got = QJS.JS_GetPropertyStr(m_Ctx, obj, pProp);
      Assert.AreEqual(42, ToInt32(got));
      QJS.JS_FreeValue(m_Ctx, got);
      QJS.JS_FreeValue(m_Ctx, obj);
    }

    [Test]
    public void Eval_SyntaxError_IsException()
    {
      var result = Eval("function(");
      Assert.IsTrue(QJS.IsException(result));
      var exc = QJS.JS_GetException(m_Ctx);
      QJS.JS_FreeValue(m_Ctx, exc);
    }

    [Test]
    public void Eval_Module_ExportsFunction()
    {
      var result = Eval("(function() { function add(a,b) { return a+b; } return add(2,3); })()");
      Assert.IsFalse(QJS.IsException(result), "Module eval returned exception");
      Assert.AreEqual(5, ToInt32(result));
      QJS.JS_FreeValue(m_Ctx, result);
    }

    // ── Shim callback tests ──

    [Test]
    public void Callback_ReturnsInt()
    {
      SetGlobalFunction("myFn", CallbackReturns777, 0);
      var result = Eval("myFn()");
      Assert.IsFalse(QJS.IsException(result), "myFn() returned exception");
      Assert.AreEqual(777, ToInt32(result));
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Callback_ReadsArgs()
    {
      SetGlobalFunction("add", CallbackAdd, 2);
      var result = Eval("add(10, 20)");
      Assert.IsFalse(QJS.IsException(result), "add() returned exception");
      Assert.AreEqual(30, ToInt32(result));
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Callback_ReturnsString()
    {
      SetGlobalFunction("greet", CallbackReturnsHello, 0);
      var result = Eval("greet()");
      Assert.IsFalse(QJS.IsException(result), "greet() returned exception");
      Assert.AreEqual("hello", ToCString(result));
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Callback_ThrowsException()
    {
      SetGlobalFunction("thrower", CallbackThrows, 0);
      var result = Eval("thrower()");
      Assert.IsTrue(QJS.IsException(result));
      var exc = QJS.JS_GetException(m_Ctx);
      QJS.JS_FreeValue(m_Ctx, exc);
    }

    [Test]
    public void Callback_MultipleRegistrations()
    {
      SetGlobalFunction("fnA", CallbackReturnsA, 0);
      SetGlobalFunction("fnB", CallbackReturnsB, 0);
      SetGlobalFunction("fnC", CallbackReturnsC, 0);

      var rA = Eval("fnA()");
      var rB = Eval("fnB()");
      var rC = Eval("fnC()");

      Assert.AreEqual(1, ToInt32(rA));
      Assert.AreEqual(2, ToInt32(rB));
      Assert.AreEqual(3, ToInt32(rC));

      QJS.JS_FreeValue(m_Ctx, rA);
      QJS.JS_FreeValue(m_Ctx, rB);
      QJS.JS_FreeValue(m_Ctx, rC);
    }

    [Test]
    public void Callback_UsesThisVal()
    {
      SetGlobalFunction("getThisTag", CallbackReadsThis, 0);
      // Bind on an object and call as method — this_val should be the object
      var result = Eval("var obj = {}; obj.fn = getThisTag; obj.fn()");
      Assert.IsFalse(QJS.IsException(result), "obj.fn() returned exception");
      // JS_TAG_OBJECT == -1
      Assert.AreEqual((int)QJS.JS_TAG_OBJECT, ToInt32(result));
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Callback_ReturnsFloat()
    {
      SetGlobalFunction("getPi", CallbackReturnsPi, 0);
      var result = Eval("getPi()");
      Assert.IsFalse(QJS.IsException(result), "getPi() returned exception");
      Assert.AreEqual(3.14, ToFloat64(result), 0.0001);
      QJS.JS_FreeValue(m_Ctx, result);
    }

    // Diagnostic static fields for fine-grained checking
    static long s_diagAllU, s_diagAllTag;
    static int s_diagLen, s_diagElemIsStr;

    [Test]
    public void Callback_ChainedPropertyAccess_Inline()
    {
      SetGlobalFunction("readChained", CallbackChainedPropertyAccess, 1);
      var result = Eval("readChained({all: ['LocalTransform']})");
      Assert.IsFalse(QJS.IsException(result), "readChained() returned exception");

      // Check static diagnostics captured inside the callback
      Assert.AreEqual(-1, s_diagAllTag, $"allArr.tag: saved immediately after JS_GetPropertyStr");
      Assert.AreEqual(1, s_diagLen, $"length from JS_GetPropertyStr on allArr");
      Assert.AreEqual(1, s_diagElemIsStr, $"elem[0] from JS_GetPropertyUint32 on allArr");
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Callback_ChainedPropertyAccess_ViaHelper()
    {
      // Same chain but passes JSValue through a helper method
      SetGlobalFunction("readHelper", CallbackChainedViaHelper, 1);
      var result = Eval("readHelper({all: ['LocalTransform']})");
      Assert.IsFalse(QJS.IsException(result), "readHelper() returned exception");
      Assert.AreEqual(1, ToInt32(result), "helper should read array length 1");
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Callback_ReadsObjectProperty()
    {
      SetGlobalFunction("readProp", CallbackReadsObjectProperty, 1);
      var result = Eval("readProp({x: 42})");
      Assert.IsFalse(QJS.IsException(result), "readProp() returned exception");
      Assert.AreEqual(42, ToInt32(result), "JS_GetPropertyStr on argv[0] object should return property value");
      QJS.JS_FreeValue(m_Ctx, result);
    }

    [Test]
    public void Callback_ReadsArrayLength()
    {
      SetGlobalFunction("readLen", CallbackReadsArrayLength, 1);
      var result = Eval("readLen([1,2,3])");
      Assert.IsFalse(QJS.IsException(result), "readLen() returned exception");
      Assert.AreEqual(3, ToInt32(result), "JS_GetPropertyStr 'length' on argv[0] array should return 3");
      QJS.JS_FreeValue(m_Ctx, result);
    }
  }
}
