namespace UnityJS.Integration.Physics
{
  using UnityJS.Entities.Components;
  using UnityJS.Entities.Systems.Tick;
  using Unity.Entities;
  using Unity.Physics.Systems;

  [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
  [UpdateAfter(typeof(PhysicsSystemGroup))]
  public partial class JsFixedTickSystem : JsTickSystemBase
  {
    protected override JsTickGroup GetTickGroup()
    {
      return JsTickGroup.Fixed;
    }
  }
}
