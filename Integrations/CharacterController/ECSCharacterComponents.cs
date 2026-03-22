using Unity.Entities;
using Unity.Mathematics;
using UnityJS.Entities.Core;

public struct FixedInputEvent
{
  byte m_WasEverSet;
  uint m_LastSetTick;

  public void Set(uint tick)
  {
    m_LastSetTick = tick;
    m_WasEverSet = 1;
  }

  public bool IsSet(uint tick)
  {
    return m_WasEverSet == 1 && tick == m_LastSetTick;
  }
}

/// <summary>Lua-writable movement intent produced by input scripts.</summary>
[JsBridge]
public struct ECSCharacterControl : IComponentData
{
  /// <summary>Desired movement direction in world space (Y is up).</summary>
  public float3 moveVector;

  /// <summary>Whether a jump was requested this frame.</summary>
  public bool jump;

  /// <summary>Whether sprinting is active this frame.</summary>
  public bool sprint;
}

public struct ECSCharacterFixedInput : IComponentData
{
  public FixedInputEvent jumpEvent;
}

/// <summary>Runtime character movement stats, readable and writable from Lua.</summary>
[JsBridge]
public struct ECSCharacterStats : IComponentData
{
  /// <summary>Maximum walking speed in units per second.</summary>
  public float maxSpeed;

  /// <summary>Maximum sprinting speed in units per second.</summary>
  public float sprintSpeed;

  /// <summary>Ground acceleration in units per second squared.</summary>
  public float acceleration;

  /// <summary>Aerial acceleration in units per second squared.</summary>
  public float airAcceleration;

  /// <summary>Ground drag factor applied each frame.</summary>
  public float drag;

  /// <summary>Air drag factor applied each frame.</summary>
  public float airDrag;

  /// <summary>Upward impulse applied on jump.</summary>
  public float jumpForce;

  /// <summary>Gravity acceleration (negative = downward).</summary>
  public float gravity;

  /// <summary>How fast the character rotates to face movement direction.</summary>
  public float rotationSpeed;

  /// <summary>Multiplicative modifier applied to final movement speed.</summary>
  public float speedMultiplier;

  /// <summary>Current stamina points remaining.</summary>
  public float stamina;

  /// <summary>Maximum stamina capacity.</summary>
  public float maxStamina;

  /// <summary>Number of jumps performed since last grounded.</summary>
  public int jumpCount;

  /// <summary>Maximum number of jumps allowed before landing.</summary>
  public int maxJumps;

  public static ECSCharacterStats Default()
  {
    return new ECSCharacterStats
    {
      maxSpeed = 10f,
      sprintSpeed = 16f,
      acceleration = 15f,
      airAcceleration = 50f,
      drag = 0f,
      airDrag = 0f,
      jumpForce = 10f,
      gravity = -30f,
      rotationSpeed = 25f,
      speedMultiplier = 1f,
      stamina = 100f,
      maxStamina = 100f,
      jumpCount = 0,
      maxJumps = 2,
    };
  }
}

/// <summary>Read-only character physics state exposed to Lua.</summary>
[JsBridge]
public struct ECSCharacterState : IComponentData
{
  /// <summary>Whether the character is currently touching ground.</summary>
  public bool isGrounded;

  /// <summary>Current velocity vector in world space.</summary>
  public float3 velocity;

  /// <summary>Whether the character was grounded on the previous frame.</summary>
  public bool wasGroundedLastFrame;
}
