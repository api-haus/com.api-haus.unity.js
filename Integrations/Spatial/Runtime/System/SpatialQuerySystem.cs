using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace MiniSpatial
{
  [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
  [UpdateAfter(typeof(PhysicsSystemGroup))]
  [BurstCompile]
  public partial struct SpatialQuerySystem : ISystem
  {
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
      ref var store = ref SpatialQuery.TreeStore;
      DisposeStore(ref store);

      int count = 0;
      foreach (var (_, _) in SystemAPI.Query<RefRO<SpatialAgent>, RefRO<LocalToWorld>>())
        count++;

      if (count == 0)
        return;

      var buckets = new NativeParallelMultiHashMap<int, SpatialEntry>(count, Allocator.Temp);
      var uniqueTags = new NativeHashSet<int>(16, Allocator.Temp);

      foreach (
        var (agent, ltw, entity) in SystemAPI
          .Query<RefRO<SpatialAgent>, RefRO<LocalToWorld>>()
          .WithEntityAccess()
      )
      {
        int tag = agent.ValueRO.tag;
        var worldShape = agent.ValueRO.shape.Transform(ltw.ValueRO.Value);

        buckets.Add(
          tag,
          new SpatialEntry
          {
            matrix = ltw.ValueRO.Value,
            entity = entity,
            shape = worldShape,
          }
        );
        uniqueTags.Add(tag);
      }

      store.trees = new NativeHashMap<int, KDTree>(uniqueTags.Count, Allocator.Persistent);

      foreach (var tag in uniqueTags)
      {
        int tagCount = buckets.CountValuesForKey(tag);
        var entries = new NativeArray<SpatialEntry>(tagCount, Allocator.Temp);
        int idx = 0;
        foreach (var entry in buckets.GetValuesForKey(tag))
          entries[idx++] = entry;

        store.trees[tag] = new KDTree(entries, Allocator.Persistent);
        entries.Dispose();
      }

      store.isValid = true;

      uniqueTags.Dispose();
      buckets.Dispose();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
      ref var store = ref SpatialQuery.TreeStore;
      DisposeStore(ref store);
    }

    private static void DisposeStore(ref SpatialTreeStore store)
    {
      if (!store.isValid)
        return;

      foreach (var kv in store.trees)
        kv.Value.Dispose();

      store.trees.Dispose();
      store.isValid = false;
    }
  }
}
