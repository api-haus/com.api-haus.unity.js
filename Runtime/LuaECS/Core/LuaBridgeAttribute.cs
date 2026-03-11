namespace LuaECS.Core
{
    using System;

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class LuaBridgeAttribute : Attribute
	{
		public string LuaName { get; }
		public Type ComponentType { get; }
		public bool NeedAccessors { get; set; } = true;
		public bool NeedSetters { get; set; } = true;

		public LuaBridgeAttribute(string luaName = null)
		{
			LuaName = luaName;
		}

		public LuaBridgeAttribute(Type componentType, string luaName = null)
		{
			ComponentType = componentType;
			LuaName = luaName;
		}
	}
}
