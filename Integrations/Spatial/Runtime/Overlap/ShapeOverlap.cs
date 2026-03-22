using Unity.Mathematics;

namespace MiniSpatial
{
  public static class ShapeOverlap
  {
    public static bool SphereSphereOverlap(
      float3 center0,
      float radiusSq0,
      float3 center1,
      float radiusSq1
    )
    {
      float distSq = math.lengthsq(center1 - center0);
      return radiusSq0 + radiusSq1 + 2.0f * math.sqrt(radiusSq0 * radiusSq1) > distSq;
    }

    public static bool CuboidSphereOverlap(
      float3 boxMin,
      float3 boxMax,
      float3 sphereCenter,
      float radiusSq
    )
    {
      float3 closestPoint = math.max(boxMin, math.min(sphereCenter, boxMax));
      return math.distancesq(sphereCenter, closestPoint) <= radiusSq;
    }

    public static bool SphereBoxOverlap(
      float3 sphereCenter,
      float radiusSq,
      float3 boxCenter,
      float3 halfExtents
    )
    {
      float3 boxMin = boxCenter - halfExtents;
      float3 boxMax = boxCenter + halfExtents;
      return CuboidSphereOverlap(boxMin, boxMax, sphereCenter, radiusSq);
    }

    public static bool BoxBoxOverlap(float3 centerA, float3 halfA, float3 centerB, float3 halfB)
    {
      float3 minA = centerA - halfA;
      float3 maxA = centerA + halfA;
      float3 minB = centerB - halfB;
      float3 maxB = centerB + halfB;
      return math.all(minA <= maxB) && math.all(maxA >= minB);
    }

    public static bool SphereContainsCuboid(
      float3 sphereCenter,
      float radiusSq,
      float3 min,
      float3 max
    )
    {
      float3 brd = new float3(max.x, min.y, min.z);
      float3 tld = new float3(min.x, min.y, max.z);
      float3 trd = new float3(max.x, min.y, max.z);
      float3 blu = new float3(min.x, max.y, min.z);
      float3 bru = new float3(max.x, max.y, min.z);
      float3 tlu = new float3(min.x, max.y, max.z);

      return math.distancesq(sphereCenter, min) <= radiusSq
        && math.distancesq(sphereCenter, brd) <= radiusSq
        && math.distancesq(sphereCenter, tld) <= radiusSq
        && math.distancesq(sphereCenter, trd) <= radiusSq
        && math.distancesq(sphereCenter, max) <= radiusSq
        && math.distancesq(sphereCenter, blu) <= radiusSq
        && math.distancesq(sphereCenter, bru) <= radiusSq
        && math.distancesq(sphereCenter, tlu) <= radiusSq;
    }
  }
}
