using Unity.Burst;
using Unity.Collections;

namespace MiniSpatial
{
  public struct SpatialQueryDebugEntry
  {
    public int tagHash;
    public SpatialShape shape;
    public int resultCount;
    public int treeCount;
  }

  internal struct SpatialQueryDebugKey { }

  public static class SpatialQueryDebug
  {
    const int k_MaxEntries = 64;

    static readonly SharedStatic<FixedList4096Bytes<SpatialQueryDebugEntry>> s_Data = SharedStatic<
      FixedList4096Bytes<SpatialQueryDebugEntry>
    >.GetOrCreate<SpatialQueryDebugKey>();

    public static int Count => s_Data.Data.Length;

    public static SpatialQueryDebugEntry Get(int index) => s_Data.Data[index];

    public static void Clear() => s_Data.Data.Clear();

    public static void Record(int tagHash, SpatialShape shape, int resultCount, int treeCount = 0)
    {
      ref var data = ref s_Data.Data;
      if (data.Length >= k_MaxEntries)
        return;
      data.Add(
        new SpatialQueryDebugEntry
        {
          tagHash = tagHash,
          shape = shape,
          resultCount = resultCount,
          treeCount = treeCount,
        }
      );
    }
  }
}
