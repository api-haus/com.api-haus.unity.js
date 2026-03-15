namespace UnityJS.Entities.Components
{
  using System;
  using Core;
  using Unity.Collections;
  using Unity.Entities;

  /// <summary>
  /// Persistent ID for JS entity references.
  /// Unlike Entity, this ID remains stable across structural changes.
  /// </summary>
  public struct JsEntityId : IComponentData
  {
    public int value;
  }

  [Serializable]
  public struct JsScriptAssetReference : IEquatable<JsScriptAssetReference>
  {
    public FixedString64Bytes scriptId;

    public bool IsValid => !scriptId.IsEmpty;

    public string Path => scriptId.ToString();

    public void SetPath(string path)
    {
      if (string.IsNullOrEmpty(path))
      {
        Clear();
        return;
      }

      if (
        !JsScriptPathUtility.TryNormalizeScriptId(path, out var normalized, out _)
        || string.IsNullOrEmpty(normalized)
      )
      {
        Clear();
        return;
      }

      scriptId = new FixedString64Bytes(normalized);
    }

    public void Clear()
    {
      scriptId.Clear();
    }

    public FixedString64Bytes AsFixedString()
    {
      return scriptId;
    }

    public override string ToString()
    {
      return scriptId.ToString();
    }

    public bool Equals(JsScriptAssetReference other)
    {
      return scriptId.Equals(other.scriptId);
    }

    public override bool Equals(object obj)
    {
      return obj is JsScriptAssetReference other && Equals(other);
    }

    public override int GetHashCode()
    {
      return scriptId.GetHashCode();
    }

    public static implicit operator FixedString64Bytes(JsScriptAssetReference reference) =>
      reference.scriptId;

    public static implicit operator JsScriptAssetReference(FixedString64Bytes scriptId) =>
      new() { scriptId = scriptId };
  }

  /// <summary>
  /// Request to add a JS script to an entity.
  /// Processed by fulfillment system which creates the runtime state
  /// and adds the script to the JsScript buffer.
  /// </summary>
  public struct JsScriptRequest : IBufferElementData
  {
    public FixedString64Bytes scriptName;
    public Hash128 requestHash;
    public bool fulfilled;
  }

  /// <summary>
  /// Initialized JS script with runtime state.
  /// This is a cleanup buffer - entity won't be destroyed until this buffer is removed.
  /// </summary>
  public struct JsScript : ICleanupBufferElementData
  {
    public FixedString64Bytes scriptName;
    public int stateRef;
    public int entityIndex;
    public Hash128 requestHash;
    public bool disabled;
    public JsTickGroup tickGroup;
  }
}
