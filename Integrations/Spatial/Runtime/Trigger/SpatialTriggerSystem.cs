using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace MiniSpatial
{
  [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
  [UpdateAfter(typeof(SpatialQuerySystem))]
  [BurstCompile]
  public partial struct SpatialTriggerSystem : ISystem
  {
    BufferLookup<StatefulSpatialOverlap> m_OverlapLookup;
    BufferLookup<PreviousSpatialOverlap> m_PreviousLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
      m_OverlapLookup = state.GetBufferLookup<StatefulSpatialOverlap>();
      m_PreviousLookup = state.GetBufferLookup<PreviousSpatialOverlap>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
      m_OverlapLookup.Update(ref state);
      m_PreviousLookup.Update(ref state);

      var currentList = new NativeList<Entity>(32, Allocator.Temp);

      foreach (
        var (trigger, ltw, entity) in SystemAPI
          .Query<RefRO<SpatialTrigger>, RefRO<LocalToWorld>>()
          .WithAll<StatefulSpatialOverlap, PreviousSpatialOverlap>()
          .WithEntityAccess()
      )
      {
        var worldShape = trigger.ValueRO.shape.Transform(ltw.ValueRO.Value);

        currentList.Clear();
        var query = new ShapeQuery { shape = worldShape, results = currentList };
        SpatialQuery.Range(trigger.ValueRO.targetTag, ref query);

        // Filter self
        for (int i = currentList.Length - 1; i >= 0; i--)
        {
          if (currentList[i] == entity)
          {
            currentList.RemoveAtSwapBack(i);
          }
        }

        // Sort by Entity.Index for two-pointer merge
        currentList.Sort(new EntityComparer());

        var overlapBuf = m_OverlapLookup[entity];
        var previousBuf = m_PreviousLookup[entity];

        overlapBuf.Clear();

        // Two-pointer merge: current (sorted) vs previous (sorted)
        int ci = 0,
          pi = 0;
        int cLen = currentList.Length;
        int pLen = previousBuf.Length;

        while (ci < cLen && pi < pLen)
        {
          var ce = currentList[ci];
          var pe = previousBuf[pi].other;

          if (ce.Index < pe.Index)
          {
            overlapBuf.Add(
              new StatefulSpatialOverlap { other = ce, state = SpatialEventState.Enter }
            );
            ci++;
          }
          else if (ce.Index > pe.Index)
          {
            overlapBuf.Add(
              new StatefulSpatialOverlap { other = pe, state = SpatialEventState.Exit }
            );
            pi++;
          }
          else
          {
            overlapBuf.Add(
              new StatefulSpatialOverlap { other = ce, state = SpatialEventState.Stay }
            );
            ci++;
            pi++;
          }
        }

        // Remaining current = Enter
        while (ci < cLen)
        {
          overlapBuf.Add(
            new StatefulSpatialOverlap { other = currentList[ci], state = SpatialEventState.Enter }
          );
          ci++;
        }

        // Remaining previous = Exit
        while (pi < pLen)
        {
          overlapBuf.Add(
            new StatefulSpatialOverlap
            {
              other = previousBuf[pi].other,
              state = SpatialEventState.Exit,
            }
          );
          pi++;
        }

        // Copy current to previous for next frame
        previousBuf.Clear();
        for (int i = 0; i < currentList.Length; i++)
        {
          previousBuf.Add(new PreviousSpatialOverlap { other = currentList[i] });
        }
      }

      currentList.Dispose();
    }

    struct EntityComparer : IComparer<Entity>
    {
      public int Compare(Entity a, Entity b) => a.Index.CompareTo(b.Index);
    }
  }
}
