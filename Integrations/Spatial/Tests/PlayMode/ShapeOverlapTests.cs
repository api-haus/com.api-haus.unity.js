using NUnit.Framework;
using Unity.Mathematics;

namespace MiniSpatial.Tests
{
  public class ShapeOverlapTests
  {
    [Test]
    public void SphereSphere_Overlapping()
    {
      Assert.IsTrue(ShapeOverlap.SphereSphereOverlap(float3.zero, 4f, new float3(3, 0, 0), 4f));
    }

    [Test]
    public void SphereSphere_NotOverlapping()
    {
      Assert.IsFalse(ShapeOverlap.SphereSphereOverlap(float3.zero, 1f, new float3(10, 0, 0), 1f));
    }

    [Test]
    public void SphereSphere_Touching()
    {
      // radius 1 each (radiusSq=1), centers 2 apart — exactly touching
      // SphereSphereOverlap uses strict > so touching returns false
      Assert.IsFalse(ShapeOverlap.SphereSphereOverlap(float3.zero, 1f, new float3(2, 0, 0), 1f));
    }

    [Test]
    public void SphereSphere_Concentric()
    {
      Assert.IsTrue(ShapeOverlap.SphereSphereOverlap(float3.zero, 4f, float3.zero, 1f));
    }

    [Test]
    public void SphereBox_Overlapping()
    {
      Assert.IsTrue(
        ShapeOverlap.SphereBoxOverlap(float3.zero, 4f, new float3(2, 0, 0), new float3(1, 1, 1))
      );
    }

    [Test]
    public void SphereBox_NotOverlapping()
    {
      Assert.IsFalse(
        ShapeOverlap.SphereBoxOverlap(float3.zero, 1f, new float3(10, 0, 0), new float3(1, 1, 1))
      );
    }

    [Test]
    public void SphereBox_CornerCase()
    {
      // Sphere at origin radiusSq=2, box corner at (1,1,0)
      Assert.IsTrue(
        ShapeOverlap.SphereBoxOverlap(float3.zero, 2f, new float3(2, 2, 0), new float3(1, 1, 1))
      );
    }

    [Test]
    public void BoxBox_Overlapping()
    {
      Assert.IsTrue(
        ShapeOverlap.BoxBoxOverlap(
          float3.zero,
          new float3(2, 2, 2),
          new float3(3, 0, 0),
          new float3(2, 2, 2)
        )
      );
    }

    [Test]
    public void BoxBox_NotOverlapping()
    {
      Assert.IsFalse(
        ShapeOverlap.BoxBoxOverlap(
          float3.zero,
          new float3(1, 1, 1),
          new float3(10, 0, 0),
          new float3(1, 1, 1)
        )
      );
    }

    [Test]
    public void BoxBox_Touching()
    {
      Assert.IsTrue(
        ShapeOverlap.BoxBoxOverlap(
          float3.zero,
          new float3(1, 1, 1),
          new float3(2, 0, 0),
          new float3(1, 1, 1)
        )
      );
    }

    [Test]
    public void CuboidSphereOverlap_SphereInsideBox()
    {
      Assert.IsTrue(
        ShapeOverlap.CuboidSphereOverlap(
          new float3(-5, -5, -5),
          new float3(5, 5, 5),
          float3.zero,
          1f
        )
      );
    }

    [Test]
    public void SphereContainsCuboid_True()
    {
      // Sphere at origin radius=10 (radiusSq=100), small box
      Assert.IsTrue(
        ShapeOverlap.SphereContainsCuboid(
          float3.zero,
          100f,
          new float3(-1, -1, -1),
          new float3(1, 1, 1)
        )
      );
    }

    [Test]
    public void SphereContainsCuboid_False()
    {
      Assert.IsFalse(
        ShapeOverlap.SphereContainsCuboid(
          float3.zero,
          1f,
          new float3(-5, -5, -5),
          new float3(5, 5, 5)
        )
      );
    }
  }
}
