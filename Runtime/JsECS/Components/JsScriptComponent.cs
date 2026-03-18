namespace UnityJS.Entities.Components
{
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

  /// <summary>
  /// Request to add a JS script to an entity.
  /// Processed by fulfillment system which creates the runtime state
  /// and adds the script to the JsScript buffer.
  /// </summary>
  public struct JsScriptRequest : IBufferElementData
  {
    public FixedString64Bytes scriptName;
    public FixedString512Bytes propertiesJson;
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
