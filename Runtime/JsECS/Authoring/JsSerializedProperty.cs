namespace UnityJS.Entities.Authoring
{
  using System;
  using UnityEngine;

  public enum JsPropertyType
  {
    Float,
    Bool,
    String,
    Vector2,
    Vector3,
  }

  [Serializable]
  public struct JsSerializedProperty
  {
    public string key;
    public JsPropertyType type;
    public float floatValue;
    public bool boolValue;
    public string stringValue;
    public Vector2 vector2Value;
    public Vector3 vector3Value;
  }
}
