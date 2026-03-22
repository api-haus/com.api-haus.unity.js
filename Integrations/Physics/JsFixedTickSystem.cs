namespace UnityJS.Integration.Physics
{
  using Unity.Entities;
  using Unity.Physics.Systems;
  using UnityJS.Entities.Components;
  using UnityJS.Entities.Systems.Tick;

  [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
  [UpdateAfter(typeof(PhysicsSystemGroup))]
  public partial struct JsFixedTickSystem : ISystem
  {
    JsTickSystemHelper.State m_TickState;

    public void OnCreate(ref SystemState state)
    {
      JsTickSystemHelper.OnCreate(ref state, out m_TickState);
    }

    public void OnUpdate(ref SystemState state)
    {
      if (!SystemAPI.TryGetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>(out var ecbSingleton))
        return;
      var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
      JsTickSystemHelper.OnUpdate(ref state, ref m_TickState, JsTickGroup.Fixed, ecb);
    }
  }
}
