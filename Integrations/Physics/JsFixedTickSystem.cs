namespace UnityJS.Integration.Physics
{
  using Unity.Entities;
  using Unity.Physics.Systems;
  using UnityJS.Entities.Components;
  using UnityJS.Entities.Systems.Tick;

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
