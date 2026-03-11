namespace UnityJS.Entities.Core
{
	using System;

	/// <summary>
	/// Mark a static method to auto-generate a JS bridge wrapper, registration, and type stub.
	/// The method must be static, non-generic, in a partial class.
	/// When Signature is set, the method is stub-only: no wrapper/registration generated,
	/// just metadata emitted for type stub generation.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
	public sealed class JsCompileAttribute : Attribute
	{
		public string Table { get; }
		public string Function { get; }
		public string Signature { get; set; }

		public JsCompileAttribute(string table, string function)
		{
			Table = table;
			Function = function;
		}
	}
}
