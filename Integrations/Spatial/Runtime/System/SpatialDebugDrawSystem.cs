using Drawing;
using StoredPrefs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace MiniSpatial
{
  [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
  [UpdateAfter(typeof(SpatialTriggerSystem))]
  public partial struct SpatialDebugDrawSystem : ISystem
  {
    static readonly FixedString32Bytes k_Key = "debug.spatial";

    public void OnUpdate(ref SystemState state)
    {
      if (!PrefsStore.IsCreated || !PrefsStore.IsSet(in k_Key))
      {
        SpatialQueryDebug.Clear();
        return;
      }

      var fixedDt = SystemAPI.Time.DeltaTime;
      using var _ = Draw.ingame.WithDuration(fixedDt);

      // Draw agents
      foreach (var (agent, ltw) in SystemAPI.Query<RefRO<SpatialAgent>, RefRO<LocalToWorld>>())
      {
        var a = agent.ValueRO;
        var m = ltw.ValueRO.Value;

        using (Draw.ingame.WithMatrix((Matrix4x4)m))
        {
          if (a.shape.type == SpatialShapeType.Sphere)
          {
            var radius = math.sqrt(a.shape.sphere.radiusSq);
            Draw.ingame.WireSphere(a.shape.sphere.center, radius, Color.green);
          }
          else
          {
            Draw.ingame.WireBox(a.shape.box.center, a.shape.box.halfExtents * 2, Color.cyan);
          }
        }

        var worldCenter = a.shape.Transform(m).Center;
        var label = new FixedString64Bytes();
        label.Append((FixedString32Bytes)"tag:");
        label.Append(a.tag);
        Draw.ingame.Label2D(worldCenter, ref label, 12f);
      }

      // Draw queries from this frame
      var queryColor = new Color(1f, 0.4f, 0f, 0.8f); // orange
      for (int i = 0; i < SpatialQueryDebug.Count; i++)
      {
        var q = SpatialQueryDebug.Get(i);

        float3 qCenter;
        if (q.shape.type == SpatialShapeType.Sphere)
        {
          qCenter = q.shape.sphere.center;
          var radius = math.sqrt(q.shape.sphere.radiusSq);
          Draw.ingame.WireSphere(qCenter, radius, queryColor);
        }
        else
        {
          qCenter = q.shape.box.center;
          Draw.ingame.WireBox(qCenter, q.shape.box.halfExtents * 2, queryColor);
        }

        var qlabel = new FixedString128Bytes();
        qlabel.Append((FixedString32Bytes)"q:");
        qlabel.Append(q.tagHash);
        if (q.resultCount < 0)
        {
          qlabel.Append((FixedString32Bytes)" NO TREE");
        }
        else
        {
          qlabel.Append((FixedString32Bytes)" hits:");
          qlabel.Append(q.resultCount);
          qlabel.Append((FixedString32Bytes)" tree:");
          qlabel.Append(q.treeCount);
        }
        Draw.ingame.Label2D(qCenter, ref qlabel, 12f);
      }

      SpatialQueryDebug.Clear();
    }
  }
}
