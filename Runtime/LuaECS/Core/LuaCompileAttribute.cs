namespace LuaECS.Core
{
    using System;

    /// <summary>
    /// Mark a static method to auto-generate a Lua bridge wrapper, registration, and type stub.
    /// The method must be static, non-generic, in a partial class.
    /// Supported param types: float, int, bool, float3 (+ out variants).
    /// Supported return types: void, float, int, bool, float3.
    /// When Signature is set, the method is stub-only: no wrapper/registration generated,
    /// just metadata emitted for type stub generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
	public sealed class LuaCompileAttribute : Attribute
	{
		public string Table { get; }
		public string Function { get; }
		public string Signature { get; set; }

		public LuaCompileAttribute(string table, string function)
		{
			Table = table;
			Function = function;
		}
	}
}
