namespace UnityJS.Editor
{
  using System.Collections.Generic;
  using System.IO;
  using Entities.Authoring;
  using Entities.Core;
  using UnityEditor;
  using UnityEngine;

  [CustomEditor(typeof(JsComponentAuthoring))]
  public class JsComponentAuthoringEditor : UnityEditor.Editor
  {
    SerializedProperty m_ComponentName;
    SerializedProperty m_TransformUsageType;
    SerializedProperty m_PropertyOverrides;
    List<JsParsedProperty> m_ParsedProperties;
    string m_LastParsedName;
    string m_LastParsedContent;

    void OnEnable()
    {
      m_ComponentName = serializedObject.FindProperty("componentName");
      m_TransformUsageType = serializedObject.FindProperty("transformUsageType");
      m_PropertyOverrides = serializedObject.FindProperty("propertyOverrides");
    }

    public override void OnInspectorGUI()
    {
      serializedObject.Update();

      DrawComponentNameField();
      EditorGUILayout.PropertyField(m_TransformUsageType, new GUIContent("Transform Usage"));

      var authoring = (JsComponentAuthoring)target;
      if (authoring.HasValidComponent)
        DrawPropertyOverrides(authoring);

      serializedObject.ApplyModifiedProperties();
    }

    void DrawComponentNameField()
    {
      EditorGUILayout.PropertyField(m_ComponentName, new GUIContent("Component"));

      var name = m_ComponentName.stringValue;
      if (!string.IsNullOrEmpty(name))
      {
        var fullPath = ResolveComponentPath(name);
        if (fullPath == null || !File.Exists(fullPath))
          EditorGUILayout.HelpBox($"Component not found: {name}", MessageType.Warning);
      }
    }

    void DrawPropertyOverrides(JsComponentAuthoring authoring)
    {
      var name = authoring.componentName;
      var fullPath = ResolveComponentPath(name);
      if (fullPath == null || !File.Exists(fullPath))
        return;

      var content = File.ReadAllText(fullPath);
      if (
        m_ParsedProperties == null
        || m_LastParsedName != name
        || m_LastParsedContent != content
      )
      {
        m_ParsedProperties = JsPropertyParser.Parse(content);
        m_LastParsedName = name;
        m_LastParsedContent = content;
      }

      if (m_ParsedProperties.Count == 0)
        return;

      EditorGUILayout.Space();
      EditorGUILayout.LabelField("Property Overrides", EditorStyles.boldLabel);

      foreach (var parsed in m_ParsedProperties)
      {
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

    void SetOverride(JsComponentAuthoring authoring, string key, JsPropertyType type, float value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty
      {
        key = key,
        type = type,
        floatValue = value,
      };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideBool(JsComponentAuthoring authoring, string key, bool value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty
      {
        key = key,
        type = JsPropertyType.Bool,
        boolValue = value,
      };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideString(JsComponentAuthoring authoring, string key, string value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty
      {
        key = key,
        type = JsPropertyType.String,
        stringValue = value,
      };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideVector2(JsComponentAuthoring authoring, string key, Vector2 value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty
      {
        key = key,
        type = JsPropertyType.Vector2,
        vector2Value = value,
      };
      if (idx >= 0)
        authoring.propertyOverrides[idx] = prop;
      else
        authoring.propertyOverrides.Add(prop);
      EditorUtility.SetDirty(authoring);
    }

    void SetOverrideVector3(JsComponentAuthoring authoring, string key, Vector3 value)
    {
      Undo.RecordObject(authoring, "Change JS Property");
      var idx = FindOverrideIndex(authoring.propertyOverrides, key);
      var prop = new JsSerializedProperty
      {
        key = key,
        type = JsPropertyType.Vector3,
        vector3Value = value,
      };
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
      return name is "pauseTimer" or "paused" or "target" or "origin";
    }

    /// <summary>
    /// Resolve a component name to an absolute .ts file path by searching
    /// {searchPath}/components/{name}.ts across all registered search paths.
    /// </summary>
    static string ResolveComponentPath(string componentName)
    {
      if (string.IsNullOrEmpty(componentName))
        return null;

      var relativePath = "components/" + componentName + ".ts";

      // Check default StreamingAssets path
      var defaultBase = JsScriptPathUtility.SCRIPTS_FOLDER_RELATIVE;
      var projectRoot = Path.GetDirectoryName(Application.dataPath);
      var candidate = Path.Combine(projectRoot, defaultBase, relativePath);
      if (File.Exists(candidate))
        return candidate;

      // Check registered search paths
      foreach (var searchPath in JsScriptPathUtility.GetSearchPaths())
      {
        candidate = Path.Combine(searchPath, relativePath);
        if (File.Exists(candidate))
          return candidate;
      }

      return null;
    }
  }
}
