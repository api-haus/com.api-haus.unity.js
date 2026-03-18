#if UNITY_EDITOR
namespace UnityJS.Editor
{
  using System.Collections.Generic;
  using System.Globalization;
  using System.Text;
  using System.Text.RegularExpressions;
  using Entities.Authoring;
  using UnityEngine;

  public struct JsParsedProperty
  {
    public string name;
    public string tooltip;
    public JsPropertyType type;
    public float floatValue;
    public bool boolValue;
    public string stringValue;
    public Vector2 vector2Value;
    public Vector3 vector3Value;
  }

  public static class JsPropertyParser
  {
    // Match: private <name> = <value>
    // Also matches: private <name>: <type> = <value>
    static readonly Regex s_PropertyRegex = new(
      @"^\s*(?:private|public|protected)\s+(\w+)(?:\s*:\s*\w+)?\s*=\s*(.+)$"
    );

    static readonly Regex s_Float3Call = new(
      @"^float3\(\s*([^,)]+)\s*,\s*([^,)]+)\s*,\s*([^,)]+)\s*\)$"
    );

    static readonly Regex s_Float2Call = new(
      @"^float2\(\s*([^,)]+)\s*,\s*([^,)]+)\s*\)$"
    );

    static readonly Regex s_Vector3New = new(
      @"^new\s+Vector3\(\s*([^,)]+)\s*,\s*([^,)]+)\s*,\s*([^,)]+)\s*\)$"
    );

    static readonly Regex s_Vector2New = new(
      @"^new\s+Vector2\(\s*([^,)]+)\s*,\s*([^,)]+)\s*\)$"
    );

    static readonly Regex s_EnumRef = new(@"^\w+\.\w+$");

    public static List<JsParsedProperty> Parse(string source)
    {
      var results = new List<JsParsedProperty>();
      var lines = source.Split('\n');
      var jsdoc = new StringBuilder();
      var inJsdoc = false;

      foreach (var line in lines)
      {
        var trimmed = line.Trim();

        if (trimmed.Contains("/**"))
        {
          jsdoc.Clear();
          inJsdoc = true;
          // Extract content after /** and before possible */
          var after = trimmed.Substring(trimmed.IndexOf("/**") + 3);
          if (after.Contains("*/"))
          {
            after = after.Substring(0, after.IndexOf("*/"));
            inJsdoc = false;
          }
          after = after.Trim();
          if (after.Length > 0)
            jsdoc.Append(after);
          continue;
        }

        if (inJsdoc)
        {
          if (trimmed.Contains("*/"))
          {
            var before = trimmed.Substring(0, trimmed.IndexOf("*/"));
            before = before.TrimStart('*', ' ');
            if (before.Length > 0)
            {
              if (jsdoc.Length > 0) jsdoc.Append(' ');
              jsdoc.Append(before);
            }
            inJsdoc = false;
          }
          else
          {
            var content = trimmed.TrimStart('*', ' ');
            if (content.Length > 0)
            {
              if (jsdoc.Length > 0) jsdoc.Append(' ');
              jsdoc.Append(content);
            }
          }
          continue;
        }

        var match = s_PropertyRegex.Match(trimmed);
        if (match.Success)
        {
          var name = match.Groups[1].Value;
          var rawValue = match.Groups[2].Value.Trim().TrimEnd(';');

          if (TryParseProperty(name, rawValue, out var prop))
          {
            prop.tooltip = jsdoc.Length > 0 ? jsdoc.ToString() : null;
            results.Add(prop);
          }
          jsdoc.Clear();
          continue;
        }

        // Non-comment, non-blank, non-property line — discard accumulated JSDoc
        if (trimmed.Length > 0)
          jsdoc.Clear();
      }

      return results;
    }

    static bool TryParseProperty(string name, string value, out JsParsedProperty prop)
    {
      prop = default;
      prop.name = name;

      // Bool
      if (value == "true" || value == "false")
      {
        prop.type = JsPropertyType.Bool;
        prop.boolValue = value == "true";
        return true;
      }

      // String
      if ((value.StartsWith("'") && value.EndsWith("'")) ||
          (value.StartsWith("\"") && value.EndsWith("\"")))
      {
        prop.type = JsPropertyType.String;
        prop.stringValue = value.Substring(1, value.Length - 2);
        return true;
      }

      // float3.zero
      if (value == "float3.zero")
      {
        prop.type = JsPropertyType.Vector3;
        prop.vector3Value = Vector3.zero;
        return true;
      }

      // float2.zero
      if (value == "float2.zero")
      {
        prop.type = JsPropertyType.Vector2;
        prop.vector2Value = Vector2.zero;
        return true;
      }

      // float3(x, y, z)
      var m3 = s_Float3Call.Match(value);
      if (m3.Success)
      {
        if (TryParseFloat(m3.Groups[1].Value, out var x) &&
            TryParseFloat(m3.Groups[2].Value, out var y) &&
            TryParseFloat(m3.Groups[3].Value, out var z))
        {
          prop.type = JsPropertyType.Vector3;
          prop.vector3Value = new Vector3(x, y, z);
          return true;
        }
      }

      // new Vector3(x, y, z)
      var mv3 = s_Vector3New.Match(value);
      if (mv3.Success)
      {
        if (TryParseFloat(mv3.Groups[1].Value, out var x) &&
            TryParseFloat(mv3.Groups[2].Value, out var y) &&
            TryParseFloat(mv3.Groups[3].Value, out var z))
        {
          prop.type = JsPropertyType.Vector3;
          prop.vector3Value = new Vector3(x, y, z);
          return true;
        }
      }

      // float2(x, y)
      var m2 = s_Float2Call.Match(value);
      if (m2.Success)
      {
        if (TryParseFloat(m2.Groups[1].Value, out var x) &&
            TryParseFloat(m2.Groups[2].Value, out var y))
        {
          prop.type = JsPropertyType.Vector2;
          prop.vector2Value = new Vector2(x, y);
          return true;
        }
      }

      // new Vector2(x, y)
      var mv2 = s_Vector2New.Match(value);
      if (mv2.Success)
      {
        if (TryParseFloat(mv2.Groups[1].Value, out var x) &&
            TryParseFloat(mv2.Groups[2].Value, out var y))
        {
          prop.type = JsPropertyType.Vector2;
          prop.vector2Value = new Vector2(x, y);
          return true;
        }
      }

      // Numeric literal
      if (TryParseFloat(value, out var fVal))
      {
        prop.type = JsPropertyType.Float;
        prop.floatValue = fVal;
        return true;
      }

      // Enum reference like WANDER_PLANE.XZ → Float(0)
      if (s_EnumRef.IsMatch(value))
      {
        prop.type = JsPropertyType.Float;
        prop.floatValue = 0f;
        return true;
      }

      // Complex expression — skip
      return false;
    }

    static bool TryParseFloat(string s, out float result)
    {
      s = s.Trim();
      return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
  }
}
#endif
