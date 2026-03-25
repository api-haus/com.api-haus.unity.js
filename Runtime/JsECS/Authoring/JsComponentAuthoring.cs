namespace UnityJS.Entities.Authoring
{
  using System.Collections.Generic;
  using Unity.Entities;
  using UnityEngine;

  [AddComponentMenu("JS/Js Component Authoring")]
  [RequireComponent(typeof(JsScriptBufferAuthoring))]
  public class JsComponentAuthoring : MonoBehaviour
  {
    [Tooltip("Component name (e.g. slime_wander, slime_spatial)")]
    public string componentName;

    [Tooltip(
      "How this entity uses transforms. Set to Dynamic if the script modifies LocalTransform."
    )]
    public TransformUsageFlags transformUsageType = TransformUsageFlags.Dynamic;

    public List<JsSerializedProperty> propertyOverrides = new();

    public bool HasValidComponent => !string.IsNullOrEmpty(componentName);

    void Reset()
    {
      EnsureBufferAuthoring();
    }

    void OnValidate()
    {
      EnsureBufferAuthoring();
    }

    void EnsureBufferAuthoring()
    {
      if (TryGetComponent<JsScriptBufferAuthoring>(out _))
        return;

      gameObject.AddComponent<JsScriptBufferAuthoring>();
    }
  }
}
