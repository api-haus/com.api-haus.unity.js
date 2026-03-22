using Unity.Collections;
using Unity.Entities;

namespace MiniSpatial
{
  public struct ShapeQuery : IAABBVisitor
  {
    public SpatialShape shape;
    public NativeList<Entity> results;

    public bool OnVisit(in SpatialEntry entry)
    {
      if (shape.Overlaps(entry.shape))
        results.Add(entry.entity);
      return true;
    }
  }
}
