using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MiniSpatial.Tests
{
  public class KDTreeTests
  {
    [Test]
    public void Build_And_Range_ReturnsCorrectEntries()
    {
      var entries = new NativeArray<SpatialEntry>(5, Allocator.Temp);
      for (int i = 0; i < 5; i++)
      {
        var pos = new float3(i * 2, 0, 0);
        entries[i] = new SpatialEntry
        {
          matrix = float4x4.Translate(pos),
          entity = Entity.Null,
          shape = SpatialShape.Sphere(1f, pos),
        };
      }

      var tree = new KDTree(entries, Allocator.Temp);
      Assert.AreEqual(5, tree.Count);

      // Search bounds covering positions 0..4 (entries at x=0,2,4)
      var results = new NativeList<Entity>(16, Allocator.Temp);
      var visitor = new CollectVisitor { results = results };
      tree.Range(new Bounds(new Vector3(2, 0, 0), new Vector3(6, 2, 2)), ref visitor);

      Assert.AreEqual(3, results.Length); // x=0, x=2, x=4

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    [Test]
    public void Build_SingleEntry()
    {
      var entries = new NativeArray<SpatialEntry>(1, Allocator.Temp);
      entries[0] = new SpatialEntry
      {
        matrix = float4x4.Translate(new float3(5, 5, 5)),
        entity = Entity.Null,
        shape = SpatialShape.Sphere(1f, new float3(5, 5, 5)),
      };

      var tree = new KDTree(entries, Allocator.Temp);
      Assert.AreEqual(1, tree.Count);

      var results = new NativeList<Entity>(4, Allocator.Temp);
      var visitor = new CollectVisitor { results = results };
      tree.Range(new Bounds(new Vector3(5, 5, 5), new Vector3(2, 2, 2)), ref visitor);

      Assert.AreEqual(1, results.Length);

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    [Test]
    public void Build_EmptyArray()
    {
      var entries = new NativeArray<SpatialEntry>(0, Allocator.Temp);
      var tree = new KDTree(entries, Allocator.Temp);
      Assert.AreEqual(0, tree.Count);

      var results = new NativeList<Entity>(4, Allocator.Temp);
      var visitor = new CollectVisitor { results = results };
      tree.Range(new Bounds(Vector3.zero, Vector3.one * 100), ref visitor);
      Assert.AreEqual(0, results.Length);

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    [Test]
    public void Range_MissesOutOfBoundsEntries()
    {
      var entries = new NativeArray<SpatialEntry>(3, Allocator.Temp);
      entries[0] = MakeEntry(new float3(0, 0, 0));
      entries[1] = MakeEntry(new float3(100, 0, 0));
      entries[2] = MakeEntry(new float3(0, 100, 0));

      var tree = new KDTree(entries, Allocator.Temp);

      var results = new NativeList<Entity>(4, Allocator.Temp);
      var visitor = new CollectVisitor { results = results };
      tree.Range(new Bounds(new Vector3(0, 0, 0), new Vector3(2, 2, 2)), ref visitor);

      Assert.AreEqual(1, results.Length);

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    [Test]
    public void Range_LargeDataset()
    {
      int n = 1000;
      var entries = new NativeArray<SpatialEntry>(n, Allocator.Temp);
      var rng = new Unity.Mathematics.Random(42);
      int expectedCount = 0;

      for (int i = 0; i < n; i++)
      {
        var pos = rng.NextFloat3() * 200f - 100f;
        entries[i] = MakeEntry(pos);
        if (math.all(pos >= -10f) && math.all(pos <= 10f))
          expectedCount++;
      }

      var tree = new KDTree(entries, Allocator.Temp);

      var results = new NativeList<Entity>(n, Allocator.Temp);
      var visitor = new CollectVisitor { results = results };
      tree.Range(new Bounds(Vector3.zero, new Vector3(20, 20, 20)), ref visitor);

      Assert.AreEqual(expectedCount, results.Length);

      results.Dispose();
      tree.Dispose();
      entries.Dispose();
    }

    private static SpatialEntry MakeEntry(float3 pos)
    {
      return new SpatialEntry
      {
        matrix = float4x4.Translate(pos),
        entity = Entity.Null,
        shape = SpatialShape.Sphere(1f, pos),
      };
    }

    private struct CollectVisitor : IAABBVisitor
    {
      public NativeList<Entity> results;

      public bool OnVisit(in SpatialEntry entry)
      {
        results.Add(entry.entity);
        return true;
      }
    }
  }
}
