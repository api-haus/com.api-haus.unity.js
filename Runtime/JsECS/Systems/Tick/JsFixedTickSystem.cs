namespace UnityJS.Entities.Systems.Tick
{
  using Components;
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
