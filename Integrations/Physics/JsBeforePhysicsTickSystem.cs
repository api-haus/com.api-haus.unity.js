namespace UnityJS.Integration.Physics
{
  using Unity.Entities;
  using Unity.Physics.Systems;
  using UnityJS.Entities.Components;
  using UnityJS.Entities.Systems.Tick;

  [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
  [UpdateBefore(typeof(PhysicsSystemGroup))]
  public partial class JsBeforePhysicsTickSystem : JsTickSystemBase
  {
    protected override JsTickGroup GetTickGroup()
    {
      return JsTickGroup.BeforePhysics;
    }
  }
}
