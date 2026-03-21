using Unity.Burst;
using Unity.CharacterController;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using static Unity.Mathematics.math;

public struct ECSCharacterUpdateContext
{
  public uint fixedTick;
}

[BurstCompile]
public struct ECSCharacterProcessor : IKinematicCharacterProcessor<ECSCharacterUpdateContext>
{
  public KinematicCharacterDataAccess CharacterDataAccess;
  public RefRW<ECSCharacterControl> control;
  public RefRO<ECSCharacterStats> stats;
  public RefRW<ECSCharacterState> state;
  public RefRO<ECSCharacterFixedInput> fixedInput;

  public void PhysicsUpdate(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext
  )
  {
    ref var characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;
    ref var characterPosition = ref CharacterDataAccess.LocalTransform.ValueRW.Position;
    var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();

    KinematicCharacterUtilities.Update_Initialize(
      in this,
      ref context,
      ref baseContext,
      ref characterBody,
      CharacterDataAccess.CharacterHitsBuffer,
      CharacterDataAccess.DeferredImpulsesBuffer,
      CharacterDataAccess.VelocityProjectionHits,
      baseContext.Time.DeltaTime
    );
    KinematicCharacterUtilities.Update_ParentMovement(
      in this,
      ref context,
      ref baseContext,
      CharacterDataAccess.CharacterEntity,
      ref characterBody,
      CharacterDataAccess.CharacterProperties.ValueRO,
      CharacterDataAccess.PhysicsCollider.ValueRO,
      CharacterDataAccess.LocalTransform.ValueRO,
      ref characterPosition,
      characterBody.WasGroundedBeforeCharacterUpdate
    );
    KinematicCharacterUtilities.Update_Grounding(
      in this,
      ref context,
      ref baseContext,
      ref characterBody,
      CharacterDataAccess.CharacterEntity,
      CharacterDataAccess.CharacterProperties.ValueRO,
      CharacterDataAccess.PhysicsCollider.ValueRO,
      CharacterDataAccess.LocalTransform.ValueRO,
      CharacterDataAccess.VelocityProjectionHits,
      CharacterDataAccess.CharacterHitsBuffer,
      ref characterPosition
    );

    HandleVelocityControl(ref context, ref baseContext);

    KinematicCharacterUtilities.Update_PreventGroundingFromFutureSlopeChange(
      in this,
      ref context,
      ref baseContext,
      CharacterDataAccess.CharacterEntity,
      ref characterBody,
      CharacterDataAccess.CharacterProperties.ValueRO,
      CharacterDataAccess.PhysicsCollider.ValueRO,
      in stepHandling
    );
    KinematicCharacterUtilities.Update_GroundPushing(
      in this,
      ref context,
      ref baseContext,
      ref characterBody,
      CharacterDataAccess.CharacterProperties.ValueRO,
      CharacterDataAccess.LocalTransform.ValueRO,
      CharacterDataAccess.DeferredImpulsesBuffer,
      new float3(0, stats.ValueRO.gravity, 0)
    );
    KinematicCharacterUtilities.Update_MovementAndDecollisions(
      in this,
      ref context,
      ref baseContext,
      CharacterDataAccess.CharacterEntity,
      ref characterBody,
      CharacterDataAccess.CharacterProperties.ValueRO,
      CharacterDataAccess.PhysicsCollider.ValueRO,
      CharacterDataAccess.LocalTransform.ValueRO,
      CharacterDataAccess.VelocityProjectionHits,
      CharacterDataAccess.CharacterHitsBuffer,
      CharacterDataAccess.DeferredImpulsesBuffer,
      ref characterPosition
    );
    KinematicCharacterUtilities.Update_MovingPlatformDetection(ref baseContext, ref characterBody);
    KinematicCharacterUtilities.Update_ParentMomentum(
      ref baseContext,
      ref characterBody,
      CharacterDataAccess.LocalTransform.ValueRO.Position
    );
    KinematicCharacterUtilities.Update_ProcessStatefulCharacterHits(
      CharacterDataAccess.CharacterHitsBuffer,
      CharacterDataAccess.StatefulHitsBuffer
    );

    // Write state for Lua readback
    ref var charState = ref state.ValueRW;
    charState.wasGroundedLastFrame = characterBody.WasGroundedBeforeCharacterUpdate;
    charState.isGrounded = characterBody.IsGrounded;
    charState.velocity = characterBody.RelativeVelocity;
  }

