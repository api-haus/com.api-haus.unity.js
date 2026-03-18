#if UNITY_EDITOR
namespace UnityJS.Editor
{
  using System.Collections.Generic;
  using System.IO;
  using Entities.Authoring;
  using Entities.Core;
  using UnityEditor;
  using UnityEngine;

  [CustomEditor(typeof(JsScriptAuthoring))]
  public class JsScriptAuthoringEditor : UnityEditor.Editor
  {
    SerializedProperty m_ScriptPath;
    SerializedProperty m_TransformUsageType;
    SerializedProperty m_PropertyOverrides;
    List<JsParsedProperty> m_ParsedProperties;
    string m_LastParsedPath;
    string m_LastParsedContent;

    void OnEnable()
    {
      m_ScriptPath = serializedObject.FindProperty("scriptPath");
      m_TransformUsageType = serializedObject.FindProperty("transformUsageType");
      m_PropertyOverrides = serializedObject.FindProperty("propertyOverrides");
    }

    public override void OnInspectorGUI()
    {
      serializedObject.Update();

      DrawScriptPathField();
      EditorGUILayout.PropertyField(m_TransformUsageType, new GUIContent("Transform Usage"));

      var authoring = (JsScriptAuthoring)target;
      if (authoring.HasValidScript)
        DrawPropertyOverrides(authoring);

      serializedObject.ApplyModifiedProperties();
    }

    void DrawScriptPathField()
    {
      EditorGUILayout.BeginHorizontal();
      EditorGUILayout.PropertyField(m_ScriptPath, new GUIContent("Script Path"));

      if (GUILayout.Button("Browse", GUILayout.Width(60)))
      {
        var basePath = Path.Combine(Application.streamingAssetsPath, "unity.js");
        var path = EditorUtility.OpenFilePanel("Select TS Script", basePath, "ts");
        if (!string.IsNullOrEmpty(path))
        {
          var relative = GetRelativePath(path, basePath);
          if (relative != null)
          {
            m_ScriptPath.stringValue = relative;
            m_ParsedProperties = null;
          }
        }
      }

      EditorGUILayout.EndHorizontal();

      // Validate
      var scriptPath = m_ScriptPath.stringValue;
      if (!string.IsNullOrEmpty(scriptPath))
      {
        var fullPath = Path.Combine(Application.streamingAssetsPath, "unity.js", scriptPath);
        if (!File.Exists(fullPath))
          EditorGUILayout.HelpBox($"Script not found: {scriptPath}", MessageType.Warning);
      }
    }

    void DrawPropertyOverrides(JsScriptAuthoring authoring)
    {
      var scriptPath = authoring.scriptPath;
      var fullPath = Path.Combine(Application.streamingAssetsPath, "unity.js", scriptPath);
      if (!File.Exists(fullPath))
        return;

      var content = File.ReadAllText(fullPath);
      if (m_ParsedProperties == null || m_LastParsedPath != scriptPath || m_LastParsedContent != content)
      {
        m_ParsedProperties = JsPropertyParser.Parse(content);
        m_LastParsedPath = scriptPath;
        m_LastParsedContent = content;
      }

      if (m_ParsedProperties.Count == 0)
        return;

      EditorGUILayout.Space();
      EditorGUILayout.LabelField("Property Overrides", EditorStyles.boldLabel);

      foreach (var parsed in m_ParsedProperties)
      {
        // Skip internal state properties
        if (IsInternalProperty(parsed.name))
          continue;

        var overrideIndex = FindOverrideIndex(authoring.propertyOverrides, parsed.name);
        var hasOverride = overrideIndex >= 0;

        EditorGUI.BeginChangeCheck();

        var label = new GUIContent(parsed.name, parsed.tooltip);

        switch (parsed.type)
        {
          case JsPropertyType.Float:
          {
            var current = hasOverride
              ? authoring.propertyOverrides[overrideIndex].floatValue
              : parsed.floatValue;
            var newVal = EditorGUILayout.FloatField(label, current);
            if (EditorGUI.EndChangeCheck())
              SetOverride(authoring, parsed.name, JsPropertyType.Float, newVal);
            else
              EditorGUI.EndChangeCheck();
            break;
          }
          case JsPropertyType.Bool:
          {
            var current = hasOverride
              ? authoring.propertyOverrides[overrideIndex].boolValue
              : parsed.boolValue;
            var newVal = EditorGUILayout.Toggle(label, current);
            if (EditorGUI.EndChangeCheck())
              SetOverrideBool(authoring, parsed.name, newVal);
            else
              EditorGUI.EndChangeCheck();
            break;
          }
          case JsPropertyType.String:
          {
            var current = hasOverride
              ? authoring.propertyOverrides[overrideIndex].stringValue
              : parsed.stringValue;
            var newVal = EditorGUILayout.TextField(label, current);
            if (EditorGUI.EndChangeCheck())
              SetOverrideString(authoring, parsed.name, newVal);
            else
              EditorGUI.EndChangeCheck();
            break;
          }
          case JsPropertyType.Vector2:
          {
            var current = hasOverride
              ? authoring.propertyOverrides[overrideIndex].vector2Value
              : parsed.vector2Value;
            var newVal = EditorGUILayout.Vector2Field(label, current);
            if (EditorGUI.EndChangeCheck())
              SetOverrideVector2(authoring, parsed.name, newVal);
            else
              EditorGUI.EndChangeCheck();
            break;
          }
          case JsPropertyType.Vector3:
          {
            var current = hasOverride
              ? authoring.propertyOverrides[overrideIndex].vector3Value
              : parsed.vector3Value;
            var newVal = EditorGUILayout.Vector3Field(label, current);
            if (EditorGUI.EndChangeCheck())
              SetOverrideVector3(authoring, parsed.name, newVal);
            else
              EditorGUI.EndChangeCheck();
            break;
          }
          default:
            EditorGUI.EndChangeCheck();
            break;
        }
      }

      EditorGUILayout.Space();
      if (GUILayout.Button("Reset to Defaults"))
      {
        Undo.RecordObject(authoring, "Reset JS Property Overrides");
        authoring.propertyOverrides.Clear();
        EditorUtility.SetDirty(authoring);
      }
    }

    void SetOverride(JsScriptAuthoring authoring, string key, JsPropertyType type, float value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty { key = key, type = type, floatValue = value };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideBool(JsScriptAuthoring authoring, string key, bool value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty { key = key, type = JsPropertyType.Bool, boolValue = value };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideString(JsScriptAuthoring authoring, string key, string value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty { key = key, type = JsPropertyType.String, stringValue = value };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideVector2(JsScriptAuthoring authoring, string key, Vector2 value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty { key = key, type = JsPropertyType.Vector2, vector2Value = value };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideVector3(JsScriptAuthoring authoring, string key, Vector3 value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty { key = key, type = JsPropertyType.Vector3, vector3Value = value };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    static int FindOverrideIndex(List<JsSerializedProperty> overrides, string key)
    {
      for (var i = 0; i < overrides.Count; i++)
        if (overrides[i].key == key)
          return i;
      return -1;
    }

    static bool IsInternalProperty(string name)
    {
      // Skip properties that are runtime state, not configurable from inspector
      return name is "pauseTimer" or "paused" or "target" or "origin";
    }

    static string GetRelativePath(string fullPath, string basePath)
    {
      fullPath = fullPath.Replace('\\', '/');
      basePath = basePath.Replace('\\', '/');
      if (!basePath.EndsWith("/"))
        basePath += "/";

      if (fullPath.StartsWith(basePath))
        return fullPath.Substring(basePath.Length);

      return null;
    }
  }
}
#endif
