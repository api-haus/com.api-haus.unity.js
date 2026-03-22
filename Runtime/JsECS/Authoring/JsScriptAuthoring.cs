namespace UnityJS.Entities.Authoring
{
  using System.Collections.Generic;
  using Unity.Entities;
  using UnityEngine;

  [AddComponentMenu("JS/Js Script Authoring")]
  [RequireComponent(typeof(JsScriptBufferAuthoring))]
  public class JsScriptAuthoring : MonoBehaviour
  {
    [Tooltip("Path relative to project root (e.g. Assets/StreamingAssets/unity.js/components/slime_wander.ts)")]
    public string scriptPath;

    [Tooltip(
      "How this entity uses transforms. Set to Dynamic if the script modifies LocalTransform."
    )]
    public TransformUsageFlags transformUsageType = TransformUsageFlags.Dynamic;

    public List<JsSerializedProperty> propertyOverrides = new();

    public bool HasValidScript => !string.IsNullOrEmpty(scriptPath);

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