  void HandleVelocityControl(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext
  )
  {
    var deltaTime = baseContext.Time.DeltaTime;
    ref var characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;
    var s = stats.ValueRO;
    ref var ctrl = ref control.ValueRW;

    var moveVector = ctrl.moveVector;

    // Rotate move input to account for parent rotation
    if (characterBody.ParentEntity != Entity.Null)
    {
      moveVector = rotate(characterBody.RotationFromParent, moveVector);
      characterBody.RelativeVelocity = rotate(
        characterBody.RotationFromParent,
        characterBody.RelativeVelocity
      );
    }

    var effectiveSpeed = (ctrl.sprint ? s.sprintSpeed : s.maxSpeed) * s.speedMultiplier;

    if (characterBody.IsGrounded)
    {
      var targetVelocity = MathUtilities.ClampToMaxLength(moveVector, 1f) * effectiveSpeed;
      CharacterControlUtilities.StandardGroundMove_Interpolated(
        ref characterBody.RelativeVelocity,
        targetVelocity,
        s.acceleration,
        deltaTime,
        characterBody.GroundingUp,
        characterBody.GroundHit.Normal
      );
    }
    else
    {
      var airAccel = MathUtilities.ClampToMaxLength(moveVector, 1f) * s.airAcceleration;
      CharacterControlUtilities.StandardAirMove(
        ref characterBody.RelativeVelocity,
        airAccel,
        effectiveSpeed,
        characterBody.GroundingUp,
        deltaTime,
        false
      );

      CharacterControlUtilities.AccelerateVelocity(
        ref characterBody.RelativeVelocity,
        new float3(0, s.gravity, 0),
        deltaTime
      );

      CharacterControlUtilities.ApplyDragToVelocity(
        ref characterBody.RelativeVelocity,
        deltaTime,
        s.airDrag
      );
    }

    // Jump — outside grounded check so air jumps (double jump) work
    if (fixedInput.ValueRO.jumpEvent.IsSet(context.fixedTick))
      CharacterControlUtilities.StandardJump(
        ref characterBody,
        characterBody.GroundingUp * s.jumpForce,
        true,
        characterBody.GroundingUp
      );
  }

  public void VariableUpdate(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext
  )
  {
    ref var characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;
    ref var characterRotation = ref CharacterDataAccess.LocalTransform.ValueRW.Rotation;
    var s = stats.ValueRO;
    var moveVector = control.ValueRO.moveVector;

    KinematicCharacterUtilities.AddVariableRateRotationFromFixedRateRotation(
      ref characterRotation,
      characterBody.RotationFromParent,
      baseContext.Time.DeltaTime,
      characterBody.LastPhysicsUpdateDeltaTime
    );

    if (lengthsq(moveVector) > 0f)
      CharacterControlUtilities.SlerpRotationTowardsDirectionAroundUp(
        ref characterRotation,
        baseContext.Time.DeltaTime,
        normalizesafe(moveVector),
        MathUtilities.GetUpFromRotation(characterRotation),
        s.rotationSpeed
      );
  }

  public void UpdateGroundingUp(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext
  )
  {
    ref var characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;
    KinematicCharacterUtilities.Default_UpdateGroundingUp(
      ref characterBody,
      CharacterDataAccess.LocalTransform.ValueRO.Rotation
    );
  }

  public bool CanCollideWithHit(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext,
    in BasicHit hit
  )
  {
    return PhysicsUtilities.IsCollidable(hit.Material);
  }

  public bool IsGroundedOnHit(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext,
    in BasicHit hit,
    int groundingEvaluationType
  )
  {
    var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();
    return KinematicCharacterUtilities.Default_IsGroundedOnHit(
      in this,
      ref context,
      ref baseContext,
      CharacterDataAccess.CharacterEntity,
      CharacterDataAccess.PhysicsCollider.ValueRO,
      CharacterDataAccess.CharacterBody.ValueRO,
      CharacterDataAccess.CharacterProperties.ValueRO,
      in hit,
      in stepHandling,
      groundingEvaluationType
    );
  }

  public void OnMovementHit(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext,
    ref KinematicCharacterHit hit,
    ref float3 remainingMovementDirection,
    ref float remainingMovementLength,
    float3 originalVelocityDirection,
    float hitDistance
  )
  {
    ref var characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;
    ref var characterPosition = ref CharacterDataAccess.LocalTransform.ValueRW.Position;
    var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();

    KinematicCharacterUtilities.Default_OnMovementHit(
      in this,
      ref context,
      ref baseContext,
      ref characterBody,
      CharacterDataAccess.CharacterEntity,
      CharacterDataAccess.CharacterProperties.ValueRO,
      CharacterDataAccess.PhysicsCollider.ValueRO,
      CharacterDataAccess.LocalTransform.ValueRO,
      ref characterPosition,
      CharacterDataAccess.VelocityProjectionHits,
      ref hit,
      ref remainingMovementDirection,
      ref remainingMovementLength,
      originalVelocityDirection,
      hitDistance,
      stepHandling.StepHandling,
      stepHandling.MaxStepHeight,
      stepHandling.CharacterWidthForStepGroundingCheck
    );
  }

  public void OverrideDynamicHitMasses(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext,
    ref PhysicsMass characterMass,
    ref PhysicsMass otherMass,
    BasicHit hit
  ) { }

  public void ProjectVelocityOnHits(
    ref ECSCharacterUpdateContext context,
    ref KinematicCharacterUpdateContext baseContext,
    ref float3 velocity,
    ref bool characterIsGrounded,
    ref BasicHit characterGroundHit,
    in DynamicBuffer<KinematicVelocityProjectionHit> hits,
    float3 originalVelocityDirection
  )
  {
    var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();
    KinematicCharacterUtilities.Default_ProjectVelocityOnHits(
      ref velocity,
      ref characterIsGrounded,
      ref characterGroundHit,
      in hits,
      originalVelocityDirection,
      stepHandling.ConstrainVelocityToGroundPlane,
      in CharacterDataAccess.CharacterBody.ValueRO
    );
  }
}
