using Unity.Mathematics;
using UnityEngine;

namespace MiniSpatial
{
  public static class SpatialShapeExtensions
  {
    public static Bounds ComputeWorldAABB(this SpatialShape s) =>
      s.type switch
      {
        SpatialShapeType.Sphere => new Bounds(
          s.sphere.center,
          new float3(math.sqrt(s.sphere.radiusSq) * 2)
        ),
        SpatialShapeType.Box => new Bounds(s.box.center, s.box.halfExtents * 2),
        _ => default,
      };

    public static bool Overlaps(this SpatialShape a, SpatialShape b) =>
      (a.type, b.type) switch
      {
        (SpatialShapeType.Sphere, SpatialShapeType.Sphere) => ShapeOverlap.SphereSphereOverlap(
          a.sphere.center,
          a.sphere.radiusSq,
          b.sphere.center,
          b.sphere.radiusSq
        ),

        (SpatialShapeType.Sphere, SpatialShapeType.Box) => ShapeOverlap.SphereBoxOverlap(
          a.sphere.center,
          a.sphere.radiusSq,
          b.box.center,
          b.box.halfExtents
        ),

        (SpatialShapeType.Box, SpatialShapeType.Sphere) => ShapeOverlap.SphereBoxOverlap(
          b.sphere.center,
          b.sphere.radiusSq,
          a.box.center,
          a.box.halfExtents
        ),

        (SpatialShapeType.Box, SpatialShapeType.Box) => ShapeOverlap.BoxBoxOverlap(
          a.box.center,
          a.box.halfExtents,
          b.box.center,
          b.box.halfExtents
        ),

        _ => false,
      };
  }
}
