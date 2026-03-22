namespace UnityJS.Entities.Systems.Tick
{
  using Components;
  using Unity.Entities;
  using Unity.Transforms;

  [UpdateInGroup(typeof(SimulationSystemGroup))]
  [UpdateAfter(typeof(TransformSystemGroup))]
  public partial struct JsAfterTransformTickSystem : ISystem
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
      JsTickSystemHelper.OnUpdate(ref state, ref m_TickState, JsTickGroup.AfterTransform, ecb);
    }
  }
}
