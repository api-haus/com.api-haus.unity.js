using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace MiniSpatial
{
  public interface IAABBVisitor
  {
    bool OnVisit(in SpatialEntry entry);
  }

  public unsafe struct KDTree : IDisposable
  {
    private NativeArray<SpatialEntry> m_Nodes;
    private Bounds m_Bounds;

    public bool IsCreated => m_Nodes.IsCreated;
    public int Count => m_Nodes.Length;

    public Bounds GetBounds() => m_Bounds;

    public KDTree(NativeArray<SpatialEntry> entries, Allocator allocator)
    {
      m_Nodes = default;
      m_Bounds = default;
      Construct(entries, allocator);
    }

    private void Construct(NativeArray<SpatialEntry> entries, Allocator allocator)
    {
      if (entries.Length == 0)
      {
        m_Nodes = new NativeArray<SpatialEntry>(0, allocator);
        m_Bounds = default;
        return;
      }

      m_Nodes = new NativeArray<SpatialEntry>(entries.Length, allocator);

      float3 min = new float3(float.PositiveInfinity);
      float3 max = new float3(float.NegativeInfinity);
      for (int i = 0; i < entries.Length; i++)
      {
        min = math.min(min, entries[i].shape.Center);
        max = math.max(max, entries[i].shape.Center);
      }
      m_Bounds = default;
      m_Bounds.SetMinMax((Vector3)min, (Vector3)max);

      var listCopy = new NativeList<SpatialEntry>(entries.Length, Allocator.Temp);
      listCopy.CopyFrom(entries);
      BuildRecursive(ref listCopy, 0, 0, listCopy.Length, 0);
      listCopy.Dispose();
    }

    private void BuildRecursive(
      ref NativeList<SpatialEntry> src,
      int index,
      int left,
      int right,
      int depth
    )
    {
      int length = right - left;
      if (length <= 0)
        return;

      if (length == 1)
      {
        m_Nodes[index] = src[left];
        return;
      }

      int treeHeight = (int)math.ceil(math.log2(length + 1));
      int maxN = 1 << treeHeight;
      int minN = 1 << (treeHeight - 1);
      int half = (maxN + minN) / 2;

      int medianIdx;
      if (length < half)
        medianIdx = (minN / 2) + (length - minN);
      else
        medianIdx = (maxN / 2) - 1;
      medianIdx += left;

      int axis0 = depth % 3;
      int axis1 = (depth + 1) % 3;
      int axis2 = (depth + 2) % 3;
      var comparer = new Composite3DComparer
      {
        axis0 = axis0,
        axis1 = axis1,
        axis2 = axis2,
      };

      QuickSelect(ref src, comparer, medianIdx - left, left, right);
      m_Nodes[index] = src[medianIdx];

      BuildRecursive(ref src, index * 2 + 1, left, medianIdx, depth + 1);
      BuildRecursive(ref src, index * 2 + 2, medianIdx + 1, right, depth + 1);
    }

    public void Range<T>(Bounds searchBounds, ref T visitor)
      where T : struct, IAABBVisitor
    {
      if (!m_Nodes.IsCreated || m_Nodes.Length == 0)
        return;
      float3 min = (float3)(Vector3)m_Bounds.min;
      float3 max = (float3)(Vector3)m_Bounds.max;
      RangeRecursive(searchBounds, ref visitor, 0, min, max, 0);
    }

    private void RangeRecursive<T>(
      Bounds searchBounds,
      ref T visitor,
      int nodeIdx,
      float3 min,
      float3 max,
      int depth
    )
      where T : struct, IAABBVisitor
    {
      var entry = m_Nodes[nodeIdx];

      if (searchBounds.Contains(entry.shape.Center))
      {
        if (!visitor.OnVisit(in entry))
          return;
      }

      int left = nodeIdx * 2 + 1;
      int right = nodeIdx * 2 + 2;

      if (left >= m_Nodes.Length)
        return;

      int axis = depth % 3;
      float splitPlane = entry.shape.Center[axis];

      float3 maxLeft = max;
      float3 minRight = min;
      maxLeft[axis] = splitPlane;
      minRight[axis] = splitPlane;

      var boundsLeft = new Bounds();
      boundsLeft.SetMinMax((Vector3)min, (Vector3)maxLeft);

      if (searchBounds.Intersects(boundsLeft))
        RangeRecursive(searchBounds, ref visitor, left, min, maxLeft, depth + 1);

      if (right < m_Nodes.Length)
      {
        var boundsRight = new Bounds();
        boundsRight.SetMinMax((Vector3)minRight, (Vector3)max);

        if (searchBounds.Intersects(boundsRight))
          RangeRecursive(searchBounds, ref visitor, right, minRight, max, depth + 1);
      }
    }

    public void Dispose()
    {
      if (m_Nodes.IsCreated)
        m_Nodes.Dispose();
    }

    private struct Composite3DComparer : IComparer<SpatialEntry>
    {
      public int axis0;
      public int axis1;
      public int axis2;

      public int Compare(SpatialEntry a, SpatialEntry b)
      {
        int comp = a.shape.Center[axis0].CompareTo(b.shape.Center[axis0]);
        if (comp != 0)
          return comp;
        comp = a.shape.Center[axis1].CompareTo(b.shape.Center[axis1]);
        if (comp != 0)
          return comp;
        return a.shape.Center[axis2].CompareTo(b.shape.Center[axis2]);
      }
    }

    private static void QuickSelect(
      ref NativeList<SpatialEntry> list,
      Composite3DComparer comparer,
      int k,
      int left,
      int right
    )
    {
      var rnd = new Unity.Mathematics.Random();
      rnd.InitState();

      if (left == right)
        return;

      k += left;
      int pivot = RandomPartition(ref list, comparer, ref rnd, left, right);
      while (pivot != k)
      {
        if (pivot < k)
          left = pivot + 1;
        else
          right = pivot;

        if (left == right)
          return;
        pivot = RandomPartition(ref list, comparer, ref rnd, left, right);
      }
    }

    private static void Swap(ref NativeList<SpatialEntry> list, int a, int b)
    {
      (list[a], list[b]) = (list[b], list[a]);
    }

    private static int RandomPartition(
      ref NativeList<SpatialEntry> list,
      Composite3DComparer comparer,
      ref Unity.Mathematics.Random rnd,
      int left,
      int right
    )
    {
      int pivot = rnd.NextInt(left, right);
      int pivotLocation = left;
      var pivotElement = list[pivot];

      Swap(ref list, pivot, right - 1);

      for (int i = left; i < right - 1; i++)
      {
        if (comparer.Compare(list[i], pivotElement) < 0)
        {
          Swap(ref list, i, pivotLocation);
          pivotLocation++;
        }
      }
      Swap(ref list, right - 1, pivotLocation);

      return pivotLocation;
    }
  }
}
