using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.CharacterController;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Fixed-rate bridge: consumes control.jump flag → FixedInputEvent, then clears jump.
/// Runs at the start of FixedStep so the event is ready for physics.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct ECSCharacterInputBridgeSystem : ISystem
{
  [BurstCompile]
  public void OnCreate(ref SystemState state)
  {
    state.RequireForUpdate<FixedTickSystem.Singleton>();
  }

  [BurstCompile]
  public void OnUpdate(ref SystemState state)
  {
    var tick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick;

    foreach (
      var (control, fixedInput) in SystemAPI.Query<
        RefRW<ECSCharacterControl>,
        RefRW<ECSCharacterFixedInput>
      >()
    )
      if (control.ValueRO.jump)
      {
        fixedInput.ValueRW.jumpEvent.Set(tick);
        control.ValueRW.jump = false;
      }
  }
}

[UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
[BurstCompile]
public partial struct ECSCharacterPhysicsUpdateSystem : ISystem
{
  EntityQuery m_CharacterQuery;
  ECSCharacterUpdateContext m_Context;
  KinematicCharacterUpdateContext m_BaseContext;

  [BurstCompile]
  public void OnCreate(ref SystemState state)
  {
    m_CharacterQuery = KinematicCharacterUtilities
      .GetBaseCharacterQueryBuilder()
      .WithAll<ECSCharacterControl, ECSCharacterStats, ECSCharacterState, ECSCharacterFixedInput>()
      .Build(ref state);

    m_Context = new ECSCharacterUpdateContext();
    m_BaseContext = new KinematicCharacterUpdateContext();
    m_BaseContext.OnSystemCreate(ref state);

    state.RequireForUpdate(m_CharacterQuery);
    state.RequireForUpdate<PhysicsWorldSingleton>();
    state.RequireForUpdate<FixedTickSystem.Singleton>();
  }

  [BurstCompile]
  public void OnUpdate(ref SystemState state)
  {
    m_BaseContext.OnSystemUpdate(
      ref state,
      SystemAPI.Time,
      SystemAPI.GetSingleton<PhysicsWorldSingleton>()
    );

    m_Context.fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick;

    var job = new ECSCharacterPhysicsUpdateJob { context = m_Context, baseContext = m_BaseContext };
    job.ScheduleParallel();
  }

  [BurstCompile]
  [WithAll(typeof(Simulate))]
  public partial struct ECSCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
  {
    public ECSCharacterUpdateContext context;
    public KinematicCharacterUpdateContext baseContext;

    void Execute(
      Entity entity,
      RefRW<LocalTransform> localTransform,
      RefRW<KinematicCharacterProperties> characterProperties,
      RefRW<KinematicCharacterBody> characterBody,
      RefRW<PhysicsCollider> physicsCollider,
      RefRW<ECSCharacterControl> control,
      RefRO<ECSCharacterStats> stats,
      RefRW<ECSCharacterState> charState,
      RefRO<ECSCharacterFixedInput> fixedInput,
      DynamicBuffer<KinematicCharacterHit> hitsBuffer,
      DynamicBuffer<StatefulKinematicCharacterHit> statefulHitsBuffer,
      DynamicBuffer<KinematicCharacterDeferredImpulse> deferredImpulsesBuffer,
      DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits
    )
    {
      var processor = new ECSCharacterProcessor
      {
        CharacterDataAccess = new KinematicCharacterDataAccess(
          entity,
          localTransform,
          characterProperties,
          characterBody,
          physicsCollider,
          hitsBuffer,
          statefulHitsBuffer,
          deferredImpulsesBuffer,
          velocityProjectionHits
        ),
        control = control,
        stats = stats,
        state = charState,
        fixedInput = fixedInput,
      };
      processor.PhysicsUpdate(ref context, ref baseContext);
    }

    public bool OnChunkBegin(
      in ArchetypeChunk chunk,
      int unfilteredChunkIndex,
      bool useEnabledMask,
      in v128 chunkEnabledMask
    )
    {
      baseContext.EnsureCreationOfTmpCollections();
      return true;
    }

    public void OnChunkEnd(
      in ArchetypeChunk chunk,
      int unfilteredChunkIndex,
      bool useEnabledMask,
      in v128 chunkEnabledMask,
      bool chunkWasExecuted
    ) { }
  }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
[BurstCompile]
public partial struct ECSCharacterVariableUpdateSystem : ISystem
{
  EntityQuery m_CharacterQuery;
  ECSCharacterUpdateContext m_Context;
  KinematicCharacterUpdateContext m_BaseContext;

  [BurstCompile]
  public void OnCreate(ref SystemState state)
  {
    m_CharacterQuery = KinematicCharacterUtilities
      .GetBaseCharacterQueryBuilder()
      .WithAll<ECSCharacterControl, ECSCharacterStats, ECSCharacterState, ECSCharacterFixedInput>()
      .Build(ref state);

    m_Context = new ECSCharacterUpdateContext();
    m_BaseContext = new KinematicCharacterUpdateContext();
    m_BaseContext.OnSystemCreate(ref state);

    state.RequireForUpdate(m_CharacterQuery);
    state.RequireForUpdate<PhysicsWorldSingleton>();
  }

  [BurstCompile]
  public void OnUpdate(ref SystemState state)
  {
    m_BaseContext.OnSystemUpdate(
      ref state,
      SystemAPI.Time,
      SystemAPI.GetSingleton<PhysicsWorldSingleton>()
    );

    var job = new ECSCharacterVariableUpdateJob
    {
      context = m_Context,
      baseContext = m_BaseContext,
    };
    job.ScheduleParallel();
  }

  [BurstCompile]
  [WithAll(typeof(Simulate))]
  public partial struct ECSCharacterVariableUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
  {
    public ECSCharacterUpdateContext context;
    public KinematicCharacterUpdateContext baseContext;

    void Execute(
      Entity entity,
      RefRW<LocalTransform> localTransform,
      RefRW<KinematicCharacterProperties> characterProperties,
      RefRW<KinematicCharacterBody> characterBody,
      RefRW<PhysicsCollider> physicsCollider,
      RefRW<ECSCharacterControl> control,
      RefRO<ECSCharacterStats> stats,
      RefRW<ECSCharacterState> charState,
      RefRO<ECSCharacterFixedInput> fixedInput,
      DynamicBuffer<KinematicCharacterHit> hitsBuffer,
      DynamicBuffer<StatefulKinematicCharacterHit> statefulHitsBuffer,
      DynamicBuffer<KinematicCharacterDeferredImpulse> deferredImpulsesBuffer,
      DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits
    )
    {
      var processor = new ECSCharacterProcessor
      {
        CharacterDataAccess = new KinematicCharacterDataAccess(
          entity,
          localTransform,
          characterProperties,
          characterBody,
          physicsCollider,
          hitsBuffer,
          statefulHitsBuffer,
          deferredImpulsesBuffer,
          velocityProjectionHits
        ),
        control = control,
        stats = stats,
        state = charState,
        fixedInput = fixedInput,
      };
      processor.VariableUpdate(ref context, ref baseContext);
    }

    public bool OnChunkBegin(
      in ArchetypeChunk chunk,
      int unfilteredChunkIndex,
      bool useEnabledMask,
      in v128 chunkEnabledMask
    )
    {
      baseContext.EnsureCreationOfTmpCollections();
      return true;
    }

    public void OnChunkEnd(
      in ArchetypeChunk chunk,
      int unfilteredChunkIndex,
      bool useEnabledMask,
      in v128 chunkEnabledMask,
      bool chunkWasExecuted
    ) { }
  }
}
