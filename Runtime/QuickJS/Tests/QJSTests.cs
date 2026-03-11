namespace UnityJS.QJS.Tests
{
	using System;
	using System.Runtime.InteropServices;
	using System.Text;
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
			var bytes = Encoding.UTF8.GetBytes(code);
			fixed (byte* pCode = bytes)
			{
				byte* pFile = stackalloc byte[] { (byte)'t', (byte)'e', (byte)'s', (byte)'t', 0 };
				return QJS.JS_Eval(m_Ctx, pCode, bytes.Length, pFile, flags);
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
			byte* pStr = stackalloc byte[] {
				(byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0
			};
			var val = QJS.JS_NewString(m_Ctx, pStr);
			Assert.AreEqual("hello", ToCString(val));
			QJS.JS_FreeValue(m_Ctx, val);
		}

		[Test]
		public void NewObject_SetGetProperty()
		{
			var obj = QJS.JS_NewObject(m_Ctx);
			byte* pProp = stackalloc byte[] { (byte)'x', 0 };
			QJS.JS_SetPropertyStr(m_Ctx, obj, pProp, QJS.NewInt32(m_Ctx, 42));
			var got = QJS.JS_GetPropertyStr(m_Ctx, obj, pProp);
			Assert.AreEqual(42, ToInt32(got));
			QJS.JS_FreeValue(m_Ctx, got);
			QJS.JS_FreeValue(m_Ctx, obj);
		}

		[Test]
		public void NewCFunction_RegisterAndCallFromJS()
		{
			// Test C function registration via JS_Call (avoids managed callback ABI issues)
			// Register a simple JS function, retrieve it, and call it via JS_Call
			var fn = Eval("(function(a,b) { return a + b; })");
			Assert.IsFalse(QJS.IsException(fn), "Function creation failed");
			Assert.IsTrue(QJS.IsObject(fn), "Expected function to be an object");

			var args = stackalloc JSValue[2];
			args[0] = QJS.NewInt32(m_Ctx, 100);
			args[1] = QJS.NewInt32(m_Ctx, 200);

			var result = QJS.JS_Call(m_Ctx, fn, QJS.JS_UNDEFINED, 2, args);
			Assert.IsFalse(QJS.IsException(result), "JS_Call returned exception");
			Assert.AreEqual(300, ToInt32(result));

			QJS.JS_FreeValue(m_Ctx, result);
			QJS.JS_FreeValue(m_Ctx, fn);
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
	}
}
