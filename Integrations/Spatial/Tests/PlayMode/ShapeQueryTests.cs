using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MiniSpatial.Tests
{
  public class ShapeQueryTests
  {
    [Test]
    public void SphereQuery_FindsOverlappingEntries()
    {
      var entries = new NativeArray<SpatialEntry>(3, Allocator.Temp);
      entries[0] = MakeEntry(new float3(0, 0, 0), SpatialShape.Sphere(1f, new float3(0, 0, 0)));
      entries[1] = MakeEntry(new float3(1, 0, 0), SpatialShape.Sphere(1f, new float3(1, 0, 0)));
      entries[2] = MakeEntry(new float3(100, 0, 0), SpatialShape.Sphere(1f, new float3(100, 0, 0)));

      var tree = new KDTree(entries, Allocator.Temp);

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(4f), results = results };

      var queryAABB = query.shape.ComputeWorldAABB();
      tree.Range(queryAABB, ref query);

      Assert.AreEqual(2, results.Length);

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    [Test]
    public void BoxQuery_FindsOverlappingEntries()
    {
      var entries = new NativeArray<SpatialEntry>(3, Allocator.Temp);
      entries[0] = MakeEntry(
        new float3(0, 0, 0),
        SpatialShape.Box(new float3(1, 1, 1), new float3(0, 0, 0))
      );
      entries[1] = MakeEntry(
        new float3(1.5f, 0, 0),
        SpatialShape.Box(new float3(1, 1, 1), new float3(1.5f, 0, 0))
      );
      entries[2] = MakeEntry(
        new float3(50, 0, 0),
        SpatialShape.Box(new float3(1, 1, 1), new float3(50, 0, 0))
      );

      var tree = new KDTree(entries, Allocator.Temp);

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery
      {
        shape = SpatialShape.Box(new float3(2, 2, 2)),
        results = results,
      };

      var queryAABB = query.shape.ComputeWorldAABB();
      tree.Range(queryAABB, ref query);

      Assert.AreEqual(2, results.Length);

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    [Test]
    public void SeparateTrees_QueryOnlySearchesOwnTag()
    {
      // Build separate trees per tag
      var enemyEntries = new NativeArray<SpatialEntry>(2, Allocator.Temp);
      enemyEntries[0] = MakeEntry(
        new float3(0, 0, 0),
        SpatialShape.Sphere(1f, new float3(0, 0, 0))
      );
      enemyEntries[1] = MakeEntry(
        new float3(0.5f, 0, 0),
        SpatialShape.Sphere(1f, new float3(0.5f, 0, 0))
      );

      var allyEntries = new NativeArray<SpatialEntry>(1, Allocator.Temp);
      allyEntries[0] = MakeEntry(new float3(1, 0, 0), SpatialShape.Sphere(1f, new float3(1, 0, 0)));

      var enemyTree = new KDTree(enemyEntries, Allocator.Temp);
      var allyTree = new KDTree(allyEntries, Allocator.Temp);

      // Query enemy tree
      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(100f), results = results };

      var queryAABB = query.shape.ComputeWorldAABB();
      enemyTree.Range(queryAABB, ref query);
      Assert.AreEqual(2, results.Length, "Enemy tree should have 2 entries");

      // Query ally tree
      results.Clear();
      allyTree.Range(queryAABB, ref query);
      Assert.AreEqual(1, results.Length, "Ally tree should have 1 entry");

      results.Dispose();
      enemyTree.Dispose();
      allyTree.Dispose();
      enemyEntries.Dispose();
      allyEntries.Dispose();
    }

    [Test]
    public void MixedShapes_SphereQueryAgainstBoxEntries()
    {
      var entries = new NativeArray<SpatialEntry>(2, Allocator.Temp);
      entries[0] = MakeEntry(
        new float3(2, 0, 0),
        SpatialShape.Box(new float3(1, 1, 1), new float3(2, 0, 0))
      );
      entries[1] = MakeEntry(
        new float3(20, 0, 0),
        SpatialShape.Box(new float3(1, 1, 1), new float3(20, 0, 0))
      );

      var tree = new KDTree(entries, Allocator.Temp);

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(16f), results = results };

      var queryAABB = query.shape.ComputeWorldAABB();
      tree.Range(queryAABB, ref query);

      Assert.AreEqual(1, results.Length);

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    private static SpatialEntry MakeEntry(float3 pos, SpatialShape shape) =>
      new()
      {
        matrix = float4x4.Translate(pos),
        entity = Entity.Null,
        shape = shape,
      };
  }
}
