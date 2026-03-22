using Unity.Entities;

namespace MiniSpatial
{
  public enum SpatialEventState : byte
  {
    Enter = 0,
    Stay = 1,
    Exit = 2,
  }

  public struct SpatialTrigger : IComponentData
  {
    public SpatialShape shape;
    public int targetTag;
  }

  public struct StatefulSpatialOverlap : IBufferElementData
  {
    public Entity other;
    public SpatialEventState state;
  }

  public struct PreviousSpatialOverlap : IBufferElementData
  {
    public Entity other;
  }
}
