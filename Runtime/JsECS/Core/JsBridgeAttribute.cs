namespace UnityJS.Entities.Core
{
  using System;

  [AttributeUsage(
    AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Assembly,
    AllowMultiple = true
  )]
  public sealed class JsBridgeAttribute : Attribute
  {
    public string JsName { get; }
    public Type ComponentType { get; }
    public JsBridgeAttribute(string jsName = null) => JsName = jsName;

    public JsBridgeAttribute(Type componentType, string jsName = null)
    {
      ComponentType = componentType;
      JsName = jsName;
    }
  }
}
