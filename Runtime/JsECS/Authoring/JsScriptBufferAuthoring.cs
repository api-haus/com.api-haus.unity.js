namespace UnityJS.Entities.Authoring
{
  using System.Collections.Generic;
  using Components;
  using Core;
  using Unity.Collections;
  using Unity.Entities;
  using UnityEngine;

  [DisallowMultipleComponent]
  [ExecuteAlways]
  public sealed class JsScriptBufferAuthoring : MonoBehaviour
  {
    void OnEnable()
    {
      hideFlags |= HideFlags.NotEditable;
    }

    void OnValidate()
    {
      hideFlags |= HideFlags.NotEditable;
    }

    public class Baker : Baker<JsScriptBufferAuthoring>
    {
      readonly List<JsScriptAuthoring> m_Scripts = new();

      public override void Bake(JsScriptBufferAuthoring authoring)
      {
        authoring.GetComponents(m_Scripts);

        var hasValidScript = false;

        foreach (var script in m_Scripts)
        {
          DependsOn(script);

          if (!hasValidScript && script != null && script.HasValidScript)
            hasValidScript = true;
        }

        if (!hasValidScript)
        {
          m_Scripts.Clear();
          return;
        }

        var entity = GetEntity(TransformUsageFlags.None);
        var requestsBuffer = AddBuffer<JsScriptRequest>(entity);

        foreach (var scriptAuthor in m_Scripts)
        {
          if (scriptAuthor == null)
            continue;

          string scriptId = null;

          // Prefer scriptPath if set, fall back to legacy JsScriptAssetReference
          if (!string.IsNullOrEmpty(scriptAuthor.scriptPath))
            scriptId = JsScriptPathUtility.NormalizeScriptId(scriptAuthor.scriptPath);
          else if (scriptAuthor.script.IsValid)
            scriptId = JsScriptPathUtility.NormalizeScriptId(scriptAuthor.script.ToString());

          if (string.IsNullOrEmpty(scriptId))
            continue;

          var hash = JsScriptPathUtility.HashScriptName(scriptId);

          requestsBuffer.Add(
            new JsScriptRequest
            {
              scriptName = new FixedString64Bytes(scriptId),
              requestHash = hash,
              fulfilled = false,
            }
          );
        }

        AddBuffer<JsEvent>(entity);

        m_Scripts.Clear();
      }
    }
  }
}
