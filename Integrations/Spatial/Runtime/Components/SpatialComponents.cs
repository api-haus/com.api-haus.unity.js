using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MiniSpatial
{
  public enum SpatialShapeType : byte
  {
    Sphere = 0,
    Box = 1,
  }

  public struct SpatialSphere
  {
    public float3 center;
    public float radiusSq;
  }

  public struct SpatialBox
  {
    public float3 center;
    public float3 halfExtents;
  }

  [StructLayout(LayoutKind.Explicit)]
  public struct SpatialShape
  {
    [FieldOffset(0)]
    public SpatialShapeType type;

    [FieldOffset(4)]
    public SpatialSphere sphere;

    [FieldOffset(4)]
    public SpatialBox box;

    public static SpatialShape Sphere(float radiusSq, float3 center = default) =>
      new()
      {
        type = SpatialShapeType.Sphere,
        sphere = new SpatialSphere { center = center, radiusSq = radiusSq },
      };

    public static SpatialShape Box(float3 halfExtents, float3 center = default) =>
      new()
      {
        type = SpatialShapeType.Box,
        box = new SpatialBox { center = center, halfExtents = halfExtents },
      };

    public float3 Center => type == SpatialShapeType.Sphere ? sphere.center : box.center;

    public SpatialShape Transform(float4x4 matrix) =>
      type switch
      {
        SpatialShapeType.Sphere => Sphere(sphere.radiusSq, math.transform(matrix, sphere.center)),
        SpatialShapeType.Box => Box(box.halfExtents, math.transform(matrix, box.center)),
        _ => default,
      };
  }

  public readonly struct SpatialTag : IEquatable<SpatialTag>
  {
    public readonly int hash;

    public SpatialTag(int hash) => this.hash = hash;

    public static implicit operator SpatialTag(string name) =>
      new((int)xxHash3.Hash64(new FixedString32Bytes(name)).x);

    public static implicit operator int(SpatialTag tag) => tag.hash;

    public bool Equals(SpatialTag other) => hash == other.hash;

    public override int GetHashCode() => hash;

    public override bool Equals(object obj) => obj is SpatialTag t && Equals(t);
  }

  public struct SpatialEntry
  {
    public float4x4 matrix;
    public Entity entity;
    public SpatialShape shape;
  }

  public struct SpatialAgent : IComponentData
  {
    public SpatialShape shape;
    public int tag;
#if SPATIAL_DEBUG
    public FixedString32Bytes debugTagName;
#endif
  }
}
