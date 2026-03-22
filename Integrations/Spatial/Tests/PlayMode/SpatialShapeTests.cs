using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MiniSpatial.Tests
{
  public class SpatialShapeTests
  {
    [Test]
    public void Sphere_Construction()
    {
      var s = SpatialShape.Sphere(25f);
      Assert.AreEqual(SpatialShapeType.Sphere, s.type);
      Assert.AreEqual(25f, s.sphere.radiusSq);
      Assert.AreEqual(float3.zero, s.sphere.center);
    }

    [Test]
    public void Sphere_WithCenter()
    {
      var s = SpatialShape.Sphere(9f, new float3(1, 2, 3));
      Assert.AreEqual(new float3(1, 2, 3), s.sphere.center);
    }

    [Test]
    public void Box_Construction()
    {
      var b = SpatialShape.Box(new float3(2, 3, 4));
      Assert.AreEqual(SpatialShapeType.Box, b.type);
      Assert.AreEqual(new float3(2, 3, 4), b.box.halfExtents);
      Assert.AreEqual(float3.zero, b.box.center);
    }

    [Test]
    public void ComputeWorldAABB_Sphere()
    {
      var s = SpatialShape.Sphere(25f, new float3(10, 0, 0)); // radius = 5
      var aabb = s.ComputeWorldAABB();
      Assert.AreEqual(new Vector3(10, 0, 0), aabb.center);
      Assert.AreEqual(new Vector3(10, 10, 10), aabb.size);
    }

    [Test]
    public void ComputeWorldAABB_Box()
    {
      var b = SpatialShape.Box(new float3(2, 3, 4), new float3(5, 5, 5));
      var aabb = b.ComputeWorldAABB();
      Assert.AreEqual(new Vector3(5, 5, 5), aabb.center);
      Assert.AreEqual(new Vector3(4, 6, 8), aabb.size);
    }

    [Test]
    public void Overlaps_SphereSphere()
    {
      var a = SpatialShape.Sphere(4f);
      var b = SpatialShape.Sphere(4f, new float3(3, 0, 0));
      Assert.IsTrue(a.Overlaps(b));
      Assert.IsFalse(a.Overlaps(SpatialShape.Sphere(4f, new float3(20, 0, 0))));
    }

    [Test]
    public void Overlaps_SphereBox()
    {
      var s = SpatialShape.Sphere(4f);
      var b = SpatialShape.Box(new float3(1, 1, 1), new float3(2, 0, 0));
      Assert.IsTrue(s.Overlaps(b));
      Assert.IsFalse(s.Overlaps(SpatialShape.Box(new float3(1, 1, 1), new float3(20, 0, 0))));
    }

    [Test]
    public void Overlaps_BoxSphere()
    {
      var b = SpatialShape.Box(new float3(1, 1, 1), new float3(2, 0, 0));
      var s = SpatialShape.Sphere(4f);
      Assert.IsTrue(b.Overlaps(s));
    }

    [Test]
    public void Overlaps_BoxBox()
    {
      var a = SpatialShape.Box(new float3(2, 2, 2));
      var b = SpatialShape.Box(new float3(2, 2, 2), new float3(3, 0, 0));
      Assert.IsTrue(a.Overlaps(b));
      Assert.IsFalse(a.Overlaps(SpatialShape.Box(new float3(2, 2, 2), new float3(10, 0, 0))));
    }
  }
}
