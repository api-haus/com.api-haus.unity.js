namespace UnityJS.QJS
{
  using System;
  using System.Runtime.InteropServices;

  /// <summary>
  /// QuickJS tagged value. 128-bit on 64-bit platforms: { JSValueUnion u; int64_t tag; }
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct JSValue : IEquatable<JSValue>
  {
    public long u;
    public long tag;

    public readonly bool Equals(JSValue other)
    {
      return u == other.u && tag == other.tag;
    }

    public override readonly bool Equals(object obj)
    {
      return obj is JSValue v && Equals(v);
    }

    public override readonly int GetHashCode()
    {
      return HashCode.Combine(u, tag);
    }

    public static bool operator ==(JSValue a, JSValue b) => a.Equals(b);

    public static bool operator !=(JSValue a, JSValue b) => !a.Equals(b);
  }

  public struct JSRuntime : IEquatable<JSRuntime>
  {
    public nuint Handle;
    public readonly bool IsNull => Handle == 0;

    public readonly bool Equals(JSRuntime other)
    {
      return Handle == other.Handle;
    }

    public override readonly bool Equals(object obj)
    {
      return obj is JSRuntime r && Equals(r);
    }

    public override readonly int GetHashCode()
    {
      return Handle.GetHashCode();
    }
  }

  public struct JSContext : IEquatable<JSContext>
  {
    public nuint Handle;
    public readonly bool IsNull => Handle == 0;

    public readonly bool Equals(JSContext other)
    {
      return Handle == other.Handle;
    }

    public override readonly bool Equals(object obj)
    {
      return obj is JSContext c && Equals(c);
    }

    public override readonly int GetHashCode()
    {
      return Handle.GetHashCode();
    }
  }

  public unsafe delegate JSValue JSCFunction(
    JSContext ctx,
    JSValue thisVal,
    int argc,
    JSValue* argv
  );

  public unsafe delegate byte* JSModuleNormalizeFunc(
    JSContext ctx,
    byte* base_name,
    byte* name,
    void* opaque
  );

  public unsafe delegate JSValue JSModuleLoaderFunc(JSContext ctx, byte* name, void* opaque);

  public struct JSModuleDef
  {
    public nuint Handle;
  }

  public static class QJS
  {
    const string Lib = "qjs";
    const CallingConvention CC = CallingConvention.Cdecl;

    // ── Tag constants (from quickjs.h) ──
    public const long JS_TAG_INT = 0;
    public const long JS_TAG_BOOL = 1;
    public const long JS_TAG_NULL = 2;
    public const long JS_TAG_UNDEFINED = 3;
    public const long JS_TAG_EXCEPTION = 6;
    public const long JS_TAG_FLOAT64 = 8;
    public const long JS_TAG_OBJECT = -1;
    public const long JS_TAG_STRING = -7;
    public const long JS_TAG_SYMBOL = -8;
    public const long JS_TAG_BIG_INT = -9;

    // ── Eval flags ──
    public const int JS_EVAL_TYPE_GLOBAL = 0;
    public const int JS_EVAL_TYPE_MODULE = 1;
    public const int JS_EVAL_FLAG_COMPILE_ONLY = 1 << 5;

    // ── Inline reimplementations (NOT exported from .so) ──

    public static JSValue JS_MKVAL(long tag, int val)
    {
      return new JSValue { u = val, tag = tag };
    }

    public static JSValue JS_NULL => JS_MKVAL(JS_TAG_NULL, 0);
    public static JSValue JS_UNDEFINED => JS_MKVAL(JS_TAG_UNDEFINED, 0);
    public static JSValue JS_TRUE => JS_MKVAL(JS_TAG_BOOL, 1);
    public static JSValue JS_FALSE => JS_MKVAL(JS_TAG_BOOL, 0);

    public static bool IsException(JSValue v)
    {
      return v.tag == JS_TAG_EXCEPTION;
    }

    public static bool IsNumber(JSValue v)
    {
      return v.tag == JS_TAG_INT || v.tag == JS_TAG_FLOAT64;
    }

    public static bool IsString(JSValue v)
    {
      return v.tag == JS_TAG_STRING;
    }

    public static bool IsObject(JSValue v)
    {
      return v.tag == JS_TAG_OBJECT;
    }

    public static bool IsUndefined(JSValue v)
    {
      return v.tag == JS_TAG_UNDEFINED;
    }

    public static bool IsNull(JSValue v)
    {
      return v.tag == JS_TAG_NULL;
    }

    public static bool IsBool(JSValue v)
    {
      return v.tag == JS_TAG_BOOL;
    }

    public static JSValue NewInt32(JSContext ctx, int val)
    {
      return JS_MKVAL(JS_TAG_INT, val);
    }

    public static JSValue NewBool(JSContext ctx, bool val)
    {
      return JS_MKVAL(JS_TAG_BOOL, val ? 1 : 0);
    }

    public static JSValue JS_NewBool(JSContext ctx, int val)
    {
      return JS_MKVAL(JS_TAG_BOOL, val);
    }

    public static unsafe JSValue NewFloat64(JSContext ctx, double val)
    {
      JSValue v;
      v.tag = JS_TAG_FLOAT64;
      v.u = *(long*)&val;
      return v;
    }

    /// <summary>
    /// Safe JS_Eval wrapper that handles null-termination automatically.
    /// Returns the eval result (caller must free). Returns JS_UNDEFINED on exception
    /// after logging the error via the provided tag.
    /// </summary>
    public static unsafe JSValue EvalGlobal(JSContext ctx, string code, string filename)
    {
      var codeBytes = System.Text.Encoding.UTF8.GetBytes(code + '\0');
      var fileBytes = System.Text.Encoding.UTF8.GetBytes(filename + '\0');
      fixed (byte* pCode = codeBytes, pFile = fileBytes)
      {
        return JS_Eval(ctx, pCode, codeBytes.Length - 1, pFile, JS_EVAL_TYPE_GLOBAL);
      }
    }

    // ── Exported functions (DllImport) ──

    // Runtime lifecycle
    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSRuntime JS_NewRuntime();

    [DllImport(Lib, CallingConvention = CC)]
    public static extern void JS_FreeRuntime(JSRuntime rt);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSContext JS_NewContext(JSRuntime rt);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSRuntime JS_GetRuntime(JSContext ctx);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern void JS_FreeContext(JSContext ctx);

    // Eval
    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_Eval(
      JSContext ctx,
      byte* input,
      nint input_len,
      byte* filename,
      int eval_flags
    );

    // Module loader
    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe void JS_SetModuleLoaderFunc(
      JSRuntime rt,
      nint normalize_func,
      nint loader_func,
      void* opaque
    );

    // Value creation (exported ones only)
    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_NewStringLen(JSContext ctx, byte* str, nint len);

    public static unsafe JSValue JS_NewString(JSContext ctx, byte* str)
    {
      var len = 0;
      if (str != null)
        while (str[len] != 0)
          len++;
      return JS_NewStringLen(ctx, str, len);
    }

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSValue JS_NewObject(JSContext ctx);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSValue JS_NewObjectProto(JSContext ctx, JSValue proto);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSValue JS_NewArray(JSContext ctx);

    public static JSValue JS_NewInt64(JSContext ctx, long val)
    {
      if (val >= int.MinValue && val <= int.MaxValue)
        return NewInt32(ctx, (int)val);
      return NewFloat64(ctx, (double)val);
    }

    // Value extraction
    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe int JS_ToInt32(JSContext ctx, int* pres, JSValue val);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe int JS_ToInt64(JSContext ctx, long* pres, JSValue val);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe int JS_ToFloat64(JSContext ctx, double* pres, JSValue val);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern int JS_ToBool(JSContext ctx, JSValue val);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe byte* JS_ToCStringLen2(
      JSContext ctx,
      nint* plen,
      JSValue val,
      int cesu8
    );

    public static unsafe byte* JS_ToCString(JSContext ctx, JSValue val)
    {
      return JS_ToCStringLen2(ctx, null, val, 0);
    }

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe void JS_FreeCString(JSContext ctx, byte* ptr);

    // Properties
    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_GetPropertyStr(
      JSContext ctx,
      JSValue this_obj,
      byte* prop
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe int JS_SetPropertyStr(
      JSContext ctx,
      JSValue this_obj,
      byte* prop,
      JSValue val
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSValue JS_GetPropertyUint32(JSContext ctx, JSValue this_obj, uint idx);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern int JS_SetPropertyUint32(
      JSContext ctx,
      JSValue this_obj,
      uint idx,
      JSValue val
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSValue JS_GetGlobalObject(JSContext ctx);

    // Functions
    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_NewCFunction2(
      JSContext ctx,
      nint func,
      byte* name,
      int length,
      int cproto,
      int magic
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_Call(
      JSContext ctx,
      JSValue func_obj,
      JSValue this_obj,
      int argc,
      JSValue* argv
    );

    // Lifecycle
    [DllImport(Lib, CallingConvention = CC)]
    public static extern void JS_FreeValue(JSContext ctx, JSValue val);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSValue JS_DupValue(JSContext ctx, JSValue val);

    // Type checks (exported)
    [DllImport(Lib, CallingConvention = CC)]
    public static extern int JS_IsArray(JSContext ctx, JSValue val);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern int JS_IsFunction(JSContext ctx, JSValue val);

    // ArrayBuffer / TypedArray
    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_NewArrayBufferCopy(JSContext ctx, byte* buf, nint len);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe byte* JS_GetArrayBuffer(JSContext ctx, nint* psize, JSValue obj);

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_NewTypedArray(
      JSContext ctx,
      int argc,
      JSValue* argv,
      int array_type
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue JS_GetTypedArrayBuffer(
      JSContext ctx,
      JSValue obj,
      nint* pbyte_offset,
      nint* pbyte_length,
      nint* pbytes_per_element
    );

    // TypedArray enum constants (from JSTypedArrayEnum)
    public const int JS_TYPED_ARRAY_INT32 = 5;
    public const int JS_TYPED_ARRAY_FLOAT32 = 10;

    // Exceptions
    [DllImport(Lib, CallingConvention = CC)]
    public static extern JSValue JS_GetException(JSContext ctx);

    // Convenience
    public static unsafe JSValue JS_NewCFunction(JSContext ctx, nint func, byte* name, int length)
    {
      return JS_NewCFunction2(ctx, func, name, length, 0, 0);
    }
  }
}
