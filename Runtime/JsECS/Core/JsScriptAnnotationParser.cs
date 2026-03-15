namespace UnityJS.Entities.Core
{
  using System;
  using System.IO;
  using Components;

  /// <summary>
  /// Parses JS script annotations from comment headers.
  /// Supported annotations:
  /// - @tick: variable|fixed|before_physics|after_physics|after_transform
  /// </summary>
  public static class JsScriptAnnotationParser
  {
    const string TICK_ANNOTATION = "@tick:";
    const int MAX_HEADER_LINES = 20;

    /// <summary>
    /// Parsed script annotations.
    /// </summary>
    public struct ScriptAnnotations
    {
      public JsTickGroup tickGroup;
      public bool hasTickAnnotation;
    }

    /// <summary>
    /// Parses annotations from script source code.
    /// Only scans the first MAX_HEADER_LINES lines for performance.
    /// </summary>
    public static ScriptAnnotations Parse(string source)
    {
      var result = new ScriptAnnotations
      {
        tickGroup = JsTickGroup.Variable,
        hasTickAnnotation = false,
      };

      if (string.IsNullOrEmpty(source))
        return result;

      using var reader = new StringReader(source);
      var lineCount = 0;

      while (reader.ReadLine() is { } line && lineCount < MAX_HEADER_LINES)
      {
        lineCount++;
        var trimmed = line.Trim();

        // Only process comment lines (JS-style //)
        if (!trimmed.StartsWith("//"))
          continue;

        // Remove comment prefix
        var content = trimmed[2..].Trim();

        // Check for @tick annotation
        if (content.StartsWith(TICK_ANNOTATION, StringComparison.OrdinalIgnoreCase))
        {
          var value = content[TICK_ANNOTATION.Length..].Trim().ToLowerInvariant();
          result.tickGroup = ParseTickGroup(value);
          result.hasTickAnnotation = true;
        }
      }

      return result;
    }

    /// <summary>
    /// Parses annotations from a script file.
    /// </summary>
    public static ScriptAnnotations ParseFile(string filePath)
    {
      if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        return new ScriptAnnotations
        {
          tickGroup = JsTickGroup.Variable,
          hasTickAnnotation = false,
        };

      // Read only enough lines for header parsing
      var lines = new System.Text.StringBuilder();
      using var reader = new StreamReader(filePath);
      var lineCount = 0;

      while (!reader.EndOfStream && lineCount < MAX_HEADER_LINES)
      {
        lines.AppendLine(reader.ReadLine());
        lineCount++;
      }

      return Parse(lines.ToString());
    }

    static JsTickGroup ParseTickGroup(string value)
    {
      return value switch
      {
        "variable" => JsTickGroup.Variable,
        "fixed" => JsTickGroup.Fixed,
        "before_physics" => JsTickGroup.BeforePhysics,
        "after_physics" => JsTickGroup.AfterPhysics,
        "after_transform" => JsTickGroup.AfterTransform,
        _ => JsTickGroup.Variable,
      };
    }

    /// <summary>
    /// Returns the string representation of a tick group for use in annotations.
    /// </summary>
    public static string TickGroupToString(JsTickGroup group)
    {
      return group switch
      {
        JsTickGroup.Variable => "variable",
        JsTickGroup.Fixed => "fixed",
        JsTickGroup.BeforePhysics => "before_physics",
        JsTickGroup.AfterPhysics => "after_physics",
        JsTickGroup.AfterTransform => "after_transform",
        _ => "variable",
      };
    }
  }
}
