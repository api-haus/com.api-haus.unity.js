namespace UnityJS.QJS
{
  using System.Runtime.InteropServices;

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  public unsafe delegate void QJSShimCallback(
    JSContext ctx,
    long thisU,
    long thisTag,
    int argc,
    JSValue* argv,
    long* outU,
    long* outTag
  );

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  public unsafe delegate int QJSNormalizeCallback(
    JSContext ctx,
    byte* baseName,
    byte* name,
    byte* outBuf,
    int outBufLen
  );

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  public unsafe delegate int QJSReadFileCallback(byte* name, byte* outBuf, int outBufLen);

  public static class QJSShim
  {
    const string Lib = "qjs_shim";
    const CallingConvention CC = CallingConvention.Cdecl;

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue qjs_shim_new_function(
      JSContext ctx,
      QJSShimCallback callback,
      byte* name,
      int length
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern void qjs_shim_reset();

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe void qjs_shim_set_module_loader(
      JSContext ctx,
      QJSNormalizeCallback normalize,
      QJSReadFileCallback readFile
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue qjs_shim_eval_module(
      JSContext ctx,
      byte* source,
      int sourceLen,
      byte* filename
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue qjs_shim_new_float32array(
      JSContext ctx,
      float* data,
      int count
    );

    [DllImport(Lib, CallingConvention = CC)]
    public static extern unsafe JSValue qjs_shim_new_int32array(
      JSContext ctx,
      int* data,
      int count
    );
  }
}
