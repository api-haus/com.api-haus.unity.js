namespace UnityJS.Entities.Authoring
{
  using Components;
  using UnityEngine;
#if ODIN_INSPECTOR
  using Sirenix.OdinInspector;
#endif

  [RequireComponent(typeof(JsScriptBufferAuthoring))]
  public class JsScriptAuthoring : MonoBehaviour
  {
    [Tooltip("File name without .js extension, relative to Assets/StreamingAssets/unity.js")]
    public JsScriptAssetReference script;

#if ODIN_INSPECTOR
    [FilePath(Extensions = "ts", ParentFolder = "Assets/StreamingAssets/unity.js")]
#endif
    [Tooltip("Path relative to Assets/StreamingAssets/unity.js (e.g. components/slime_wander.ts)")]
    public string scriptPath;

    public bool HasValidScript =>
      script.IsValid || !string.IsNullOrEmpty(scriptPath);

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
