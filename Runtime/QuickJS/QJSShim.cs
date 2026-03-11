namespace UnityJS.QJS
{
	using System.Runtime.InteropServices;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public unsafe delegate void QJSShimCallback(
		JSContext ctx,
		long thisU, long thisTag,
		int argc, JSValue* argv,
		long* outU, long* outTag);

	public static class QJSShim
	{
		const string Lib = "qjs_shim";
		const CallingConvention CC = CallingConvention.Cdecl;

		[DllImport(Lib, CallingConvention = CC)]
		public static extern unsafe JSValue qjs_shim_new_function(
			JSContext ctx, QJSShimCallback callback, byte* name, int length);

		[DllImport(Lib, CallingConvention = CC)]
		public static extern void qjs_shim_reset();
	}
}
