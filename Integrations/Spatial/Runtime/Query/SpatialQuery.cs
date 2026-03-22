using Unity.Burst;
using Unity.Collections;

namespace MiniSpatial
{
  internal struct SpatialQueryKey { }

  public static class SpatialQuery
  {
    private static readonly SharedStatic<SpatialTreeStore> s_TreeData =
      SharedStatic<SpatialTreeStore>.GetOrCreate<SpatialQueryKey>();

    internal static ref SpatialTreeStore TreeStore => ref s_TreeData.Data;

    public static void Range(int tagHash, ref ShapeQuery query)
    {
      ref var store = ref s_TreeData.Data;
      if (!store.isValid)
        return;

      int before = query.results.Length;

      if (!store.trees.TryGetValue(tagHash, out var tree))
      {
        SpatialQueryDebug.Record(tagHash, query.shape, -1);
        return;
      }

      var queryAABB = query.shape.ComputeWorldAABB();
      tree.Range(queryAABB, ref query);

      SpatialQueryDebug.Record(tagHash, query.shape, query.results.Length - before, tree.Count);
    }
  }

  public struct SpatialTreeStore
  {
    public NativeHashMap<int, KDTree> trees;
    public bool isValid;
  }
}
