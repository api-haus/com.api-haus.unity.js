namespace UnityJS.Entities.Authoring
{
  using System.Collections.Generic;
  using System.Text;
  using Components;
  using Core;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
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
      readonly StringBuilder m_JsonBuilder = new();

      public override void Bake(JsScriptBufferAuthoring authoring)
      {
        authoring.GetComponents(m_Scripts);

        var hasValidScript = false;
        var transformFlags = TransformUsageFlags.None;

        foreach (var script in m_Scripts)
        {
          DependsOn(script);

          if (script == null || !script.HasValidScript)
            continue;

          hasValidScript = true;
          transformFlags |= script.transformUsageType;
        }

        if (!hasValidScript)
        {
          m_Scripts.Clear();
          return;
        }

        var entity = GetEntity(transformFlags);
        var requestsBuffer = AddBuffer<JsScriptRequest>(entity);

        foreach (var scriptAuthor in m_Scripts)
        {
          if (scriptAuthor == null || string.IsNullOrEmpty(scriptAuthor.scriptPath))
            continue;

          var scriptId = JsScriptPathUtility.NormalizeScriptId(scriptAuthor.scriptPath);
          if (string.IsNullOrEmpty(scriptId))
            continue;

          var hash = JsScriptPathUtility.HashScriptName(scriptId);

          var request = new JsScriptRequest
          {
            scriptName = new FixedString64Bytes(scriptId),
            requestHash = hash,
            fulfilled = false,
          };

          if (scriptAuthor.propertyOverrides is { Count: > 0 })
          {
            var json = SerializeOverrides(scriptAuthor.propertyOverrides);
            if (json.Length > FixedString512Bytes.UTF8MaxLengthInBytes)
            {
              Log.Warning(
                "[JsScriptBaker] Property overrides JSON ({0} bytes) exceeds 512 byte limit for '{1}', truncating",
                json.Length,
                scriptId
              );
              json = json.Substring(0, FixedString512Bytes.UTF8MaxLengthInBytes);
            }

            request.propertiesJson = new FixedString512Bytes(json);
          }

          requestsBuffer.Add(request);
        }

        AddBuffer<JsEvent>(entity);

        m_Scripts.Clear();
      }

      string SerializeOverrides(List<JsSerializedProperty> overrides)
      {
        m_JsonBuilder.Clear();
        m_JsonBuilder.Append('{');

        for (var i = 0; i < overrides.Count; i++)
        {
          if (i > 0)
            m_JsonBuilder.Append(',');

          var prop = overrides[i];
          m_JsonBuilder.Append('"');
          m_JsonBuilder.Append(prop.key);
          m_JsonBuilder.Append('"');
          m_JsonBuilder.Append(':');

          switch (prop.type)
          {
            case JsPropertyType.Float:
              m_JsonBuilder.Append(prop.floatValue.ToString("G"));
              break;
            case JsPropertyType.Bool:
              m_JsonBuilder.Append(prop.boolValue ? "true" : "false");
              break;
            case JsPropertyType.String:
              m_JsonBuilder.Append('"');
              m_JsonBuilder.Append(prop.stringValue ?? "");
              m_JsonBuilder.Append('"');
              break;
            case JsPropertyType.Vector2:
              m_JsonBuilder.Append("{\"x\":");
              m_JsonBuilder.Append(prop.vector2Value.x.ToString("G"));
              m_JsonBuilder.Append(",\"y\":");
              m_JsonBuilder.Append(prop.vector2Value.y.ToString("G"));
              m_JsonBuilder.Append('}');
              break;
            case JsPropertyType.Vector3:
              m_JsonBuilder.Append("{\"x\":");
              m_JsonBuilder.Append(prop.vector3Value.x.ToString("G"));
              m_JsonBuilder.Append(",\"y\":");
              m_JsonBuilder.Append(prop.vector3Value.y.ToString("G"));
              m_JsonBuilder.Append(",\"z\":");
              m_JsonBuilder.Append(prop.vector3Value.z.ToString("G"));
              m_JsonBuilder.Append('}');
              break;
          }
        }

        m_JsonBuilder.Append('}');
        return m_JsonBuilder.ToString();
      }
    }
  }
}
