using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MiniSpatial
{
  public class SpatialAgentAuthoring : MonoBehaviour
  {
    public string tag = "default";
    public SpatialShapeType shapeType = SpatialShapeType.Sphere;
    public float radius = 1f;
    public Vector3 halfExtents = Vector3.one;
    public Vector3 center;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
      Gizmos.matrix = transform.localToWorldMatrix;

      if (shapeType == SpatialShapeType.Sphere)
      {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(center, radius);
      }
      else
      {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, halfExtents * 2);
      }

      UnityEditor.Handles.Label(transform.TransformPoint(center), tag);
    }
#endif

    class Baker : Baker<SpatialAgentAuthoring>
    {
      public override void Bake(SpatialAgentAuthoring authoring)
      {
        var entity = GetEntity(TransformUsageFlags.Dynamic);

        SpatialTag spatialTag = authoring.tag;
        var c = new float3(authoring.center.x, authoring.center.y, authoring.center.z);

        var shape =
          authoring.shapeType == SpatialShapeType.Sphere
            ? SpatialShape.Sphere(authoring.radius * authoring.radius, c)
            : SpatialShape.Box(
              new float3(authoring.halfExtents.x, authoring.halfExtents.y, authoring.halfExtents.z),
              c
            );

        AddComponent(entity, new SpatialAgent { shape = shape, tag = spatialTag });
      }
    }
  }
}
