using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderLast = true)]
[BurstCompile]
public partial struct FixedTickSystem : ISystem
{
  public struct Singleton : IComponentData
  {
    public uint Tick;
  }

  public void OnCreate(ref SystemState state)
  {
    if (!SystemAPI.HasSingleton<Singleton>())
      state.EntityManager.CreateEntity(typeof(Singleton));
  }

  [BurstCompile]
  public void OnUpdate(ref SystemState state)
  {
    ref var singleton = ref SystemAPI.GetSingletonRW<Singleton>().ValueRW;
    singleton.Tick++;
  }
}
