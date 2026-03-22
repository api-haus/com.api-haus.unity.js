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
  /// JS component script attached to an entity.
  /// Written by baker with stateRef=-1 (needs init).
  /// JsComponentInitSystem processes entries with stateRef=-1, loads the script,
  /// and sets stateRef to the runtime state handle.
  /// </summary>
  public struct JsScript : IBufferElementData
  {
    public FixedString64Bytes scriptName;
    public FixedString512Bytes propertiesJson;
    public int stateRef;
    public int entityIndex;
    public Hash128 requestHash;
    public bool disabled;
    public JsTickGroup tickGroup;
  }
}
